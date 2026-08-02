namespace TechieDesk.Services.Agents;

/// <summary>
/// The single wording a skill uses when it is implemented but cannot run on this install
/// (REQ-RAG-022 acceptance 5).
/// </summary>
/// <remarks>
/// <para><b>Why a shared phrase rather than an exception.</b> A tool that throws ends the agent
/// turn; a tool that returns an empty result is indistinguishable from "there was nothing to
/// find". Neither tells the model — or the execution trace — that the skill was never able to run.
/// Returning a marked sentence does: the loop can report it, the trace records it, and the model
/// can say so instead of inventing an answer.</para>
/// <para><b>This is not an error channel.</b> A skill that ran and failed (a page that would not
/// load, a query the guard refused) says so in its own words. <c>unavailable:</c> means the skill
/// has no configured dependency to run against at all.</para>
/// </remarks>
public static class SkillUnavailable
{
    /// <summary>The prefix every unavailability message starts with.</summary>
    public const string Marker = "unavailable:";

    /// <summary>
    /// Builds the message a skill returns when a dependency it needs is not configured.
    /// </summary>
    /// <param name="reason">
    /// What is missing and what would fix it, in terms the workspace owner can act on.
    /// </param>
    /// <returns>The marked message handed back to the model.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is blank.</exception>
    public static string Because(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return $"{Marker} {reason.Trim()}";
    }

    /// <summary>
    /// Gets whether a tool result is an unavailability report rather than an answer.
    /// </summary>
    /// <param name="result">The text a skill returned.</param>
    /// <returns>True when the skill reported itself unavailable.</returns>
    public static bool IsUnavailable(string? result) =>
        result is not null && result.StartsWith(Marker, StringComparison.Ordinal);
}
