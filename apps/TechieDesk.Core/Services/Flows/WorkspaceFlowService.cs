using TechieDesk.Services.Agents;
using TechieDesk.Services.Agents.Mcp;
using TechieDesk.Services.Web;
using TechieRag.Abstractions;
using TechieRag.Mcp;
using TechieRag.Models;
using TechieRag.Orchestration;
using TechieRag.Services;

namespace TechieDesk.Services.Flows;

/// <summary>
/// The application half of REQ-RAG-042: persistence, workspace scoping, the two resolvers, the host
/// guardrails, and one place that runs a flow (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>One tool-composition path, shared with chat.</b> A flow's agents are given the SAME tool
/// set a chat turn would give them: the workspace skill-catalogue intersection, wrapped by
/// <see cref="EgressGate.Guard"/>, composed with the workspace's registered MCP servers through
/// <see cref="IWorkspaceMcpService.BuildTurnToolsAsync"/>. Building a second, flow-only tool path is
/// how a flow would end up with a wider tool set than the chat surface the same user is looking at.</para>
/// <para><b>The egress gate is installed as a HOST guardrail, always.</b> See
/// <see cref="FlowHostGuardrails"/>. It is not offered in the author's guardrail palette and there is
/// no argument on this class that turns it off.</para>
/// <para><b>MCP servers are started for the run and shut down with it.</b> A stdio server is a child
/// process; it must not outlive the run it was started for. With nothing registered, nothing starts
/// and nothing is contacted (REQ-NFR-008).</para>
/// </remarks>
public sealed class WorkspaceFlowService : IWorkspaceFlowService
{
    private readonly IFlowRepository flows;
    private readonly IAgentRegistry agents;
    private readonly IWorkspaceMcpService workspaceMcp;
    private readonly TechieRagManager rag;
    private readonly IWebContentFetcherFactory webFetchers;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<WorkspaceFlowService> logger;
    private readonly FlowGuardrailCatalog guardrailCatalog = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="flows">Durable flow storage.</param>
    /// <param name="agents">The workspace agent registry the agent picker and resolver read.</param>
    /// <param name="workspaceMcp">The workspace's MCP surface, for tool names and for run-time tools.</param>
    /// <param name="rag">The library manager supplying the configured LLM provider and workspace search.</param>
    /// <param name="webFetchers">Builds the web fetcher the scrape skill uses, with the same policy chat uses.</param>
    /// <param name="timeProvider">Clock, so stored timestamps are testable.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public WorkspaceFlowService(
        IFlowRepository flows,
        IAgentRegistry agents,
        IWorkspaceMcpService workspaceMcp,
        TechieRagManager rag,
        IWebContentFetcherFactory webFetchers,
        TimeProvider timeProvider,
        ILogger<WorkspaceFlowService> logger)
    {
        this.flows = flows ?? throw new ArgumentNullException(nameof(flows));
        this.agents = agents ?? throw new ArgumentNullException(nameof(agents));
        this.workspaceMcp = workspaceMcp ?? throw new ArgumentNullException(nameof(workspaceMcp));
        this.rag = rag ?? throw new ArgumentNullException(nameof(rag));
        this.webFetchers = webFetchers ?? throw new ArgumentNullException(nameof(webFetchers));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<IFlowGuardrail> Guardrails => guardrailCatalog.Available;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlowListItem>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        var records = await flows.ListAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return records.Select(ToItem).ToList();
    }

    /// <inheritdoc />
    public async Task<FlowListItem?> FindAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default)
    {
        var record = await flows.FindAsync(workspaceId, flowId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToItem(record);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string workspaceId, FlowDefinition flow, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(flow);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await flows.FindAsync(workspaceId, flow.Id, cancellationToken).ConfigureAwait(false);

        // ToJson stamps the current schema version onto the flow as it writes, so the mirrored
        // column is read AFTER serializing rather than from whatever the object arrived carrying.
        var json = FlowSerializer.ToJson(flow);

        await flows.SaveAsync(new FlowRecord
        {
            FlowId = flow.Id,
            WorkspaceId = workspaceId,
            Name = flow.Name,
            Description = flow.Description,
            DefinitionJson = json,
            SchemaVersion = flow.SchemaVersion,
            IsEnabled = isEnabled,
            CreatedAtUtc = existing?.CreatedAtUtc is { } created && created != default ? created : now,
            UpdatedAtUtc = now
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default) =>
        flows.DeleteAsync(workspaceId, flowId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(
        string workspaceId, string flowId, bool isEnabled, CancellationToken cancellationToken = default) =>
        flows.SetEnabledAsync(workspaceId, flowId, isEnabled, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentDefinition>> ListAgentsAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        return await agents.ListAsync(workspaceId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListToolNamesAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var names = new List<string>(WorkspaceSkillTools.ImplementedSkillNames);

        var registrations = await workspaceMcp.ListAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        foreach (var record in registrations.Where(record => record.Registration.IsEnabled))
        {
            names.AddRange(record.AdvertisedTools.Select(tool =>
                McpToolHandler.QualifyToolName(record.Registration.Server.Name, tool.Name)));
        }

        return names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async Task<FlowRunResult> RunAsync(
        string workspaceId,
        FlowDefinition flow,
        string input,
        IEgressConfirmation? confirmation,
        IProgress<AgentStep>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(input);

        var provider = await rag.GetLlmProviderAsync().ConfigureAwait(false);

        // Only a flow that actually calls a model needs one. A branch-and-end flow costs no tokens
        // and makes no request, so refusing it on an unconfigured install would be a false refusal —
        // exactly what the Catalyst head showed on 2026-08-01 before this check existed.
        if (provider is null && FlowCapabilities.NeedsLlmProvider(flow))
        {
            return FlowOutcomes.Failed(
                flow.Id,
                "This flow has a step that calls a model and no LLM provider is configured, so nothing "
                + "was run. Configure a model in LLM settings first.");
        }

        // The gate is built from the agent that OWNS the flow's first agent node, falling back to the
        // built-in agent. Its ConfirmEgress is what governs the whole run, exactly as one agent's
        // setting governs one chat turn — a run is one unit of consent, not one prompt per node.
        var owner = await ResolveOwningAgentAsync(workspaceId, flow).ConfigureAwait(false);
        var gate = new EgressGate(owner, confirmation);

        var manager = await rag.GetWorkspaceManagerAsync().ConfigureAwait(false);

        await using var toolScope = new FlowToolScope();

        // With no provider, the flow provably has no model-calling step, so a resolver over an empty
        // set is the honest binding: nothing can resolve, and nothing needs to.
        IFlowAgentResolver resolver = provider is null
            ? new InMemoryFlowAgentResolver()
            : new AgentRegistryFlowAgentResolver(
                agents,
                workspaceId,
                provider,
                (definition, token) => toolScope.ToolsForAsync(
                    definition,
                    token,
                    agent => BuildToolsAsync(workspaceId, agent, manager, gate, token)));

        var runtime = new FlowRuntime(resolver)
        {
            Guardrails = guardrailCatalog
        };

        // REQ-NFR-013: the host's egress gate, installed before anything can run and reachable by no
        // property of the flow. A flow is not a route around the gate.
        FlowHostGuardrails.InstallOn(runtime, gate);

        // A deterministic Tool node uses the same composed handler the agents get, so a tool called
        // without a model in the path is gated identically to one the model asked for.
        runtime.Tools = await toolScope.ToolsForAsync(
            AgentDefinition.BuiltIn(workspaceId, timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken,
            agent => BuildToolsAsync(workspaceId, agent, manager, gate, cancellationToken)).ConfigureAwait(false);

        var runner = new FlowRunner(flow, runtime, logger: null);
        return await runner.RunAsync(input, variables: null, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one stored row back, tolerating a document that cannot be parsed.</summary>
    /// <param name="record">The stored row.</param>
    /// <returns>The list item, carrying either the flow or the reason it could not be read.</returns>
    private static FlowListItem ToItem(FlowRecord record)
    {
        FlowSerializer.TryFromJson(record.DefinitionJson, out var definition, out var error);
        return new FlowListItem(record, definition, error);
    }

    /// <summary>
    /// Decides whose egress setting governs this run.
    /// </summary>
    /// <param name="workspaceId">The workspace the flow belongs to.</param>
    /// <param name="flow">The flow about to run.</param>
    /// <returns>The agent whose <see cref="AgentDefinition.ConfirmEgress"/> applies.</returns>
    /// <remarks>
    /// The first agent node's agent, because that is the agent the user chose when they composed the
    /// flow. Falling back to the built-in agent — whose <c>ConfirmEgress</c> defaults ON — means a
    /// flow with no agent node at all is still gated rather than silently ungated.
    /// </remarks>
    private async Task<AgentDefinition> ResolveOwningAgentAsync(string workspaceId, FlowDefinition flow)
    {
        var handle = flow.Nodes
            .FirstOrDefault(node => node.Kind == FlowNodeKind.Agent && !string.IsNullOrWhiteSpace(node.AgentId))
            ?.AgentId;

        var resolved = string.IsNullOrWhiteSpace(handle)
            ? null
            : await agents.ResolveAsync(workspaceId, handle).ConfigureAwait(false);

        return resolved
            ?? await agents.ResolveAsync(workspaceId, AgentDefinition.BuiltInHandle).ConfigureAwait(false)
            ?? AgentDefinition.BuiltIn(workspaceId, timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// Composes one agent's tools exactly as a chat turn composes them.
    /// </summary>
    /// <param name="workspaceId">The workspace answering the run.</param>
    /// <param name="agent">The agent whose permitted skills are being bound.</param>
    /// <param name="manager">The workspace manager backing RAG search, or null when unavailable.</param>
    /// <param name="gate">The run's egress gate.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>The composed handler and the MCP servers it started.</returns>
    private async Task<McpTurnTools> BuildToolsAsync(
        string workspaceId,
        AgentDefinition agent,
        WorkspaceManager? manager,
        EgressGate gate,
        CancellationToken cancellationToken)
    {
        var permitted = await agents.PermittedSkillsAsync(agent).ConfigureAwait(false);

        var local = AgentToolPlanner.BuildRegistry(permitted, gate.Guard(
        [
            WorkspaceSkillTools.RagSearch(async (query, token) =>
            {
                if (manager is null) return "Workspace search is not available on this install.";

                var hits = await manager.SearchScopedAsync(workspaceId, query, overrides: null, cancellationToken: token)
                    .ConfigureAwait(false);

                return hits.Count == 0
                    ? "No matching passages in scope."
                    : string.Join("\n\n", hits.Select(hit => hit.Chunk.Text));
            }),
            .. WorkspaceSkillTools.Standard(FlowSkillOptions())

        ]));

        var tools = await workspaceMcp.BuildTurnToolsAsync(workspaceId, local, gate, cancellationToken)
            .ConfigureAwait(false);

        foreach (var failure in tools.Failures)
        {
            logger.LogWarning(
                "MCP server {ServerName} is registered for this workspace but could not be used by the flow: {Reason}",
                failure.ServerName, failure.Reason);
        }

        return tools;
    }

    /// <summary>Builds the skill dependencies this install has configured, matching the chat surface.</summary>
    /// <returns>The options; every skill still exists and reports honestly when it cannot run.</returns>
    private WorkspaceSkillOptions FlowSkillOptions() => new()
    {
        WebFetcher = webFetchers.Create(blockPrivateNetworkTargets: true),

        // No web-search provider, no nominated reporting database and no per-workspace file area
        // ship — identical to the chat surface, and for the same reasons. A flow must not have
        // capabilities the chat turn beside it does not.
        WebSearch = null,
        SqlTarget = null,
        Files = null
    };

    /// <summary>
    /// Owns the MCP servers one run started, so they are shut down exactly once when it ends.
    /// </summary>
    /// <remarks>
    /// Tools are built per AGENT, and a flow can name the same agent in several nodes, so this caches
    /// by handle. Without it a five-node flow would start the workspace's stdio servers five times.
    /// </remarks>
    private sealed class FlowToolScope : IAsyncDisposable
    {
        private readonly Dictionary<string, McpTurnTools> byHandle = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets, or builds and caches, the tools for one agent.</summary>
        /// <param name="agent">The agent whose tools are wanted.</param>
        /// <param name="cancellationToken">Token to cancel the run.</param>
        /// <param name="build">Builds the tools when this agent has not been seen yet.</param>
        /// <returns>The composed tool handler.</returns>
        public async Task<IToolHandler> ToolsForAsync(
            AgentDefinition agent,
            CancellationToken cancellationToken,
            Func<AgentDefinition, Task<McpTurnTools>> build)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (byHandle.TryGetValue(agent.Handle, out var cached)) return cached.ToolHandler;

            var built = await build(agent).ConfigureAwait(false);
            byHandle[agent.Handle] = built;
            return built.ToolHandler;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (var tools in byHandle.Values)
            {
                await tools.DisposeAsync().ConfigureAwait(false);
            }

            byHandle.Clear();
        }
    }
}

/// <summary>
/// Builds the <see cref="FlowRunResult"/> values the app produces before a runner is ever created
/// (REQ-UI-040).
/// </summary>
/// <remarks>
/// The library's rule is that a flow-level problem is an OUTCOME, never an exception, precisely so a
/// screen can render what happened. "This install has no model configured" is a flow-level problem
/// that the app discovers first, so it is reported in the same shape rather than thrown.
/// </remarks>
internal static class FlowOutcomes
{
    /// <summary>Builds a failed result carrying a reason and an empty trace.</summary>
    /// <param name="flowId">The flow that could not be run.</param>
    /// <param name="reason">Why it could not be run.</param>
    /// <returns>The result.</returns>
    public static FlowRunResult Failed(string flowId, string reason) => new()
    {
        RunId = Guid.NewGuid().ToString("N"),
        FlowId = flowId,
        Outcome = FlowRunOutcome.Failed,
        FailureReason = reason,
        Output = null,
        Steps = [],
        VisitedNodeIds = [],
        Variables = new Dictionary<string, string>(StringComparer.Ordinal),
        StepsExecuted = 0,
        Usage = new TokenUsage()
    };
}
