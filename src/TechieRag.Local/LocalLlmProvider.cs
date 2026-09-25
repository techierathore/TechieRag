using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Local.Runtime;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// An <see cref="ILlmProvider"/> that runs a language model inside the app (REQ-RAG-057…065,
/// BRD-96…105).
/// </summary>
/// <remarks>
/// <para><b>One provider, a runtime per platform underneath.</b> Everything an app can observe is
/// done here, above the internal <see cref="ILocalLlmRuntime"/>: the chat template, the stop
/// sequences, the context-length refusal, the memory check, usage and the event order. The runtime
/// only loads, tokenizes and generates, so behaviour is the same whichever runtime a platform uses
/// (REQ-RAG-058).</para>
/// <para><b>Lazy.</b> Constructing the provider touches neither the network nor the disk. The first
/// call (or <see cref="LoadAsync"/>) accepts the terms, downloads once into the model root, verifies
/// SHA-256, checks free memory, then loads.</para>
/// <para><b>One answer at a time.</b> A loaded model holds one context; concurrent calls queue.</para>
/// <para><b>Not supported:</b> tool calling (<see cref="SupportsToolCalling"/> is false until a test
/// proves otherwise, REQ-RAG-063), images, and a per-call model other than the loaded one.</para>
/// </remarks>
public sealed class LocalLlmProvider : ILlmProvider, IDisposable
{
    private const string JsonSystemPrompt = "You are a helpful assistant that responds only with valid JSON.";

    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LocalLlmOptions options;
    private readonly ILogger logger;
    private readonly ILocalLlmRuntime? runtime;
    private readonly MemoryGate memoryGate;
    private readonly LocalModelStore store;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly SemaphoreSlim generateGate = new(1, 1);
    private readonly bool isPhone;
    private ILocalLlmModel? loaded;
    private ILocalTokenizer? tokenizer;
    private bool disposed;

    /// <summary>
    /// Creates a provider for this platform; nothing is downloaded or loaded until first use.
    /// </summary>
    /// <param name="options">Model, context size, defaults and terms acceptance; null uses the platform default model.</param>
    /// <param name="logger">Optional logger.</param>
    /// <exception cref="NotSupportedException">The model is too large for Android or iOS.</exception>
    public LocalLlmProvider(LocalLlmOptions? options = null, ILogger<LocalLlmProvider>? logger = null)
        : this(options ?? new LocalLlmOptions(), logger, LocalRuntimeSelector.ForCurrentPlatform(),
            MemoryGate.ForThisDevice, LocalModelStore.Shared, LocalModel.IsPhonePlatform)
    {
    }

    /// <summary>Creates a provider over an explicit runtime, memory gate and store (the conformance suite).</summary>
    /// <param name="options">The options.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="runtime">The runtime, or null when the platform has none.</param>
    /// <param name="memoryGate">The memory check.</param>
    /// <param name="store">Where files come from.</param>
    /// <param name="isPhone">Whether to behave as on Android or iOS.</param>
    internal LocalLlmProvider(
        LocalLlmOptions options,
        ILogger? logger,
        ILocalLlmRuntime? runtime,
        MemoryGate memoryGate,
        LocalModelStore store,
        bool isPhone)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.logger = logger ?? NullLogger.Instance;
        this.runtime = runtime;
        this.memoryGate = memoryGate;
        this.store = store;
        this.isPhone = isPhone;
        Model = options.Model ?? LocalModel.DefaultFor(isPhone);
        Model.EnsureSupported(isPhone);
    }

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>Gets the model this provider runs.</summary>
    public LocalModel Model { get; }

    /// <summary>
    /// Gets the context size, in tokens, the model is loaded with: <see cref="LocalLlmOptions.ContextSize"/>
    /// or the platform's default, never more than the model supports. For a Hugging Face model the limit is
    /// known once its <c>genai_config.json</c> is on disk.
    /// </summary>
    public int ContextSize
    {
        get
        {
            var requested = options.ContextSize ?? Model.DefaultContextSize(isPhone);
            return Model.ContextLength > 0 ? Math.Min(Model.ContextLength, requested) : requested;
        }
    }

    /// <summary>Gets whether the model is loaded and ready to answer.</summary>
    public bool IsLoaded => loaded is not null;

    /// <inheritdoc/>
    public string Name => "Local";

    /// <inheritdoc/>
    public string ModelName => Model.Id;

    /// <inheritdoc/>
    /// <remarks>False until a live test proves a local model calls tools reliably (REQ-RAG-063).</remarks>
    public bool SupportsToolCalling => false;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Gets the terms the user accepts before the model downloads, with the download size when this
    /// platform's file set is known (REQ-RAG-062).
    /// </summary>
    /// <returns>The terms.</returns>
    public LocalModelTerms GetTerms()
    {
        var variant = runtime is null ? null : Model.FindVariant(runtime.Format);
        return Model.GetTerms() with { DownloadBytes = variant?.DownloadBytes ?? 0 };
    }

    /// <summary>
    /// Gets the terms the user accepts before the model downloads, with the download size. For a model
    /// named on Hugging Face this reads its licence and file list through Hugging Face's API first (no
    /// model file is requested), so the terms are complete (REQ-RAG-108).
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The terms.</returns>
    /// <exception cref="InvalidOperationException">A Hugging Face model is gated, private, missing, or not in ONNX Runtime GenAI's format.</exception>
    /// <exception cref="HttpRequestException">Hugging Face could not be reached.</exception>
    public Task<LocalModelTerms> GetTermsAsync(CancellationToken cancellationToken = default) =>
        store.GetTermsAsync(Model, runtime?.Format ?? LocalModelFormat.OnnxGenAi, cancellationToken);

    /// <summary>
    /// Accepts the terms (through <see cref="LocalLlmOptions"/>), downloads and verifies the model and
    /// loads it, so the first answer does not wait for any of it. Safe to call more than once.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download or the load.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    /// <exception cref="PlatformNotSupportedException">No runtime is available for this platform.</exception>
    /// <exception cref="LocalModelTermsNotAcceptedException">The download needs terms acceptance.</exception>
    /// <exception cref="InvalidOperationException">
    /// The files must be downloaded, the model has no default address and <c>TECHIERAG_MODEL_BASE_URL</c>
    /// is not set; nothing was requested.
    /// </exception>
    /// <exception cref="LocalModelIntegrityException">A downloaded file failed its hash check.</exception>
    /// <exception cref="LocalModelMemoryException">The device has too little free memory.</exception>
    /// <exception cref="HttpRequestException">A Hugging Face model's metadata could not be fetched.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (loaded is not null)
        {
            return;
        }

        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (loaded is not null)
            {
                return;
            }

            await store.PrepareAsync(Model, cancellationToken).ConfigureAwait(false);
            var (resolvedRuntime, variant, directory) = Resolve();
            if (variant is not null)
            {
                // TECHIERAG_MODEL_BASE_URL is for the built-in catalogue; a Hugging Face name is its own source.
                var mirror = Model.HuggingFace is null ? LocalModelStore.Mirror : null;
                await store.EnsureAsync(Model, variant, directory, options, mirror, cancellationToken).ConfigureAwait(false);
            }

            var weightBytes = variant?.DownloadBytes ?? FolderBytes(directory);
            var required = Model.EstimateMemoryBytes(weightBytes, ContextSize);
            var available = memoryGate.Ensure(Model.Id, required);
            logger.LogInformation(
                "Loading local model {ModelId} ({ContextSize}-token context; needs {Required} bytes, {Available} free)",
                Model.Id, ContextSize, required, available);

            loaded = await Task.Run(() => resolvedRuntime.Load(directory, ContextSize, options.Threads), cancellationToken)
                .ConfigureAwait(false);
            tokenizer?.Dispose();
            tokenizer = null;
        }
        finally
        {
            loadGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ChatAsync([ChatMessage.User(prompt)], options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ChatStreamAsync([ChatMessage.User(prompt)], options, cancellationToken);

    /// <inheritdoc/>
    public async Task<LlmResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        LlmStreamEvent? completed = null;
        await foreach (var streamEvent in StreamCoreAsync(messages, options, isStreaming: false, cancellationToken).ConfigureAwait(false))
        {
            if (streamEvent.Kind == LlmStreamEventKind.TextDelta)
            {
                text.Append(streamEvent.Text);
            }
            else if (streamEvent.Kind == LlmStreamEventKind.Completed)
            {
                completed = streamEvent;
            }
        }

        return new LlmResponse
        {
            Content = text.ToString(),
            Usage = completed!.Usage!,
            FinishReason = completed.FinishReason ?? "stop",
            ModelName = Model.Id
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ChatStreamEventsAsync(messages, options, cancellationToken).ToTextStreamAsync(cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        StreamCoreAsync(messages, options, isStreaming: true, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// The strict JSON schema of <typeparamref name="T"/> (<see cref="TypedAnswerSchema"/>: root never
    /// null, every property required, no others) is sent with the call and the runtime constrains decoding to it (REQ-RAG-063: ONNX Runtime GenAI's <c>json_schema</c>
    /// guidance); the answer is parsed strictly.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The answer is not JSON that parses into <typeparamref name="T"/>.</exception>
    public async Task<T> CompleteAsync<T>(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var schema = TypedAnswerSchema.For(typeof(T));
        var call = new LlmCompletionOptions
        {
            Temperature = options?.Temperature ?? 0f,
            MaxTokens = options?.MaxTokens,
            TopP = options?.TopP,
            Seed = options?.Seed,
            StopSequences = options?.StopSequences,
            SystemPrompt = options?.SystemPrompt ?? JsonSystemPrompt,
            JsonMode = true,
            JsonSchema = options?.JsonSchema ?? schema,
            Model = options?.Model
        };

        var response = await ChatAsync([ChatMessage.User(prompt)], call, cancellationToken).ConfigureAwait(false);
        var content = StripCodeFence(response.Content ?? string.Empty);
        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonReadOptions)
                ?? throw new InvalidOperationException($"The local model answered null for {typeof(T).Name}.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The local model's answer is not valid JSON for {typeof(T).Name}: {exception.Message}", exception);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Counts with the model's own tokenizer (REQ-RAG-065): the loaded model's, or the tokenizer alone
    /// when the files are on disk but the weights are not loaded. Before the first download there is no
    /// tokenizer to ask, and the count is the usual four-characters-per-token estimate.
    /// </remarks>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var counter = (ILocalTokenizer?)loaded ?? TryLoadTokenizer();
        return counter?.CountTokens(text) ?? (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>Unloads the model and frees its memory.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        loaded?.Dispose();
        tokenizer?.Dispose();
        loadGate.Dispose();
        generateGate.Dispose();
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamCoreAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? callOptions,
        bool isStreaming,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        RefuseUnsupported(callOptions);
        await LoadAsync(cancellationToken).ConfigureAwait(false);
        var model = loaded!;

        var conversation = PrepareConversation(messages, callOptions);
        var prompt = Model.ChatTemplate == LocalChatTemplate.ModelDefined
            ? model.ApplyChatTemplate(ChatTemplateFormatter.ToMessagesJson(conversation))
            : ChatTemplateFormatter.Format(Model.ChatTemplate, conversation);
        var promptTokens = model.CountTokens(prompt);
        if (promptTokens >= ContextSize)
        {
            throw new LocalPromptTooLongException(Model.Id, promptTokens, ContextSize);
        }

        var settings = BuildSettings(callOptions, ContextSize - promptTokens);
        var filter = new StopSequenceFilter(settings.StopSequences);
        await generateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var watch = Stopwatch.StartNew();
            var produced = 0;
            await foreach (var piece in model.GenerateAsync(prompt, settings, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                produced++;
                var text = filter.Push(piece);
                if (text.Length > 0)
                {
                    yield return LlmStreamEvent.FromText(text);
                }

                if (filter.Stopped || produced >= settings.MaxTokens)
                {
                    break;
                }
            }

            var rest = filter.Flush();
            if (rest.Length > 0)
            {
                yield return LlmStreamEvent.FromText(rest);
            }

            var finishReason = !filter.Stopped && produced >= settings.MaxTokens ? "length" : "stop";
            var usage = new TokenUsage { InputTokens = promptTokens, OutputTokens = produced, ModelName = Model.Id, ProviderName = Name };
            RaiseCompleted(usage, watch.Elapsed, isStreaming);
            yield return LlmStreamEvent.FromCompleted(usage, finishReason, Model.Id);
        }
        finally
        {
            generateGate.Release();
        }
    }

    private void RefuseUnsupported(LlmCompletionOptions? callOptions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (callOptions?.Tools is { Count: > 0 })
        {
            throw new NotSupportedException("The local model does not call tools (SupportsToolCalling is false); send the request without tools.");
        }

        var requested = callOptions?.Model;
        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested, Model.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requested, "local/" + Model.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"This local provider runs '{Model.Id}' and cannot switch to '{requested}' per call; create a provider for that model.");
        }
    }

    private List<ChatMessage> PrepareConversation(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? callOptions)
    {
        var conversation = new List<ChatMessage>(messages);
        var system = callOptions?.SystemPrompt;
        if (callOptions is { JsonMode: true } || !string.IsNullOrEmpty(callOptions?.JsonSchema))
        {
            var jsonRule = "Respond with valid JSON only, with no other text."
                + (string.IsNullOrEmpty(callOptions.JsonSchema) ? string.Empty : " The JSON must follow this JSON schema: " + callOptions.JsonSchema);
            system = string.IsNullOrEmpty(system) ? jsonRule : system + "\n" + jsonRule;
        }

        if (!string.IsNullOrEmpty(system))
        {
            var existing = conversation.FindIndex(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                conversation[existing] = ChatMessage.System(system + "\n\n" + conversation[existing].Content);
            }
            else
            {
                conversation.Insert(0, ChatMessage.System(system));
            }
        }

        return conversation;
    }

    private LocalGenerationSettings BuildSettings(LlmCompletionOptions? callOptions, int room) => new()
    {
        MaxTokens = Math.Max(1, Math.Min(callOptions?.MaxTokens ?? options.MaxTokens, room)),
        Temperature = callOptions?.Temperature ?? options.Temperature,
        TopP = callOptions?.TopP ?? options.TopP,
        Seed = callOptions?.Seed,
        FrequencyPenalty = callOptions?.FrequencyPenalty,
        PresencePenalty = callOptions?.PresencePenalty,
        StopSequences = ChatTemplateFormatter.EndOfTurn(Model.ChatTemplate) is { } endOfTurn
            ? [.. callOptions?.StopSequences ?? [], endOfTurn]
            : [.. callOptions?.StopSequences ?? []],
        JsonMode = callOptions?.JsonMode ?? false,
        JsonSchema = callOptions?.JsonSchema
    };

    private void RaiseCompleted(TokenUsage usage, TimeSpan duration, bool isStreaming) =>
        OnCompletionCompleted?.Invoke(this, new LlmCompletionEventArgs
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            Duration = duration,
            ModelName = Model.Id,
            ProviderName = Name,
            IsStreaming = isStreaming,
            InvolvedToolCalls = false
        });

    private (ILocalLlmRuntime Runtime, LocalModelVariant? Variant, string Directory) Resolve()
    {
        var resolved = runtime ?? throw new PlatformNotSupportedException(LocalRuntimeSelector.NoRuntimeMessage);
        if (Model.Folder is not null)
        {
            return (resolved, null, Model.Folder);
        }

        var variant = Model.FindVariant(resolved.Format)
            ?? throw new NotSupportedException($"The local model '{Model.Id}' is not published in a format this platform's runtime loads.");
        return (resolved, variant, Model.GetDirectory(variant));
    }

    private ILocalTokenizer? TryLoadTokenizer()
    {
        if (tokenizer is not null || runtime is null || disposed)
        {
            return tokenizer;
        }

        if (Model.Folder is null && Model.FindVariant(runtime.Format) is null)
        {
            return null;
        }

        var (resolved, variant, directory) = Resolve();
        var present = variant is null
            ? Directory.Exists(directory)
            : variant.Files.All(f => File.Exists(Path.Combine(directory, f.FileName)));
        if (present)
        {
            tokenizer = resolved.LoadTokenizer(directory);
        }

        return tokenizer;
    }

    private static long FolderBytes(string directory) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        trimmed = firstNewline > 0 ? trimmed[(firstNewline + 1)..] : trimmed[3..];
        return (trimmed.EndsWith("```", StringComparison.Ordinal) ? trimmed[..^3] : trimmed).Trim();
    }
}
