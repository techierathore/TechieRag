using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Agentic;
using TechieRag.Agents.ChatClients;
using TechieRag.Agents.Interop;
using TechieRag.Agents.Retrieval;
using TechieRag.Models;

namespace TechieRag.Agents;

/// <summary>
/// Fluent builder for a Microsoft Agent Framework agent over a TechieRag instance, in the same style as
/// <c>TechieRagBuilder</c> (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>
/// <para>Pick one model: <see cref="UseLmStudio"/> (the primary local target), <see cref="UseOllama"/>,
/// <see cref="UseOpenAI"/>, <see cref="UseOpenAICompatible"/>, <see cref="UseConfiguredLlm"/> (the LLM
/// already configured on TechieRag, through <see cref="LlmProviderChatClient"/>) or
/// <see cref="UseCustomChatClient"/>. The agent always gets the knowledge-base tools of
/// <see cref="KnowledgeBaseTools"/> and the instructions of <see cref="AgenticInstructions"/>.</para>
/// <para>Zero egress beyond the model: no Harness agent, no hosted tools, no telemetry exporter.</para>
/// </remarks>
public sealed class TechieRagAgentBuilder
{
    private readonly ITechieRag rag;
    private readonly RetrievalToolOptions retrievalOptions = new();
    private readonly List<IToolHandler> toolHandlers = new();
    private readonly List<AITool> extraTools = new();
    private readonly List<string> additionalInstructions = new();
    private Func<IChatClient>? chatClientFactory;
    private IRetrievalSource? retrievalSource;
    private string? instructions;
    private int maxToolIterations = DefaultMaxToolIterations;
    private IProgress<AgentStep>? trace;
    private Action<ChatOptions>? configureChatOptions;
    private ChatHistoryProvider? chatHistoryProvider;
    private bool prefetch;
    private ILoggerFactory? loggerFactory;
    private string name = "techierag";

    /// <summary>The default cap on model round-trips that request tools in one run.</summary>
    public const int DefaultMaxToolIterations = 8;

    /// <summary>Starts a builder over a TechieRag instance.</summary>
    /// <param name="rag">The instance the agent answers from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rag"/> is null.</exception>
    public TechieRagAgentBuilder(ITechieRag rag)
    {
        ArgumentNullException.ThrowIfNull(rag);
        this.rag = rag;
    }

    /// <summary>Uses a model served by LM Studio (Chat Completions at <c>{endpoint}/v1</c>).</summary>
    /// <param name="endpoint">The server root, for example <c>http://localhost:1234</c>.</param>
    /// <param name="model">The model id as LM Studio lists it. Required: a wrong id is the first thing a new user hits.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseLmStudio(string endpoint, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        chatClientFactory = () => OpenAICompatibleChatClientFactory.Create(OpenAICompatibleChatClientFactory.WithV1(endpoint), null, model);
        return this;
    }

    /// <summary>Uses a model served by Ollama through its OpenAI-compatible <c>/v1</c> API.</summary>
    /// <param name="endpoint">The server root.</param>
    /// <param name="model">The model name.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseOllama(string endpoint = "http://localhost:11434", string model = "llama3.2")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        chatClientFactory = () => OpenAICompatibleChatClientFactory.Create(OpenAICompatibleChatClientFactory.WithV1(endpoint), null, model);
        return this;
    }

    /// <summary>Uses OpenAI. Same parameter order as <c>TechieRagBuilder.UseOpenAI</c>.</summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="model">The model.</param>
    /// <param name="endpoint">The service root; <c>/v1</c> is appended.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseOpenAI(string apiKey, string model = "gpt-4o-mini", string endpoint = "https://api.openai.com")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        chatClientFactory = () => OpenAICompatibleChatClientFactory.Create(OpenAICompatibleChatClientFactory.WithV1(endpoint), apiKey, model);
        return this;
    }

    /// <summary>Uses any OpenAI-compatible Chat Completions server.</summary>
    /// <param name="endpoint">The API root, given with its <c>/v1</c> (or other base path).</param>
    /// <param name="model">The model id.</param>
    /// <param name="apiKey">The API key, or null for a server that needs none.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseOpenAICompatible(string endpoint, string model, string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        chatClientFactory = () => OpenAICompatibleChatClientFactory.Create(endpoint, apiKey, model);
        return this;
    }

    /// <summary>Uses the LLM already configured on the TechieRag instance, through <see cref="LlmProviderChatClient"/>.</summary>
    /// <returns>This builder.</returns>
    /// <remarks>Keeps one provider configuration: routing, retry, fallback, token events and credential-store
    /// keys all stay as configured on TechieRag. Checked at <see cref="Build"/>.</remarks>
    public TechieRagAgentBuilder UseConfiguredLlm()
    {
        chatClientFactory = () =>
        {
            var provider = rag.GetLlmProvider()
                ?? throw new InvalidOperationException(
                    "UseConfiguredLlm needs an LLM configured on the TechieRag instance (for example TechieRagBuilder.UseLmStudioLlm). None is configured.");
            if (!provider.SupportsToolCalling)
            {
                throw new InvalidOperationException($"The configured LLM provider '{provider.Name}' does not support tool calling, which the agent needs.");
            }

            return new LlmProviderChatClient(provider);
        };
        return this;
    }

    /// <summary>Uses a chat client the caller builds (OllamaSharp, Azure OpenAI, Foundry, ...).</summary>
    /// <param name="factory">Creates the chat client once, at <see cref="Build"/>.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseCustomChatClient(Func<IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        chatClientFactory = factory;
        return this;
    }

    /// <summary>Searches through a custom source instead of the TechieRag instance (for example a workspace scope).</summary>
    /// <param name="source">The retrieval source.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder UseRetrievalSource(IRetrievalSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        retrievalSource = source;
        return this;
    }

    /// <summary>Configures the knowledge-base tools.</summary>
    /// <param name="configure">Edits the options.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithRetrieval(Action<RetrievalToolOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(retrievalOptions);
        return this;
    }

    /// <summary>Adds a TechieRag tool handler's tools, through <see cref="ToolHandlerFunctions"/>.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithToolHandler(IToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        toolHandlers.Add(handler);
        return this;
    }

    /// <summary>Adds native Microsoft.Extensions.AI tools.</summary>
    /// <param name="tools">The tools.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithTools(params AITool[] tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        extraTools.AddRange(tools);
        return this;
    }

    /// <summary>Replaces <see cref="AgenticInstructions.Default"/> entirely.</summary>
    /// <param name="value">The instructions.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithInstructions(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        instructions = value;
        return this;
    }

    /// <summary>Appends domain guidance after the instructions; the numbered default rules are never edited.</summary>
    /// <param name="value">The guidance; null or blank is ignored.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithAdditionalInstructions(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) additionalInstructions.Add(value.Trim());
        return this;
    }

    /// <summary>Caps the model round-trips that request tools in one run.</summary>
    /// <param name="max">The cap; at least 1.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithMaxToolIterations(int max = DefaultMaxToolIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        maxToolIterations = max;
        return this;
    }

    /// <summary>Reports every run as <see cref="AgentStep"/>s, through <see cref="AgentStepReporter"/>.</summary>
    /// <param name="progress">The trace sink.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithTrace(IProgress<AgentStep> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        trace = progress;
        return this;
    }

    /// <summary>Sets chat options (temperature, max output tokens, ...). Instructions and tools are applied
    /// after this, so they cannot be clobbered.</summary>
    /// <param name="configure">Edits the options.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithChatOptions(Action<ChatOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configureChatOptions = configure;
        return this;
    }

    /// <summary>Sets the chat history provider (default: Agent Framework's in-memory provider), for
    /// example a <see cref="ConversationMemoryChatHistoryProvider"/>.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithChatHistoryProvider(ChatHistoryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        chatHistoryProvider = provider;
        return this;
    }

    /// <summary>Also searches before the first model call, for models reluctant to call tools on turn one.</summary>
    /// <param name="enabled">Whether to prefetch.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithPrefetch(bool enabled = true)
    {
        prefetch = enabled;
        return this;
    }

    /// <summary>Sets the logger factory handed to Agent Framework.</summary>
    /// <param name="factory">The logger factory.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithLogging(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        loggerFactory = factory;
        return this;
    }

    /// <summary>Sets the agent's name. Default <c>techierag</c>.</summary>
    /// <param name="value">The name.</param>
    /// <returns>This builder.</returns>
    public TechieRagAgentBuilder WithName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        name = value;
        return this;
    }

    /// <summary>Builds the agent.</summary>
    /// <returns>The agent.</returns>
    /// <exception cref="InvalidOperationException">No model was chosen, or <see cref="UseConfiguredLlm"/> found no usable LLM.</exception>
    /// <exception cref="ArgumentException">A tool handler's schema is not a JSON object.</exception>
    public ITechieRagAgent Build()
    {
        if (chatClientFactory is null)
        {
            throw new InvalidOperationException(
                "Choose a model first: UseLmStudio, UseOllama, UseOpenAI, UseOpenAICompatible, UseConfiguredLlm or UseCustomChatClient.");
        }

        var source = retrievalSource ?? new TechieRagRetrievalSource(rag, retrievalOptions.Rerank);
        var retrieval = new RetrievalContextProvider(source, retrievalOptions);
        var pipeline = chatClientFactory()
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, client => client.MaximumIterationsPerRequest = maxToolIterations)
            .Build();

        var options = new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = BuildChatOptions(),
            ChatHistoryProvider = chatHistoryProvider,
            AIContextProviders = BuildContextProviders(retrieval, source)
        };

        AIAgent agent = new ChatClientAgent(pipeline, options, loggerFactory, null);
        if (trace is not null)
        {
            agent = agent.WithAgentSteps(trace, maxToolIterations);
        }

        return new TechieRagAgent(agent, rag, retrieval);
    }

    private ChatOptions BuildChatOptions()
    {
        var chatOptions = new ChatOptions();
        configureChatOptions?.Invoke(chatOptions);

        chatOptions.Instructions = ComposeInstructions();
        var tools = toolHandlers.SelectMany(ToolHandlerFunctions.From).Concat(extraTools).ToList();
        chatOptions.Tools = tools.Count > 0 ? tools : null;
        return chatOptions;
    }

    private string ComposeInstructions()
    {
        var guidance = string.Join("\n\n", additionalInstructions);
        if (instructions is null) return AgenticInstructions.WithDomainGuidance(guidance);
        return guidance.Length == 0 ? instructions : instructions + "\n\n" + guidance;
    }

    private List<AIContextProvider> BuildContextProviders(RetrievalContextProvider retrieval, IRetrievalSource source)
    {
        var providers = new List<AIContextProvider> { retrieval };
        if (prefetch)
        {
            providers.Add(new TextSearchProvider(
                async (query, cancellationToken) =>
                {
                    var results = await source.SearchAsync(query, retrievalOptions.TopK, retrievalOptions.DocumentFilter, cancellationToken).ConfigureAwait(false);
                    return results.Select(result => new TextSearchProvider.TextSearchResult
                    {
                        SourceName = result.Chunk.DocumentId,
                        Text = result.Chunk.Text,
                        RawRepresentation = result
                    });
                },
                new TextSearchProviderOptions { SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke },
                loggerFactory));
        }

        return providers;
    }
}
