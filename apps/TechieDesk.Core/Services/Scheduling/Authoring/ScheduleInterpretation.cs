namespace TechieDesk.Services.Scheduling.Authoring;

/// <summary>
/// The result of asking the configured local model to interpret an instruction (REQ-UI-046).
/// </summary>
/// <param name="Draft">The reviewable draft, or <see langword="null"/> when interpretation failed.</param>
/// <param name="Error">Why it failed, in plain language. Null on success.</param>
public sealed record ScheduleInterpretation(ScheduleDraft? Draft, string? Error = null)
{
    /// <summary>Gets a value indicating whether a draft was produced.</summary>
    public bool Succeeded => Draft is not null;

    /// <summary>Creates a failed interpretation.</summary>
    /// <param name="error">Why it failed, in terms the user can act on.</param>
    /// <returns>The failure.</returns>
    public static ScheduleInterpretation Failed(string error) => new(null, error);
}

/// <summary>
/// An action the interpreter is allowed to choose between (BRD-140: the model selects, it never
/// invents).
/// </summary>
/// <param name="JobKind">The handler key.</param>
/// <param name="DisplayName">The action's human name.</param>
/// <param name="Description">What it does.</param>
/// <remarks>
/// <b>Both display members are resource KEYS, not names (REQ-UI-056).</b> This record feeds two
/// audiences at once: the model, which is sent the ENGLISH names because the prompt is machine text
/// and translating it would change what the model is asked for, and the user, who reads the same
/// action list back in a refusal ("I only know how to …"). Carrying keys lets each consumer resolve
/// for its own audience — <c>JobMessage.Neutral</c> for the prompt, the reader's localizer for the
/// refusal. Before this, a Hindi refusal named the actions in English.
/// </remarks>
public sealed record AvailableAction(string JobKind, string DisplayNameKey, string DescriptionKey);

/// <summary>
/// Turns a natural-language instruction into a reviewable schedule draft, using the configured local
/// model (BRD-140 / REQ-UI-046, ADR-010).
/// </summary>
public interface IScheduleInterpreter
{
    /// <summary>Gets the actions interpretation is constrained to.</summary>
    IReadOnlyList<AvailableAction> AvailableActions { get; }

    /// <summary>Gets a value indicating whether a model is configured to interpret with.</summary>
    bool IsModelAvailable { get; }

    /// <summary>Interprets an instruction.</summary>
    /// <param name="instruction">What the user typed, in their own words.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A reviewable draft, or a plain-language reason it could not be produced.</returns>
    Task<ScheduleInterpretation> InterpretAsync(string instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds a draft around a different cron expression — the Advanced disclosure's edit path.
    /// </summary>
    /// <param name="draft">The draft being edited.</param>
    /// <param name="cronExpression">The replacement expression.</param>
    /// <returns>The rebuilt draft, or a reason the expression is unusable.</returns>
    /// <remarks>
    /// Rebuilding, not patching: the plain-language text and the next-run preview are both derived
    /// from the expression, so editing the expression without recomputing them would leave the user
    /// confirming a sentence that no longer describes the schedule.
    /// </remarks>
    ScheduleInterpretation Rebuild(ScheduleDraft draft, string cronExpression);
}
