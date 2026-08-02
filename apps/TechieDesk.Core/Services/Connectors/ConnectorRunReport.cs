using TechieDesk.Services.Localization;
using TechieDesk.Services.Scheduling;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// One item a connector run touched, with the reason it was not ingested (BRD-65).
/// </summary>
/// <param name="ItemId">The source's identifier, so a retry can name it.</param>
/// <param name="ItemName">The human-facing name — a path, a page title, a subject line.</param>
/// <param name="Status">Whether it was ingested, skipped, or failed.</param>
/// <param name="Reason">Why, in operator terms. Never contains a credential.</param>
/// <param name="RecordedUtc">When the result was recorded.</param>
public sealed record ConnectorRunItem(
    string ItemId,
    string ItemName,
    RunItemStatus Status,
    string? Reason,
    DateTime RecordedUtc);

/// <summary>
/// What one connector run did, in full, phrased so it can never overstate itself
/// (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para>The connector equivalent of <c>WebIngestionOutcome</c>, and it follows the same honesty
/// rule: a run with skips is never described as a success. "Ingested 47 documents" while twelve items
/// were dropped is technically true and practically a lie, because the operator's next act is to
/// search for content that was never added.</para>
/// <para>This is the shape the connector hub binds to for a finished run. The live view of a run in
/// flight is <see cref="JobProgressSnapshot"/>, from
/// <see cref="IConnectorJobService.ActiveRuns"/>.</para>
/// </remarks>
/// <param name="RunId">The run row this describes.</param>
/// <param name="ScheduleId">The schedule behind it, or <see langword="null"/> for a hand-started run.</param>
/// <param name="JobName">The run's display name, captured when it started.</param>
/// <param name="Trigger">What caused the run.</param>
/// <param name="Outcome">How it ended, or <see cref="RunOutcome.Running"/> while in flight.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="CompletedUtc">When it finished, or <see langword="null"/> while in flight.</param>
/// <param name="Detail">The run's one-line detail as recorded.</param>
/// <param name="FailureReason">Why the run itself stopped, when it did.</param>
/// <param name="Items">Every item the run recorded a result for.</param>
public sealed record ConnectorRunReport(
    long RunId,
    long? ScheduleId,
    string JobName,
    RunTrigger Trigger,
    RunOutcome Outcome,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string? Detail,
    string? FailureReason,
    IReadOnlyList<ConnectorRunItem> Items)
{
    private readonly IReadOnlyList<ConnectorRunItem> ingested =
        Items.Where(item => item.Status == RunItemStatus.Processed).ToList();

    private readonly IReadOnlyList<ConnectorRunItem> failed =
        Items.Where(item => item.Status == RunItemStatus.Failed).ToList();

    private readonly IReadOnlyList<ConnectorRunItem> skipped =
        Items.Where(item => item.Status == RunItemStatus.Skipped).ToList();

    /// <summary>Gets the items now in the document library.</summary>
    public IReadOnlyList<ConnectorRunItem> Ingested => ingested;

    /// <summary>Gets the items that could not be read, each with its reason.</summary>
    public IReadOnlyList<ConnectorRunItem> Failed => failed;

    /// <summary>Gets the items deliberately not read — unchanged, oversized, or empty — each with its reason.</summary>
    public IReadOnlyList<ConnectorRunItem> Skipped => skipped;

    /// <summary>Gets everything that was attempted but did not reach the library, failures first.</summary>
    /// <remarks>
    /// The list an operator actually wants: "what did I not get, and why". Failures lead because a
    /// failure is the one an operator can usually do something about.
    /// </remarks>
    public IReadOnlyList<ConnectorRunItem> NotIngested => [.. failed, .. skipped];

    /// <summary>Gets a value indicating whether the run is still in flight.</summary>
    public bool IsRunning => Outcome == RunOutcome.Running;

    /// <summary>Gets a value indicating whether some content was ingested and some was not.</summary>
    public bool IsPartial => ingested.Count > 0 && (failed.Count > 0 || skipped.Count > 0);

    /// <summary>Gets the run duration, or <see langword="null"/> while it is in flight.</summary>
    public TimeSpan? Duration => CompletedUtc is { } completed ? completed - StartedUtc : null;

    /// <summary>
    /// Builds a one-line summary, in the reader's language, that never claims more than the run
    /// achieved.
    /// </summary>
    /// <param name="localize">Resolves the resource keys the summary is assembled from.</param>
    /// <returns>The summary line the connector hub shows against the run.</returns>
    /// <remarks>
    /// <para>Cancellation is stated as a stop that kept its work, not as a failure and not as a
    /// success: the documents already ingested are in the library and searchable, and telling the
    /// operator otherwise sends them to re-run something that is already half done.</para>
    /// <para><b>REQ-UI-051 / BRD-91.</b> Every arm below was an English literal built in this
    /// service and rendered raw by the connector hub, which neither razor counter can see. It takes
    /// a <see cref="LocalizeText"/> rather than returning a key because the sentence is genuinely
    /// COMPOSED here — a head, a pluralized count and an optional "and here is what did not make it"
    /// tail — and the honesty rule that assembles those three parts is the whole point of this type.
    /// Pushing it into the page would put the rule where the next screen can get it wrong.</para>
    /// <para>Pluralization goes through separate keys rather than an <c>s</c> appended in code.
    /// Hindi does not form a plural that way, and "4 documents" rendered as "4 दस्तावेज़s" is the
    /// tell-tale of a counter that was never really translated.</para>
    /// </remarks>
    public string SummaryText(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return Outcome switch
        {
            RunOutcome.Running => ingested.Count == 0
                ? localize("ConnectorRunSummaryRunning")
                : localize("ConnectorRunSummaryRunningWithCount", Documents(localize, ingested.Count)),
            RunOutcome.Cancelled => localize(
                "ConnectorRunSummaryStopped",
                Documents(localize, ingested.Count),
                NotIngestedTail(localize)),
            RunOutcome.Failed => ingested.Count == 0
                ? localize("ConnectorRunSummaryFailedNothing", FailureText(localize))
                : localize(
                    "ConnectorRunSummaryFailedAfter",
                    Documents(localize, ingested.Count),
                    FailureText(localize)),
            RunOutcome.Skipped => localize("ConnectorRunSummarySkipped"),
            _ when ingested.Count == 0 && failed.Count == 0 && skipped.Count == 0 =>
                localize("ConnectorRunSummaryEmptySource"),
            _ when ingested.Count == 0 && failed.Count == 0 =>
                localize("ConnectorRunSummaryNothingNew", SourceItems(localize, skipped.Count)),
            _ when ingested.Count == 0 && skipped.Count > 0 => localize(
                "ConnectorRunSummaryNoneReadableWithSkips",
                SourceItems(localize, failed.Count),
                SourceItems(localize, skipped.Count)),
            _ when ingested.Count == 0 =>
                localize("ConnectorRunSummaryNoneReadable", SourceItems(localize, failed.Count)),
            _ => localize(
                "ConnectorRunSummaryIngested",
                Documents(localize, ingested.Count),
                NotIngestedTail(localize)),
        };
    }

    /// <summary>Builds a report from a recorded run and its per-item rows.</summary>
    /// <param name="run">The run row.</param>
    /// <param name="items">The per-item rows recorded against it.</param>
    /// <returns>The report.</returns>
    public static ConnectorRunReport From(ScheduleRun run, IReadOnlyList<ScheduleRunItem> items)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(items);

        return new ConnectorRunReport(
            run.ScheduleRunId,
            run.ScheduleId,
            run.JobName,
            run.TriggerKind,
            run.Outcome,
            run.StartedUtc,
            run.CompletedUtc,
            run.Detail,
            run.FailureReason,
            items
                .Select(item => new ConnectorRunItem(
                    item.ItemId, item.ItemName, item.Status, item.Reason, item.RecordedUtc))
                .ToList());
    }

    /// <summary>Renders the "and here is what did not make it" half of the summary.</summary>
    /// <param name="localize">Resolves the resource keys the tail is built from.</param>
    /// <returns>The tail, already punctuated for the reader's language.</returns>
    /// <remarks>
    /// Four whole keys rather than clauses joined with a hard-coded <c>;</c> and <c>.</c>. Devanagari
    /// ends a sentence with <c>।</c>, so punctuation belongs to the translation and not to this
    /// method — and a translator handed "; {0}, {1}." has no way to reorder the two counts, which
    /// Hindi word order needs.
    /// </remarks>
    private string NotIngestedTail(LocalizeText localize) => (failed.Count, skipped.Count) switch
    {
        (0, 0) => localize("ConnectorRunTailNone"),
        (> 0, 0) => localize("ConnectorRunTailFailed", SourceItems(localize, failed.Count)),
        (0, > 0) => localize("ConnectorRunTailSkipped", SourceItems(localize, skipped.Count)),
        _ => localize(
            "ConnectorRunTailBoth", SourceItems(localize, failed.Count), SourceItems(localize, skipped.Count)),
    };

    /// <summary>Renders the reason a run stopped, falling back to a translated generic.</summary>
    /// <param name="localize">Resolves the fallback key.</param>
    /// <returns>The recorded reason, or "the run failed" in the reader's language.</returns>
    /// <remarks>
    /// <see cref="FailureReason"/> is what the connector or the runner recorded on the run row when
    /// it stopped. It is a stored value from an earlier moment, so it is shown as it was recorded;
    /// only the fallback used when nothing was recorded is this type's to translate.
    /// </remarks>
    private string FailureText(LocalizeText localize) =>
        FailureReason ?? localize("ConnectorRunSummaryRunFailed");

    /// <summary>Renders a count of catalogue documents in the reader's language.</summary>
    /// <param name="localize">Resolves the singular or plural key.</param>
    /// <param name="count">How many.</param>
    /// <returns>"1 document" or "4 documents", translated.</returns>
    private static string Documents(LocalizeText localize, int count) =>
        localize(count == 1 ? "ConnectorRunCountDocument" : "ConnectorRunCountDocuments", count);

    /// <summary>Renders a count of source items in the reader's language.</summary>
    /// <param name="localize">Resolves the singular or plural key.</param>
    /// <param name="count">How many.</param>
    /// <returns>"1 item" or "4 items", translated.</returns>
    private static string SourceItems(LocalizeText localize, int count) =>
        localize(count == 1 ? "ConnectorRunCountItem" : "ConnectorRunCountItems", count);
}
