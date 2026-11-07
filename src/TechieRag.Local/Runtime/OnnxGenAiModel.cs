using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace TechieRag.Local.Runtime;

/// <summary>
/// A model loaded by <see cref="OnnxGenAiRuntime"/>: the GenAI model, its tokenizer, and a generator
/// per answer (REQ-RAG-058).
/// </summary>
/// <remarks>
/// <para><b>Off the caller's thread.</b> The prompt's prefill and every token are computed on the
/// thread pool, so an awaiting UI thread never blocks on inference.</para>
/// <para><b>Cancellation</b> is checked before each token; the generator, its parameters, the token
/// sequence and the decoding stream are disposed however the enumeration ends.</para>
/// <para><b>After <see cref="Dispose"/></b> every member and every resumed step of a generation throws
/// <see cref="ObjectDisposedException"/> before it touches a native handle (REQ-RAG-129, Sevak TR-RAG-053).
/// The provider frees the model only once no call is inside, so this is the second line of defence.</para>
/// <para><b>Decoded text</b> is cleaned of SentencePiece space markers (<see cref="DecodedText"/>).</para>
/// <para><b>JSON.</b> A schema is enforced by GenAI's <c>json_schema</c> guidance. A native build
/// without guidance support throws on it once; from then on the answer is unconstrained and relies on
/// the provider's JSON instruction and strict parsing, which is what every other provider does.</para>
/// </remarks>
internal sealed partial class OnnxGenAiModel : ILocalLlmModel
{
    private static volatile bool guidanceUnavailable;

    private readonly Config config;
    private readonly Model model;
    private readonly Tokenizer tokenizer;
    private readonly int contextSize;
    private readonly object processorLock = new();
    private MultiModalProcessor? processor;
    private int? lastImagePromptTokens;
    private volatile bool disposed;

    /// <summary>Loads a model folder on the CPU.</summary>
    /// <param name="modelDirectory">The folder with <c>genai_config.json</c>.</param>
    /// <param name="contextSize">The context size in tokens; the most tokens one generation holds.</param>
    /// <param name="threads">Intra-op CPU threads, or null for ONNX Runtime's choice.</param>
    public OnnxGenAiModel(string modelDirectory, int contextSize, int? threads)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextSize);
        this.contextSize = contextSize;
        config = new Config(modelDirectory);
        try
        {
            config.ClearProviders();
            config.Overlay(OverlayJson(contextSize, threads));
            model = new Model(config);
            tokenizer = new Tokenizer(model);
        }
        catch
        {
            model?.Dispose();
            config.Dispose();
            throw;
        }
    }

    /// <summary>Gets whether this process's native build turned out to lack guidance support.</summary>
    internal static bool GuidanceUnavailable => guidanceUnavailable;

    /// <inheritdoc/>
    public int CountTokens(string text)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return OnnxGenAiTokenizer.Count(tokenizer, text);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        LocalGenerationSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        using var sequences = tokenizer.Encode(prompt);
        var search = OnnxGenAiSearchOptions.From(settings, sequences[0].Length, contextSize);
        using var parameters = CreateParameters(search);
        using var generator = new Generator(model, parameters);
        await Task.Run(() => generator.AppendTokenSequences(sequences), cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed, this);
        using var stream = tokenizer.CreateStream();
        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = await Task.Run(() => NextToken(generator), cancellationToken).ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(disposed, this);
            yield return DecodedText.Clean(stream.Decode(token));
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    /// <inheritdoc/>
    public int? LastImagePromptTokens => lastImagePromptTokens;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>The prompt and the pictures go through GenAI's <see cref="MultiModalProcessor"/>, which turns each
    /// picture into the vision model's input and the prompt's image markers into its tokens, and the result
    /// is handed to the generator with <see cref="Generator.SetInputs"/> (REQ-RAG-147 / BRD-205).</para>
    /// <para>One picture is loaded from memory. Several are written to a private temporary folder on this
    /// machine, loaded by path and deleted as soon as the processor has read them, because GenAI 0.16.0's
    /// managed API loads only one picture from memory. Nothing is sent anywhere.</para>
    /// </remarks>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        IReadOnlyList<LocalImage> images,
        int promptTokens,
        LocalGenerationSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(settings);
        if (images.Count == 0)
        {
            throw new ArgumentException("A generation with pictures needs at least one picture.", nameof(images));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        var multiModal = Processor();
        using var inputs = await Task.Run(() => ProcessImages(multiModal, prompt, images), cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed, this);
        // A picture's token count depends on its size and is known only once the processor has run, so the
        // generation may use the whole context; the provider stops the answer at its MaxTokens.
        var search = OnnxGenAiSearchOptions.From(settings, Math.Max(promptTokens, contextSize), contextSize);
        using var parameters = CreateParameters(search);
        using var generator = new Generator(model, parameters);
        await Task.Run(() => SetInputs(generator, inputs), cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(disposed, this);
        lastImagePromptTokens = ReadPromptLength(generator);
        using var stream = multiModal.CreateStream();
        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = await Task.Run(() => NextToken(generator), cancellationToken).ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(disposed, this);
            yield return DecodedText.Clean(stream.Decode(token));
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// GenAI 0.16.0's <c>Tokenizer.ApplyChatTemplate</c> with no template text uses the model folder's own
    /// (<c>chat_template.jinja</c>, else <c>tokenizer_config.json</c>'s <c>chat_template</c>). The start
    /// marker it writes (Gemma's <c>&lt;bos&gt;</c>) is kept by <c>Encode</c> as that one special token, not
    /// doubled: measured on Arm's Gemma 3 1B on 2026-09-25, where a prompt without it made most answers empty.
    /// </remarks>
    public string ApplyChatTemplate(string messagesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messagesJson);
        ObjectDisposedException.ThrowIf(disposed, this);
        return tokenizer.ApplyChatTemplate(null!, messagesJson, null!, true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (processorLock)
        {
            processor?.Dispose();
            processor = null;
        }

        tokenizer.Dispose();
        model.Dispose();
        config.Dispose();
    }

    /// <summary>
    /// Builds the configuration overlay: the default <c>max_length</c> is the context size, and the
    /// thread count, when given, is ONNX Runtime's intra-op thread count.
    /// </summary>
    /// <param name="contextSize">The context size.</param>
    /// <param name="threads">The thread count, or null.</param>
    /// <returns>The overlay JSON.</returns>
    internal static string OverlayJson(int contextSize, int? threads)
    {
        var search = new JsonObject { ["max_length"] = contextSize };
        var overlay = new JsonObject { ["search"] = search };
        if (threads is > 0)
        {
            overlay["model"] = new JsonObject
            {
                ["decoder"] = new JsonObject { ["session_options"] = new JsonObject { ["intra_op_num_threads"] = threads.Value } }
            };
        }

        return overlay.ToJsonString();
    }

    private static NamedTensors ProcessImages(MultiModalProcessor multiModal, string prompt, IReadOnlyList<LocalImage> images)
    {
        if (images.Count == 1)
        {
            using var one = Images.Load(images[0].Bytes);
            return multiModal.ProcessImages(prompt, one);
        }

        var folder = Path.Combine(Path.GetTempPath(), "techierag-local-images", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var paths = images.Select((image, index) => WriteImage(folder, index, image)).ToArray();
            using var loaded = Images.Load(paths);
            return multiModal.ProcessImages(prompt, loaded);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A virus scanner may hold a file a moment longer; the folder is under the user's temp path.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }
        }
    }

    private static string WriteImage(string folder, int index, LocalImage image)
    {
        var path = Path.Combine(folder, $"{index + 1}{ExtensionFor(image.MediaType)}");
        File.WriteAllBytes(path, image.Bytes);
        return path;
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".png"
    };

    private static void SetInputs(Generator generator, NamedTensors inputs)
    {
        try
        {
            generator.SetInputs(inputs);
        }
        catch (OnnxRuntimeGenAIException exception) when (exception.Message.Contains("exceeds max length", StringComparison.Ordinal))
        {
            var match = InputIdsSize().Match(exception.Message);
            throw new LocalImagePromptTooLongException(match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null, exception);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"input_ids size \((\d+)\)")]
    private static partial System.Text.RegularExpressions.Regex InputIdsSize();

    private static int? ReadPromptLength(Generator generator)
    {
        var count = (long)generator.TokenCount();
        return count is > 0 and <= int.MaxValue ? (int)count : null;
    }

    private MultiModalProcessor Processor()
    {
        lock (processorLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return processor ??= new MultiModalProcessor(model);
        }
    }

    private static int NextToken(Generator generator)
    {
        generator.GenerateNextToken();
        return generator.GetNextTokens()[0];
    }

    private GeneratorParams CreateParameters(OnnxGenAiSearchOptions search)
    {
        var parameters = new GeneratorParams(model);
        try
        {
            parameters.SetSearchOption("max_length", search.MaxLength);
            parameters.SetSearchOption("do_sample", search.DoSample);
            SetIfPresent(parameters, "temperature", search.Temperature);
            SetIfPresent(parameters, "top_p", search.TopP);
            SetIfPresent(parameters, "random_seed", search.Seed);
            SetIfPresent(parameters, "repetition_penalty", search.RepetitionPenalty);
            ApplyGuidance(parameters, search.JsonSchema);
            return parameters;
        }
        catch
        {
            parameters.Dispose();
            throw;
        }
    }

    private static void SetIfPresent(GeneratorParams parameters, string name, double? value)
    {
        if (value is { } set)
        {
            parameters.SetSearchOption(name, set);
        }
    }

    private static void ApplyGuidance(GeneratorParams parameters, string? jsonSchema)
    {
        if (jsonSchema is null || guidanceUnavailable)
        {
            return;
        }

        try
        {
            parameters.SetGuidance("json_schema", jsonSchema, false);
        }
        catch (OnnxRuntimeGenAIException exception) when (exception.Message.Contains("guidance", StringComparison.OrdinalIgnoreCase))
        {
            guidanceUnavailable = true;
        }
    }
}
