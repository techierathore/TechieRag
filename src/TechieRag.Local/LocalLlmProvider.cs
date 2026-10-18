using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// <para><b>Dispose while calls are inside</b> (REQ-RAG-129, Sevak TR-RAG-053). <see cref="Dispose"/>
/// refuses new calls, cancels the load, the queue and the generation of every call already inside, and
/// returns without blocking. Each of those calls ends with <see cref="ObjectDisposedException"/> at its
/// next step, before it touches the model again. The model's native memory is freed when the last call
/// has left, never under a running token. <see cref="DisposeAsync"/> does the same and completes once the
/// memory is freed, so a host can load the next model after it.</para>
/// <para><b>Not supported:</b> tool calling (<see cref="SupportsToolCalling"/> is false until a test
/// proves otherwise, REQ-RAG-063), images, and a per-call model other than the loaded one.</para>
/// </remarks>
public sealed class LocalLlmProvider : ILlmProvider, IDisposable, IAsyncDisposable
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
    private readonly object lifetimeLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ILocalLlmModel? loaded;
    private ILocalTokenizer? tokenizer;
    private int activeCalls;
    private volatile bool disposed;

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

    /// <summary>
    /// Gets whether this platform has a local inference runtime, without loading or downloading anything:
    /// false on an Intel Mac or in a browser, where <see cref="LoadAsync"/> throws
    /// <see cref="PlatformNotSupportedException"/> (REQ-RAG-125, Sevak TR-RAG-047).
    /// </summary>
    public static bool IsRuntimeAvailable => LocalRuntimeSelector.IsSupported(RuntimeInformation.ProcessArchitecture);

    /// <summary>Gets whether the model is loaded and ready to answer.</summary>
    public bool IsLoaded => loaded is not null;

    /// <summary>Gets how many calls are inside the provider now (loading, queued or generating).</summary>
    internal int ActiveCalls
    {
        get
        {
            lock (lifetimeLock)
            {
                return activeCalls;
            }
        }
    }

    /// <inheritdoc/>
    public string Name => "Local";

    /// <inheritdoc/>
    public string ModelName => Model.Id;

    /// <inheritdoc/>
    /// <remarks>True for the shipped catalogue models that are trained for tool calling (TR-RAG-052).</remarks>
    public bool SupportsToolCalling => Model.SupportsToolCalling;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <summary>
    /// Gets the terms the user accepts before the model downloads, with the bytes the download would
    /// transfer when this platform's file set is known (REQ-RAG-062): a file already in the model folder
    /// costs nothing and an interrupted download's <c>.part</c> file only its remainder, so the figure
    /// matches what <see cref="LoadAsync"/> then reports before its first byte (REQ-RAG-061).
    /// </summary>
    /// <returns>The terms.</returns>
    public LocalModelTerms GetTerms()
    {
        var variant = runtime is null ? null : Model.FindVariant(runtime.Format);
        return Model.GetTerms() with { DownloadBytes = variant?.GetPendingBytes(Model.GetDirectory(variant)) ?? 0 };
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
    /// <exception cref="ObjectDisposedException">The provider was disposed before or during the load.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            await WhileAliveAsync(LoadCoreAsync(linked.Token), cancellationToken).ConfigureAwait(false);
            ThrowIfDisposed();
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Loads the model; the caller has entered (<see cref="Enter"/>) and passes a token the dispose cancels.</summary>
    /// <param name="cancellationToken">The caller's token linked to this provider's lifetime.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
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
                // REQ-RAG-125 (Sevak TR-RAG-047): a model that cannot fit is refused before its first byte is
                // requested. The check after the download stays: a Hugging Face model's context cache size is
                // known only once its genai_config.json is on disk.
                if (!variant.IsOnDisk(directory))
                {
                    memoryGate.Ensure(Model.Id, Model.EstimateMemoryBytes(variant.DownloadBytes, ContextSize));
                }

                // TECHIERAG_MODEL_BASE_URL is for the built-in catalogue; a Hugging Face name is its own source.
                var mirror = Model.HuggingFace is null ? LocalModelStore.Mirror : null;
                await store.EnsureAsync(Model, variant, directory, options, mirror, cancellationToken).ConfigureAwait(false);
            }

            var weightBytes = variant?.DownloadBytes ?? LocalModel.FolderBytes(directory);
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
        List<ToolCall>? toolCalls = null;
        LlmStreamEvent? completed = null;
        await foreach (var streamEvent in StreamCoreAsync(messages, options, isStreaming: false, cancellationToken).ConfigureAwait(false))
        {
            switch (streamEvent.Kind)
            {
                case LlmStreamEventKind.TextDelta:
                    text.Append(streamEvent.Text);
                    break;
                case LlmStreamEventKind.ToolCall when streamEvent.ToolCall is not null:
                    (toolCalls ??= new List<ToolCall>()).Add(streamEvent.ToolCall);
                    break;
                case LlmStreamEventKind.Completed:
                    completed = streamEvent;
                    break;
            }
        }

        var hasToolCalls = toolCalls is { Count: > 0 };
        return new LlmResponse
        {
            Content = hasToolCalls ? null : text.ToString(),
            ToolCalls = hasToolCalls ? toolCalls : null,
            Usage = completed!.Usage!,
            FinishReason = hasToolCalls ? "tool_calls" : (completed.FinishReason ?? "stop"),
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

        Enter();
        try
        {
            var counter = (ILocalTokenizer?)loaded ?? TryLoadTokenizer();
            return counter?.CountTokens(text) ?? (int)Math.Ceiling(text.Length / 4.0);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Unloads the model: refuses new calls, cancels every call inside (each ends with
    /// <see cref="ObjectDisposedException"/>), and frees the native memory once the last has left.
    /// Returns without waiting for them.
    /// </summary>
    public void Dispose()
    {
        bool releaseNow;
        lock (lifetimeLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            releaseNow = activeCalls == 0;
        }

        // Cancel after the flag is set, so a call woken by the cancellation sees it.
        lifetime.Cancel();
        if (releaseNow)
        {
            ReleaseNative();
        }
    }

    /// <summary>
    /// Unloads the model as <see cref="Dispose"/> does and completes once every call has left and the
    /// native memory is freed.
    /// </summary>
    /// <returns>A task that completes when the model's memory is freed.</returns>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await released.Task.ConfigureAwait(false);
    }

    /// <summary>Registers a call inside the provider, refusing it once disposed.</summary>
    /// <exception cref="ObjectDisposedException">The provider was disposed.</exception>
    private void Enter()
    {
        lock (lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeCalls++;
        }
    }

    /// <summary>Ends a call; the last call to leave a disposed provider frees the native model.</summary>
    private void Exit()
    {
        bool releaseNow;
        lock (lifetimeLock)
        {
            activeCalls--;
            releaseNow = disposed && activeCalls == 0;
        }

        if (releaseNow)
        {
            ReleaseNative();
        }
    }

    /// <summary>Frees the model, the tokenizer and the gates; runs once, when no call is inside.</summary>
    private void ReleaseNative()
    {
        loaded?.Dispose();
        loaded = null;
        tokenizer?.Dispose();
        tokenizer = null;
        loadGate.Dispose();
        generateGate.Dispose();
        released.TrySetResult();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <summary>Awaits a step, turning a cancellation caused by <see cref="Dispose"/> into <see cref="ObjectDisposedException"/>.</summary>
    /// <param name="step">The step.</param>
    /// <param name="callerToken">The caller's own token; its cancellation stays an <see cref="OperationCanceledException"/>.</param>
    /// <returns>A task that completes with the step.</returns>
    private async Task WhileAliveAsync(Task step, CancellationToken callerToken)
    {
        try
        {
            await step.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (disposed && !callerToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(LocalLlmProvider), exception);
        }
    }

    /// <inheritdoc cref="WhileAliveAsync(Task, CancellationToken)"/>
    private async Task<bool> WhileAliveAsync(ValueTask<bool> step, CancellationToken callerToken)
    {
        try
        {
            return await step.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (disposed && !callerToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(LocalLlmProvider), exception);
        }
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamCoreAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? callOptions,
        bool isStreaming,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        RefuseUnsupported(callOptions);

        // REQ-RAG-129 (Sevak TR-RAG-053): the call is counted from here to its last step, so the native
        // model is never freed under it, and the flag is checked after every await before the model is used.
        Enter();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            var token = linked.Token;
            await WhileAliveAsync(LoadCoreAsync(token), cancellationToken).ConfigureAwait(false);
            ThrowIfDisposed();
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
            await WhileAliveAsync(generateGate.WaitAsync(token), cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var watch = Stopwatch.StartNew();
                var produced = 0;
                var hasTools = callOptions?.Tools is { Count: > 0 } && SupportsToolCalling;
                var toolBuffer = hasTools ? new StringBuilder() : null;
                var pieces = model.GenerateAsync(prompt, settings, token).GetAsyncEnumerator(token);
                try
                {
                    while (await WhileAliveAsync(pieces.MoveNextAsync(), cancellationToken).ConfigureAwait(false))
                    {
                        ThrowIfDisposed();
                        produced++;
                        var text = filter.Push(pieces.Current);
                        if (text.Length > 0)
                        {
                            if (toolBuffer is null)
                            {
                                yield return LlmStreamEvent.FromText(text);
                                ThrowIfDisposed();
                            }
                            else
                            {
                                toolBuffer.Append(text);
                            }
                        }

                        if (filter.Stopped || produced >= settings.MaxTokens)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    await pieces.DisposeAsync().ConfigureAwait(false);
                }

                var rest = filter.Flush();
                if (rest.Length > 0)
                {
                    if (toolBuffer is null)
                    {
                        yield return LlmStreamEvent.FromText(rest);
                        ThrowIfDisposed();
                    }
                    else
                    {
                        toolBuffer.Append(rest);
                    }
                }

                var rawOutput = toolBuffer?.ToString() ?? string.Empty;
                var parsedToolCalls = hasTools ? ToolPromptFormatter.ParseToolCalls(Model, rawOutput) : null;
                var finishReason = parsedToolCalls is { Count: > 0 }
                    ? "tool_calls"
                    : (!filter.Stopped && produced >= settings.MaxTokens ? "length" : "stop");
                var usage = new TokenUsage { InputTokens = promptTokens, OutputTokens = produced, ModelName = Model.Id, ProviderName = Name };

                if (parsedToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in parsedToolCalls)
                    {
                        yield return LlmStreamEvent.FromToolCall(toolCall);
                        ThrowIfDisposed();
                    }
                }
                else if (toolBuffer is not null && toolBuffer.Length > 0)
                {
                    yield return LlmStreamEvent.FromText(toolBuffer.ToString());
                    ThrowIfDisposed();
                }

                RaiseCompleted(usage, watch.Elapsed, isStreaming);
                yield return LlmStreamEvent.FromCompleted(usage, finishReason, Model.Id);
            }
            finally
            {
                generateGate.Release();
            }
        }
        finally
        {
            Exit();
        }
    }

    private void RefuseUnsupported(LlmCompletionOptions? callOptions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (callOptions?.Tools is { Count: > 0 } && !SupportsToolCalling)
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

        if (callOptions?.Tools is { Count: > 0 } && SupportsToolCalling)
        {
            var toolInstruction = ToolPromptFormatter.BuildToolInstruction(Model, callOptions.Tools);
            system = string.IsNullOrEmpty(system) ? toolInstruction : system + "\n\n" + toolInstruction;
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

        for (var i = 0; i < conversation.Count; i++)
        {
            var message = conversation[i];
            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                && SupportsToolCalling
                && !string.IsNullOrEmpty(message.Content))
            {
                conversation[i] = new ChatMessage
                {
                    Role = message.Role,
                    Content = ToolPromptFormatter.FormatToolResult(Model, message.ToolCallId ?? string.Empty, message.Name ?? string.Empty, message.Content),
                    ToolCallId = message.ToolCallId,
                    Name = message.Name,
                    Parts = message.Parts,
                    CacheBoundary = message.CacheBoundary
                };
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
