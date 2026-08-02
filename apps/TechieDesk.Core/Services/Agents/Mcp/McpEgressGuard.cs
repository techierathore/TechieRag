using TechieRag.Abstractions;
using TechieRag.Mcp;
using TechieRag.Models;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// Makes the tools of an HTTP MCP server inherit <see cref="EgressGate"/>, so an administrator's
/// registered server cannot route around the "ask before any skill that leaves this machine" switch
/// (REQ-NFR-013 applied to REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>The defect this closes before it happens.</b> REQ-NFR-013 exists because
/// <c>AgentDefinition.ConfirmEgress</c> was a promise nothing kept. An MCP server registered over
/// <c>http(s)</c> is a third party the model can send arbitrary text to — the same shape as web
/// search, and a wider one, because the administrator chose the endpoint. Landing MCP without this
/// would have re-created the identical defect one requirement later: a switch labelled "ask before
/// anything leaves this machine" that a registered tool server silently ignores.</para>
/// <para><b>Keyed off transport, not off a name list.</b> A tool is gated because the server that
/// hosts it has <see cref="McpTransportKind.Http"/>. That is the exposure fact, it is stored on the
/// registration, and a server registered tomorrow inherits the gate the moment it is saved. A
/// hard-coded list of server or tool names is how this class of defect comes back — which is the
/// same reasoning <see cref="EgressGate"/> gives for reading <c>SkillCatalog.Exposure</c>.</para>
/// <para><b>Stdio servers are NOT gated, deliberately.</b> A stdio server is a child process on this
/// machine; no request leaves it, and the prompt's own wording — "sends a request off this machine"
/// — would be false. What a stdio server is instead is <i>local code execution</i>, and its consent
/// is the registration itself: an administrator nominating a fully-qualified executable in this
/// workspace, under a trust policy that refuses <c>PATH</c> lookup and refuses a shell. Gating it
/// here would ask the user the wrong question at the wrong time and would train them to approve a
/// prompt whose text does not match what is happening. If a stdio server itself makes network calls,
/// that is that program's behaviour and TechieDesk cannot observe it — claiming otherwise with a
/// dialog would be the very thing REQ-NFR-013 was raised about.</para>
/// <para><b>Prefix matching, and why over-matching is the safe direction.</b>
/// <c>McpToolHandler.QualifyToolName</c> produces <c>{server}-{tool}</c>, truncating with a hash
/// suffix past 64 characters — and since a server name is capped at 48 characters the
/// <c>{server}-</c> prefix always survives that truncation, so it is a reliable key. Two servers
/// named <c>acme</c> and <c>acme-eu</c> would make one prefix ambiguous; the ambiguity resolves
/// towards GATING, so the worst case is an extra confirmation, never a silent egress.</para>
/// <para><b>Decorator, not a filter.</b> Exactly as <see cref="EgressGate"/> does for skills, a
/// gated tool stays registered and stays visible to the model; it is the outbound call that stops. A
/// declined call comes back as an unsuccessful <see cref="ToolResult"/> the model can read and work
/// around, never as an exception that ends the turn.</para>
/// </remarks>
public sealed class McpEgressGuard : IToolHandler
{
    private readonly IToolHandler inner;
    private readonly IReadOnlyList<string> gatedServerPrefixes;
    private readonly EgressGate gate;

    /// <inheritdoc />
    /// <remarks>Unchanged from the wrapped handler: gating happens at execution, not at exposure.</remarks>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => inner.ToolDefinitions;

    /// <summary>
    /// Wraps a handler of MCP tools so the ones hosted off this machine ask first.
    /// </summary>
    /// <param name="inner">The MCP tool handler to wrap.</param>
    /// <param name="gatedServerNames">
    /// The configured names of the registered servers that leave the machine — the
    /// <see cref="McpTransportKind.Http"/> ones.
    /// </param>
    /// <param name="gate">This turn's gate, sharing its once-per-turn decision with the skills.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public McpEgressGuard(IToolHandler inner, IEnumerable<string> gatedServerNames, EgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(gatedServerNames);
        ArgumentNullException.ThrowIfNull(gate);

        this.inner = inner;
        this.gate = gate;
        gatedServerPrefixes = gatedServerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name + "-")
            .ToList();
    }

    /// <summary>
    /// Gets whether a qualified tool name belongs to one of the servers that leaves the machine.
    /// </summary>
    /// <param name="qualifiedToolName">The tool name as the model sees it.</param>
    /// <returns>True when the call must be confirmed before it is made.</returns>
    public bool LeavesMachine(string? qualifiedToolName) =>
        !string.IsNullOrEmpty(qualifiedToolName)
        && gatedServerPrefixes.Any(prefix =>
            qualifiedToolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!LeavesMachine(toolCall.Name))
        {
            return await inner.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
        }

        var definition = inner.ToolDefinitions
            .FirstOrDefault(tool => string.Equals(tool.Name, toolCall.Name, StringComparison.OrdinalIgnoreCase));

        var isAllowed = await gate
            .AllowExternalAsync(
                toolCall.Name, toolCall.Name, definition?.Description ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (isAllowed)
        {
            return await inner.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
        }

        // The unavailable: channel and not an exception — the loop records the block in the trace,
        // tells the model the tool could not run, and finishes the answer with what it has locally.
        //
        // REQ-UI-055: the two fields below have DIFFERENT AUDIENCES, and that is provable rather
        // than assumed. AgentLoopRunner does `messages.Add(ChatMessage.Tool(result.ToolCallId,
        // result.Content))` — Content, and only Content, enters the conversation the model reasons
        // over. ErrorMessage is never added to the message list; it reaches AgentStep.ErrorMessage,
        // which AgentTracePanel renders as the failed step's detail row. So Content stays English
        // for the model (and so the trace stays a faithful record of what the model was told), and
        // ErrorMessage is resolved in the READER's language, because a person is its only reader.
        var refusal = SkillUnavailable.Because(
            $"'{toolCall.Name}' is hosted by an MCP server that is not on this machine, and approval "
            + "to send the request was declined or could not be asked for, so nothing was sent. "
            + "Answer from what is available locally, or say the request was not approved.");

        return new ToolResult
        {
            ToolCallId = toolCall.Id,
            Content = refusal,
            IsSuccess = false,
            ErrorMessage = EgressWording.ForReader(EgressWording.McpCallNotApprovedKey)
        };
    }
}
