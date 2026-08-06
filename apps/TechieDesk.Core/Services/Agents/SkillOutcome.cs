using TechieRag.Orchestration;

namespace TechieDesk.Services.Agents;

/// <summary>
/// What a skill produced: the text the MODEL reads, and — when the product authored it — the same
/// fact as a code a PERSON can read in their own language (REQ-UI-059 clause 3 / BRD-91).
/// </summary>
/// <param name="Text">
/// The tool result handed to the model. Always English and always invariant: it is prompt content,
/// and translating it would change what the model is told depending on who is looking at the screen.
/// </param>
/// <param name="Message">
/// The localizable form of <paramref name="Text"/> for the execution trace, or null when the text is
/// the skill's own DATA — a fetched page, a query result — which is nobody's to translate.
/// </param>
/// <remarks>
/// <para><b>The premise that was wrong.</b> Two clusters classified the <c>unavailable: …</c>
/// sentences as machine-facing and out of localization scope. The reasoning was right — do not
/// translate what the model reads — but the premise was not: the UI renders that tool-result content
/// <b>verbatim to the user</b> in the execution trace. So a Hindi user met English at the exact
/// moment a skill refused to run. The fix is not to translate what the model reads; it is to carry
/// BOTH and let each audience get its own.</para>
/// <para><b>A string converts implicitly, on purpose.</b> The overwhelming majority of skill returns
/// are data — search hits, file contents, a SQL result set — with nothing to code. Those keep
/// returning a bare string and read exactly as they did; only a signature changes. Requiring every
/// skill to wrap its data in a ceremony object would have made the common case pay for the rare one.
/// </para>
/// </remarks>
public sealed record SkillOutcome(string Text, FlowMessage? Message = null)
{
    /// <summary>Wraps a plain skill result that carries no translatable product wording.</summary>
    /// <param name="text">The result text.</param>
    public static implicit operator SkillOutcome(string text) => new(text);

    /// <summary>Projects an outcome to the text the model reads.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <remarks>
    /// <b>Safe because there is exactly one production consumer.</b> Only
    /// <see cref="SkillToolHandler"/> turns an outcome into a <c>ToolResult</c>, and it reads BOTH
    /// halves explicitly — so a conversion cannot quietly drop the code at the seam that matters.
    /// Everywhere else an outcome is used, the model-facing text is genuinely the whole value.
    /// </remarks>
    public static implicit operator string(SkillOutcome outcome) => outcome?.Text ?? string.Empty;

    /// <summary>Builds an outcome from plain text, for callers that prefer a named method.</summary>
    /// <param name="text">The result text.</param>
    /// <returns>The outcome.</returns>
    public static SkillOutcome FromText(string text) => new(text);

    /// <summary>Returns the model-facing text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
