using TechieRag.Abstractions;
using TechieRag.Services;

namespace TechieRag.Orchestration;

/// <summary>
/// One agent a flow can run: an LLM, its tools, and how it is allowed to behave (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>A binding, not stored data.</b> A <see cref="FlowNode"/> names an agent by id; this is
/// what that id resolves to at run time. It is deliberately NOT part of
/// <see cref="FlowDefinition"/>: a persisted flow must not contain an API key, a live provider, or a
/// tool handler, and a flow exported from one machine must not carry another machine's tools with it.
/// The flow references; the host binds.</para>
/// <para><b>The tools are the host's, gated as the host gated them.</b> Whatever
/// <see cref="Tools"/> the host supplies is what runs — including any wrapping the host has already
/// applied, such as TechieDesk's per-skill egress wrapping. The flow adds guardrail stages on top;
/// it never unwraps or re-creates the handler.</para>
/// </remarks>
public sealed class FlowAgent
{
    /// <summary>The tool-call ceiling used when neither the agent nor the node names one.</summary>
    public const int DefaultMaxToolCalls = 8;

    /// <summary>
    /// Creates an agent binding.
    /// </summary>
    /// <param name="id">The identifier flow nodes use to reference this agent.</param>
    /// <param name="llmProvider">The provider that answers this agent's turns.</param>
    /// <param name="tools">The tools it may call; null gives it none.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="llmProvider"/> is null.</exception>
    public FlowAgent(string id, ILlmProvider llmProvider, IToolHandler? tools = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(llmProvider);

        Id = id;
        LlmProvider = llmProvider;
        Tools = tools ?? new ToolRegistry();
        DisplayName = id;
    }

    /// <summary>Gets the identifier flow nodes reference.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name shown in traces and handoff notes. Defaults to the id.</summary>
    public string DisplayName { get; set; }

    /// <summary>Gets or sets a one-line description of what this agent is for.</summary>
    public string? Description { get; set; }

    /// <summary>Gets the provider that answers this agent's turns.</summary>
    public ILlmProvider LlmProvider { get; }

    /// <summary>Gets the tools this agent may call.</summary>
    public IToolHandler Tools { get; }

    /// <summary>
    /// Gets or sets the agent's standing system prompt. It is the agent's, not the node's: a node
    /// contributes its task through <see cref="FlowNode.Instruction"/>, so the same agent behaves
    /// consistently wherever a flow places it.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Gets or sets the ceiling on tool-calling iterations for one turn of this agent.</summary>
    public int MaxToolCalls { get; set; } = DefaultMaxToolCalls;

    /// <summary>Gets or sets the sampling temperature for this agent's turns; null uses the provider default.</summary>
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the maximum tokens for this agent's turns; null uses the provider default.</summary>
    public int? MaxTokens { get; set; }
}
