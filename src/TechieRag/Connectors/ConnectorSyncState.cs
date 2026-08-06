namespace TechieRag.Connectors;

/// <summary>
/// What a previous run saw, so the next run can skip what has not changed (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>The library does not store this.</b> It is a plain, JSON-serialisable value the caller
/// persists wherever it already keeps job state. Inventing a sync-state store here would mean the
/// library owning a database it cannot migrate, in a process it does not control, for callers that
/// already have one.</para>
/// <para><b>Two mechanisms, because one is never enough.</b> <see cref="LastRunUtc"/> is the cheap
/// server-side filter — <c>SINCE</c> on IMAP, <c>lastModified</c> on a wiki — and it is the only one
/// that keeps a large source from being re-listed in full. <see cref="ItemVersions"/> is the exact
/// one: a version that matches means the content is byte-identical, whatever the timestamps claim.
/// A connector may use the first; <see cref="ConnectorRunner"/> always applies the second, so
/// incremental sync works even for a connector that ignores the hint entirely.</para>
/// <para><b>It holds no content and no credentials</b> — only identifiers and versions — so it is
/// safe to persist next to ordinary job records.</para>
/// </remarks>
public sealed class ConnectorSyncState
{
    /// <summary>Gets or sets when the previous run completed, in UTC.</summary>
    /// <remarks>Connectors pass this to the source as a server-side "changed since" filter where the API offers one.</remarks>
    public DateTimeOffset? LastRunUtc { get; set; }

    /// <summary>Gets or sets the version last seen for each item id.</summary>
    /// <remarks>Keyed by <see cref="ConnectorItem.Id"/>, valued by <see cref="ConnectorItem.Version"/>.</remarks>
    public Dictionary<string, string> ItemVersions { get; set; } = [];

    /// <summary>Determines whether an item is unchanged since the previous run.</summary>
    /// <param name="item">The item as listed by this run.</param>
    /// <returns>True when the item's version is known and identical to the one recorded.</returns>
    /// <remarks>
    /// An item with a null <see cref="ConnectorItem.Version"/> is always treated as changed. "The
    /// source would not tell me whether this changed" must fetch, not skip — the alternative silently
    /// freezes such items at whatever the first run happened to see.
    /// </remarks>
    public bool IsUnchanged(ConnectorItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Version is not null
            && ItemVersions.TryGetValue(item.Id, out var known)
            && string.Equals(known, item.Version, StringComparison.Ordinal);
    }
}
