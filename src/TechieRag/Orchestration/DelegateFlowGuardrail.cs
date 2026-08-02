namespace TechieRag.Orchestration;

/// <summary>
/// An <see cref="IFlowGuardrail"/> built from a delegate, for hosts that do not want a class per
/// check (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A host's egress gate, a profanity filter and a PII check are each one
/// expression over the payload. Requiring a named type for every one of them is how a host ends up
/// with two of the three and a comment about the third.</para>
/// <para><b>It gets no exemption.</b> A delegate guardrail that throws blocks exactly as a
/// class-based one does — the deny-by-default handling lives in <see cref="FlowRunner"/>, not in the
/// implementations, so it cannot be opted out of by choosing a different shape.</para>
/// </remarks>
public sealed class DelegateFlowGuardrail : IFlowGuardrail
{
    private readonly Func<GuardrailContext, CancellationToken, Task<GuardrailDecision>> inspect;

    /// <summary>
    /// Creates a guardrail from an asynchronous delegate.
    /// </summary>
    /// <param name="id">The stable identifier a node uses to name this guardrail.</param>
    /// <param name="description">A one-line description of what it refuses.</param>
    /// <param name="stages">The stages it wants to see; empty means every stage.</param>
    /// <param name="inspect">The judgement.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> or <paramref name="description"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inspect"/> is null.</exception>
    public DelegateFlowGuardrail(
        string id,
        string description,
        IReadOnlyList<GuardrailStage>? stages,
        Func<GuardrailContext, CancellationToken, Task<GuardrailDecision>> inspect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(inspect);

        Id = id;
        Description = description;
        Stages = stages is null || stages.Count == 0
            ? [GuardrailStage.Input, GuardrailStage.Output, GuardrailStage.ToolCall]
            : stages;
        this.inspect = inspect;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string Description { get; }

    /// <inheritdoc/>
    public IReadOnlyList<GuardrailStage> Stages { get; }

    /// <inheritdoc/>
    public Task<GuardrailDecision> InspectAsync(
        GuardrailContext context, CancellationToken cancellationToken = default) =>
        inspect(context, cancellationToken);
}
