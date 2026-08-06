namespace TechieDesk.Services.Scheduling;

/// <summary>
/// The <see cref="IJobProgressReporter"/> the runner hands to a handler: it accumulates counts and
/// per-item results, and raises a snapshot whenever anything changes (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>Failures are never dropped; successes are capped.</b> A crawl of 20,000 pages would
/// otherwise write 20,000 item rows per run and turn the app database into a log file. Successful and
/// skipped items stop being retained past <see cref="SuccessItemCap"/>, and the run says so.
/// Failures have no cap, because "which ones failed, and why" is the question BRD-65 exists to
/// answer, and a truncated answer to it is worse than none.</para>
/// </remarks>
public sealed class JobProgressCollector : IJobProgressReporter
{
    /// <summary>How many successful or skipped items are retained per run before sampling stops.</summary>
    public const int SuccessItemCap = 200;

    private readonly object gate = new();
    private readonly List<ScheduleRunItem> items = [];
    private readonly Action<JobProgressSnapshot>? onChanged;
    private readonly TimeProvider timeProvider;
    private readonly long scheduleRunId;
    private readonly long? scheduleId;
    private readonly string jobName;
    private readonly string jobKind;
    private readonly DateTime startedUtc;

    private int processed;
    private int failed;
    private int skipped;
    private int retainedSuccesses;
    private int? total;
    private JobMessage? message;

    /// <summary>Initializes the collector for one run.</summary>
    /// <param name="scheduleRunId">The open run row.</param>
    /// <param name="scheduleId">The schedule behind it, or <see langword="null"/>.</param>
    /// <param name="jobName">The job's display name.</param>
    /// <param name="jobKind">The handler key.</param>
    /// <param name="startedUtc">When the run started.</param>
    /// <param name="timeProvider">Clock used to stamp item rows.</param>
    /// <param name="onChanged">Invoked with a fresh snapshot on every change. May be <see langword="null"/>.</param>
    public JobProgressCollector(
        long scheduleRunId,
        long? scheduleId,
        string jobName,
        string jobKind,
        DateTime startedUtc,
        TimeProvider timeProvider,
        Action<JobProgressSnapshot>? onChanged)
    {
        this.scheduleRunId = scheduleRunId;
        this.scheduleId = scheduleId;
        this.jobName = jobName;
        this.jobKind = jobKind;
        this.startedUtc = startedUtc;
        this.timeProvider = timeProvider;
        this.onChanged = onChanged;
    }

    /// <summary>Gets the number of items reported as processed.</summary>
    public int Processed { get { lock (gate) { return processed; } } }

    /// <summary>Gets the number of items reported as failed.</summary>
    public int FailedCount { get { lock (gate) { return failed; } } }

    /// <summary>Gets the number of items reported as skipped.</summary>
    public int SkippedCount { get { lock (gate) { return skipped; } } }

    /// <summary>Gets a value indicating whether the success-item cap was reached.</summary>
    public bool WasItemListSampled { get; private set; }

    /// <inheritdoc />
    public void Report(int processed, int? total, JobMessage? message)
    {
        JobProgressSnapshot snapshot;
        lock (gate)
        {
            // The handler's own count wins over the derived one: a handler that knows it has done 40
            // of 500 without listing each item individually must still be able to move the bar.
            this.processed = Math.Max(this.processed, processed);
            this.total = total ?? this.total;
            this.message = message ?? this.message;
            snapshot = SnapshotLocked();
        }

        onChanged?.Invoke(snapshot);
    }

    /// <inheritdoc />
    public void RecordItem(RunItemStatus status, string itemId, string itemName, JobMessage? reason = null)
    {
        JobProgressSnapshot snapshot;
        lock (gate)
        {
            switch (status)
            {
                case RunItemStatus.Failed:
                    failed++;
                    break;
                case RunItemStatus.Skipped:
                    skipped++;
                    break;
                default:
                    processed++;
                    break;
            }

            var isFailure = status == RunItemStatus.Failed;
            if (isFailure || retainedSuccesses < SuccessItemCap)
            {
                if (!isFailure)
                {
                    retainedSuccesses++;
                }

                items.Add(new ScheduleRunItem
                {
                    ScheduleRunId = scheduleRunId,
                    ItemId = itemId,
                    ItemName = itemName,
                    Status = status,

                    // Both halves of the REQ-UI-056 pair are written together: the codes so a later
                    // reader gets their own language, and the English rendering so the row still
                    // says something in a database browser, in the helper host's log, and to a build
                    // that no longer knows the code.
                    Reason = reason?.ToInvariantString(),
                    ReasonJson = reason?.ToStorage(),
                    RecordedUtc = timeProvider.GetUtcNow().UtcDateTime
                });
            }
            else
            {
                WasItemListSampled = true;
            }

            snapshot = SnapshotLocked();
        }

        onChanged?.Invoke(snapshot);
    }

    /// <summary>Takes a snapshot of the current state.</summary>
    /// <returns>The snapshot.</returns>
    public JobProgressSnapshot Snapshot()
    {
        lock (gate)
        {
            return SnapshotLocked();
        }
    }

    /// <summary>Returns the per-item rows recorded so far.</summary>
    /// <returns>A copy of the recorded items.</returns>
    public IReadOnlyList<ScheduleRunItem> DrainItems()
    {
        lock (gate)
        {
            return items.ToList();
        }
    }

    private JobProgressSnapshot SnapshotLocked() => new(
        scheduleRunId, scheduleId, jobName, jobKind, startedUtc,
        processed, failed, skipped, total, message);
}
