using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TechieRag.Agentic;
using TechieRag.Models;

namespace TechieRag.Agents;

/// <summary>The answer of one <see cref="ITechieRagAgent"/> turn, with its typed sources (REQ-RAG-016 / BRD-84).</summary>
public sealed class AgentRagResponse
{
    /// <summary>Gets the answer text.</summary>
    public required string Answer { get; init; }

    /// <summary>Gets the distinct passages the agent retrieved this turn, as typed results for citations.</summary>
    public required IReadOnlyList<SearchResult> Sources { get; init; }

    /// <summary>Gets a trace of every search the agent made this turn.</summary>
    public required IReadOnlyList<RetrievalTrace> Searches { get; init; }

    /// <summary>Gets tool calls waiting for the host's approval (tools marked <c>RequiresConfirmation</c>); empty when none.</summary>
    public required IReadOnlyList<ToolApprovalRequestContent> PendingApprovals { get; init; }

    /// <summary>Gets the underlying Agent Framework response.</summary>
    public required AgentResponse Raw { get; init; }
}
