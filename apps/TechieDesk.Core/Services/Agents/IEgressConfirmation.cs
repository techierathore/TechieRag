namespace TechieDesk.Services.Agents;

/// <summary>
/// What the user is being asked to approve before an agent makes its first outbound request of a
/// turn (REQ-NFR-013).
/// </summary>
/// <param name="SkillName">The catalogue name of the skill that wants to leave the machine.</param>
/// <param name="DisplayName">The catalogue label for that skill, as the Skills tab shows it.</param>
/// <param name="Description">The catalogue one-liner explaining what the skill does.</param>
/// <param name="AgentDisplayName">The agent asking, so the prompt can name who wants to go out.</param>
/// <param name="DisplayNameKey">
/// Resource key for the label, when the skill is a catalogue entry. Null for a run-time tool.
/// </param>
/// <param name="DescriptionKey">
/// Resource key for the one-liner, when the skill is a catalogue entry. Null for a run-time tool.
/// </param>
/// <remarks>
/// <para>
/// The request deliberately carries no URL or query. The gate is asked BEFORE the tool runs, so the
/// only thing known at that point is which skill is about to run and on whose behalf; showing a
/// target the gate has not actually validated would be a guarantee the prompt cannot keep.
/// </para>
/// <para>
/// <b>REQ-UI-051: two ways to name the same thing, on purpose.</b> A catalogue skill has a
/// translated label and supplies the KEYS; an MCP tool has a name its own server chose, which is
/// run-time data nobody can translate, and supplies the plain strings. The prompt prefers a key
/// when it has one. Collapsing the two would mean either translating a server's tool name or
/// leaving the six shipped skills in English.
/// </para>
/// </remarks>
public sealed record EgressConfirmationRequest(
    string SkillName,
    string DisplayName,
    string Description,
    string AgentDisplayName,
    string? DisplayNameKey = null,
    string? DescriptionKey = null);

/// <summary>
/// Asks the person at the keyboard whether an agent may make a request that leaves this machine
/// (REQ-NFR-013, the enforcement half of <see cref="AgentDefinition.ConfirmEgress"/>).
/// </summary>
/// <remarks>
/// <para><b>Why a seam.</b> The confirmation is inherently asynchronous and UI-bound: it is a modal
/// the user answers, not a value the loop can read. Putting it behind an interface lets the chat
/// page supply a dialog while tests drive the same gate deterministically, without the enforcement
/// logic living in a Razor file where it cannot be tested at all.</para>
/// <para><b>Absence is a denial, never a permission.</b> <see cref="EgressGate"/> denies when no
/// implementation is supplied. A host that forgets to register a confirmer must fail closed —
/// silently permitting egress is the exact defect this seam exists to close.</para>
/// </remarks>
public interface IEgressConfirmation
{
    /// <summary>
    /// Asks the user to approve one turn's outbound requests.
    /// </summary>
    /// <param name="request">What is about to leave the machine, and on whose behalf.</param>
    /// <param name="cancellationToken">
    /// Token cancelled when the turn's time limit expires; a cancelled prompt is a denial.
    /// </param>
    /// <returns>True when the user approves; false when the user declines or dismisses the prompt.</returns>
    /// <remarks>
    /// Implementations must not return until the user has answered. Returning true optimistically
    /// while a dialog is still open would let the request go out while the confirmation is pending,
    /// which is the one behaviour REQ-NFR-013 forbids.
    /// </remarks>
    Task<bool> ConfirmAsync(EgressConfirmationRequest request, CancellationToken cancellationToken);
}
