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
    /// Gets or sets a set of document identifiers to restrict the search to, searched together in one
    /// vector query (REQ-RAG-114 / BRD-174), or null for no set filter.
    /// </summary>
    /// <remarks>
    /// <para>Takes precedence over <see cref="DocumentFilter"/> when it holds at least one id. An empty
    /// set is treated as no set filter, exactly like null; a caller that wants no results should not
    /// search.</para>
    /// <para>Reaches the store through <c>IVectorStore.SearchDocumentsAsync</c>: SqliteVec, PgVector
    /// and Qdrant filter natively in one query; a third-party store without an override falls back to
    /// one query per document, merged by score.</para>
    /// </remarks>
    public IReadOnlyCollection<string>? DocumentFilters { get; set; }

    /// <summary>
    /// Gets or sets metadata every result must carry: key and text pairs, all of which must match, applied inside the
    /// vector store, or null for no metadata filter (REQ-RAG-169, TrStudio TR-RAG-003).
    /// </summary>
    /// <remarks>
    /// <para>Scopes a search to one owner, character or tenant without over-fetching:
    /// <c>new SearchOptions { TopK = 5, MetadataFilter = new Dictionary&lt;string, string&gt; { ["userId"] = userId } }</c>
    /// returns the five best chunks whose metadata has <c>userId</c> equal to that text. Combines with
    /// <see cref="DocumentFilter"/> and <see cref="DocumentFilters"/>. An empty dictionary is no filter.</para>
    /// <para><b>Matching:</b> a value is compared as text, ordinally; a number or a true/false is compared by its
    /// JSON form (<c>42</c>, <c>true</c>). Store the values you filter on as text. A key may not hold a double
    /// quote or a backslash.</para>
    /// <para><b>Where it runs:</b> SqliteVec and PgVector filter in their own query, so the result is the best
    /// <see cref="TopK"/> among matching chunks. Qdrant and a third-party store without an override of
    /// <c>IVectorStore.SearchMatchingAsync</c> fetch wider candidate sets and filter them, which returns the same
    /// result more slowly. An <c>ITechieRag</c> implementation that does not override the options overload throws
    /// <see cref="NotSupportedException"/> rather than return unfiltered results.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? MetadataFilter { get; set; }

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
