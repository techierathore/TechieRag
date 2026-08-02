using System.Text.Json;
using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// What one connector run is asked to do, as stored on a schedule or handed to a hand-started
/// background job (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>This is the whole of the scheduler's knowledge of connectors.</b>
/// <c>Schedule.JobPayload</c> is an opaque string to the scheduling cluster; this record is the shape
/// it holds for <see cref="ConnectorJobHandler.Kind"/> jobs. Nothing source-specific belongs here —
/// a repository branch, a Confluence space key or an IMAP folder is the connector's own
/// configuration, reached through <see cref="ConnectorId"/> by the <see cref="IConnectorResolver"/>.
/// Putting them here would mean the scheduler gaining a field per connector type forever.</para>
/// <para><b>No credential is ever stored on this payload.</b> It is persisted verbatim in the
/// application database and shown in run history; secrets live in the OS credential store and are
/// resolved at run time by the connector itself.</para>
/// </remarks>
public sealed record ConnectorJobPayload
{
    /// <summary>The default cap on items fetched in one run, mirroring the library's own default.</summary>
    public const int DefaultMaxItems = 500;

    /// <summary>The default cap on listing pages walked in one run.</summary>
    public const int DefaultMaxPages = 200;

    /// <summary>The default cap on the size of a single item, in bytes.</summary>
    public const long DefaultMaxItemBytes = 2 * 1024 * 1024;

    /// <summary>The default pause between item fetches, in milliseconds.</summary>
    public const int DefaultRequestDelayMs = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>Gets the saved connector configuration this run reads, as the resolver knows it.</summary>
    public string ConnectorId { get; init; } = string.Empty;

    /// <summary>Gets the kind of source — "repository", "confluence", "email".</summary>
    /// <remarks>Matches <c>IDataConnector.SourceType</c>. Persisted, so it must be stable between releases.</remarks>
    public string ConnectorType { get; init; } = string.Empty;

    /// <summary>Gets the operator-facing name of this run, used as the run-history job name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the workspace ingested documents are linked into, or <see langword="null"/> for the library only.</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>Gets a value indicating whether ingested documents are pinned into workspace context.</summary>
    public bool Pinned { get; init; }

    /// <summary>Gets the hard cap on items fetched in one run.</summary>
    public int MaxItems { get; init; } = DefaultMaxItems;

    /// <summary>Gets the hard cap on listing pages walked in one run.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>Gets the largest single item accepted, in bytes.</summary>
    public long MaxItemBytes { get; init; } = DefaultMaxItemBytes;

    /// <summary>Gets the pause between item fetches, in milliseconds.</summary>
    public int RequestDelayMs { get; init; } = DefaultRequestDelayMs;

    /// <summary>
    /// Gets a value indicating whether items skipped as unchanged are recorded individually.
    /// </summary>
    /// <remarks>
    /// On by default, and deliberately so: BRD-65 asks for every attempted item to carry a result, and
    /// "unchanged since the last run" is the most common reason an operator finds a document missing
    /// from a sync they expected to see it in. The volume risk is handled by
    /// <see cref="Scheduling.JobProgressCollector.SuccessItemCap"/>, which caps retained non-failures
    /// and makes the run say that it did — a bounded, visible truncation rather than a silent one.
    /// </remarks>
    public bool ReportUnchanged { get; init; } = true;

    /// <summary>Reads a payload from its stored JSON form.</summary>
    /// <param name="json">The stored payload, or <see langword="null"/>.</param>
    /// <returns>The payload, or <see langword="null"/> when the string is absent or not readable.</returns>
    /// <remarks>
    /// Never throws. A payload that was hand-edited, truncated or written by an older build must
    /// surface as "this schedule's action is not usable", which the handler reports as a named run
    /// failure — not as an exception escaping onto a background timer.
    /// </remarks>
    public static ConnectorJobPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConnectorJobPayload>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the payload to the string form stored on a schedule.</summary>
    /// <returns>The JSON payload.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Checks the payload independently of any connector type.</summary>
    /// <returns><see langword="null"/> when the payload is usable, otherwise the reason it is not.</returns>
    /// <remarks>
    /// Checked at save time as well as at run time (BRD-136). A schedule that names a connector which
    /// no longer exists must be refused in the confirm dialog, not at 07:00 three days later.
    /// </remarks>
    public JobMessage? Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectorId))
        {
            return JobMessage.Of("ConnectorPayloadNoConnector");
        }

        if (string.IsNullOrWhiteSpace(ConnectorType))
        {
            return JobMessage.Of("ConnectorPayloadNoConnectorType");
        }

        if (MaxItems <= 0)
        {
            return JobMessage.Of("ConnectorPayloadItemLimitTooLow");
        }

        if (MaxPages <= 0)
        {
            return JobMessage.Of("ConnectorPayloadPageLimitTooLow");
        }

        return MaxItemBytes <= 0 ? JobMessage.Of("ConnectorPayloadSizeLimitTooLow") : null;
    }

    /// <summary>Renders the payload as the one-line action summary shown in the schedules grid.</summary>
    /// <returns>A plain-language summary as codes and arguments. Never JSON, never cron.</returns>
    /// <remarks>
    /// Four codes rather than two with a "where" fragment substituted in. "into the document library"
    /// and "into workspace X" are different sentences in Hindi, not one sentence with a noun swapped,
    /// and gluing them is the fragment trap <see cref="Scheduling.CronDescriber"/> records at length.
    /// The connector NAME and the workspace ID go through as arguments because they are data.
    /// </remarks>
    public JobMessage Describe()
    {
        var name = string.IsNullOrWhiteSpace(DisplayName) ? ConnectorId : DisplayName;
        var toLibrary = string.IsNullOrWhiteSpace(WorkspaceId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return toLibrary
                ? JobMessage.Of("ConnectorActionSyncIntoLibrary")
                : JobMessage.Of("ConnectorActionSyncIntoWorkspace", WorkspaceId);
        }

        return toLibrary
            ? JobMessage.Of("ConnectorActionSyncNamedIntoLibrary", name)
            : JobMessage.Of("ConnectorActionSyncNamedIntoWorkspace", name, WorkspaceId);
    }

    /// <summary>Converts the payload's bounds into the library's run options.</summary>
    /// <returns>The run options for this run.</returns>
    public ConnectorRunOptions ToRunOptions() => new()
    {
        MaxItems = MaxItems,
        MaxPages = MaxPages,
        MaxItemBytes = MaxItemBytes,
        RequestDelay = TimeSpan.FromMilliseconds(Math.Max(0, RequestDelayMs)),
        ReportUnchanged = ReportUnchanged,
    };
}
