namespace TechieDesk.Services.Scheduling.Authoring;

/// <summary>How much the interpretation can be trusted (REQ-UI-046).</summary>
public enum DraftConfidence
{
    /// <summary>Something in the draft did not validate; it must be corrected before saving.</summary>
    Low = 0,

    /// <summary>The draft is usable but carries warnings the user should read.</summary>
    Medium = 1,

    /// <summary>Everything validated.</summary>
    High = 2
}

/// <summary>
/// One reviewable, individually-editable line of an interpreted schedule (BRD-140).
/// </summary>
/// <param name="Label">The line's badge — "Runs", "Step 1", "Then".</param>
/// <param name="Text">What it does, in plain language.</param>
/// <param name="Origin">The words in the user's instruction this line came from, when it can be attributed.</param>
public sealed record ScheduleDraftStep(string Label, string Text, string? Origin = null);

/// <summary>
/// The structured, reviewable result of interpreting a natural-language instruction
/// (BRD-140 / REQ-UI-046, ADR-010).
/// </summary>
/// <remarks>
/// <para><b>A draft is not a schedule.</b> Nothing here is persisted until the user confirms it
/// through <see cref="IScheduleService"/>, which refuses a draft the user was not shown. ADR-010 makes
/// the confirm step the whole point: interpretation will sometimes misread an instruction, and this
/// object is what stands between that and a wrong automation running unattended.</para>
/// <para><b><see cref="ScheduleText"/> is computed, never quoted.</b> It comes from
/// <see cref="CronDescriber"/> applied to <see cref="CronExpression"/>, not from the model's own
/// summary. A model that writes <c>0 7 * * 1-5</c> and then describes it as "every morning" would
/// otherwise get the user to confirm a sentence the schedule does not mean — and the sentence is the
/// only part most people will read.</para>
/// </remarks>
public sealed record ScheduleDraft
{
    /// <summary>Gets the proposed job name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the instruction this draft was interpreted from.</summary>
    public required string Instruction { get; init; }

    /// <summary>Gets the five-field cron expression. Shown only behind the Advanced disclosure (BRD-140).</summary>
    public required string CronExpression { get; init; }

    /// <summary>Gets the time-zone id the expression's wall clock belongs to.</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>Gets the plain-language schedule text, computed from the expression.</summary>
    public required string ScheduleText { get; init; }

    /// <summary>Gets the handler key the draft will run.</summary>
    public required string JobKind { get; init; }

    /// <summary>Gets the handler-specific payload, as JSON.</summary>
    public string? JobPayload { get; init; }

    /// <summary>Gets the one-line action summary, as the handler describes the payload.</summary>
    public required string ActionSummary { get; init; }

    /// <summary>Gets the reviewable lines — trigger, every step, and the delivery action.</summary>
    /// <remarks>
    /// The UI design is explicit that the confirm panel shows the <b>full</b> understood result and
    /// never a one-line summary, because a summary hides exactly the misreading the confirm step
    /// exists to catch.
    /// </remarks>
    public required IReadOnlyList<ScheduleDraftStep> Steps { get; init; }

    /// <summary>Gets how much of the interpretation validated.</summary>
    public DraftConfidence Confidence { get; init; } = DraftConfidence.High;

    /// <summary>Gets everything the user should read before confirming. Empty when there is nothing.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Gets the next occurrences, for the preview under the confirm panel.</summary>
    public IReadOnlyList<DateTime> NextRunsUtc { get; init; } = [];

    /// <summary>Gets a value indicating whether a run missed while the machine was off is caught up.</summary>
    public bool CatchUpMissedRuns { get; init; } = true;

    /// <summary>Gets a value indicating whether a failed run raises a notification.</summary>
    public bool NotifyOnFailure { get; init; } = true;

    /// <summary>Gets a value indicating whether this draft is safe to offer for confirmation.</summary>
    public bool IsSavable => Confidence != DraftConfidence.Low;
}
