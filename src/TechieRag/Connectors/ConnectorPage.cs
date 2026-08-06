namespace TechieRag.Connectors;

/// <summary>
/// One page of a connector's listing (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Cursor, not page number.</b> The sources behind these connectors page in three different
/// ways — offset, page number, and opaque continuation token — and two of them silently skip or
/// repeat items when the collection changes mid-walk. An opaque string the connector alone
/// interprets is the only shape that fits all three, and it keeps the runner from inventing
/// arithmetic that is wrong for two hosts out of three.</para>
/// <para><b>A page can carry failures.</b> Listing is not all-or-nothing either: a repository tree
/// that the host truncated, or a mail folder that cannot be selected, is a partial result the run
/// should report and continue from — not an exception that discards the pages already walked.</para>
/// </remarks>
/// <param name="Items">Items found in this page, in source order.</param>
/// <param name="NextCursor">Opaque cursor for the next page, or null when the listing is complete.</param>
/// <param name="Failures">Problems encountered while listing this page. Reported, never silently dropped.</param>
public sealed record ConnectorPage(
    IReadOnlyList<ConnectorItem> Items,
    string? NextCursor = null,
    IReadOnlyList<ConnectorItemFailure>? Failures = null)
{
    /// <summary>Gets a page with no items and no continuation.</summary>
    public static ConnectorPage Empty { get; } = new([], null, []);
}
