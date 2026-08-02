namespace TechieRag.Models;

/// <summary>
/// Per-call options for a semantic search, including the optional per-call rerank switch.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller override retrieval behaviour for a single search without
/// mutating library-wide configuration. Introduced for REQ-RAG-047 (TR-RAG-005) so a workspace
/// can turn the second-stage rerank on or off independently of
/// <c>TechieRagConfig.Rerank.Enabled</c>.</para>
/// <para><b>Code Flow:</b> Passed to
/// <see cref="ITechieRag.SearchAsync(string, SearchOptions, System.Threading.CancellationToken)"/>.
/// The legacy positional overload builds one of these with <see cref="Rerank"/> left null, so
/// existing callers keep exactly today's behaviour.</para>
/// <para><b>Back-compat:</b> All properties default to the values the legacy overload used,
/// so <c>new SearchOptions()</c> is equivalent to <c>SearchAsync(query)</c>.</para>
/// </remarks>
public class SearchOptions
{
    /// <summary>Gets or sets the maximum number of results to return. Defaults to 5.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Gets or sets an optional document identifier to restrict the search scope, or null for all documents.</summary>
    public string? DocumentFilter { get; set; }

    /// <summary>
    /// Gets or sets the per-call rerank switch.
    /// </summary>
    /// <remarks>
    /// <para><b>null</b> (default) — fall back to the library-wide <c>TechieRagConfig.Rerank.Enabled</c>
    /// setting. This is the historical behaviour.</para>
    /// <para><b>true</b> — force the rerank stage on for this call, even when the global
    /// <c>Rerank.Enabled</c> flag is false. Has no effect when no <see cref="Abstractions.IReranker"/>
    /// is configured; a warning is logged in that case.</para>
    /// <para><b>false</b> — force the rerank stage off for this call, even when the global
    /// <c>Rerank.Enabled</c> flag is true.</para>
    /// </remarks>
    public bool? Rerank { get; set; }
}
