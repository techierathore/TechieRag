namespace TechieRag.Models;

/// <summary>
/// The context a workspace assembled for a question, together with the truncation diagnostics
/// that describe what was dropped to fit the prompt's context budget.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-RAG-048 (TR-RAG-006). <c>PromptConfig.MaxContextChunks</c> used to
/// truncate the merged pinned + retrieved context silently inside the prompt template, so a
/// caller could not tell that chunks it had been handed never reached the model. This type makes
/// the truncation observable: <see cref="WasTruncated"/> plus per-category eviction counts.</para>
/// <para><b>Eviction policy (deliberate, not incidental):</b> Pinned chunks are kept ahead of
/// retrieved chunks. Retrieved chunks are evicted from the tail first; only when the pinned
/// chunks alone exceed <see cref="MaxContextChunks"/> are pinned chunks themselves evicted, and
/// then the lowest-ranked pinned chunks go first.</para>
/// <para><b>Code Flow:</b> Produced by
/// <see cref="Services.WorkspaceManager.BuildContextWithDiagnosticsAsync"/> and used internally
/// by the workspace ask/stream paths, so <see cref="Results"/> is exactly the context the LLM
/// receives.</para>
/// </remarks>
public class WorkspaceContext
{
    /// <summary>Gets the context actually handed to the prompt template, pinned chunks first.</summary>
    public required IReadOnlyList<SearchResult> Results { get; init; }

    /// <summary>Gets the number of pinned-document chunks retained in <see cref="Results"/>.</summary>
    public required int PinnedIncluded { get; init; }

    /// <summary>Gets the number of retrieved (non-pinned) chunks retained in <see cref="Results"/>.</summary>
    public required int RetrievedIncluded { get; init; }

    /// <summary>Gets the number of pinned-document chunks dropped by the context budget.</summary>
    /// <remarks>Non-zero only when the pinned chunks alone exceed <see cref="MaxContextChunks"/>.</remarks>
    public required int PinnedEvicted { get; init; }

    /// <summary>Gets the number of retrieved (non-pinned) chunks dropped by the context budget.</summary>
    public required int RetrievedEvicted { get; init; }

    /// <summary>Gets the context budget applied, from <c>PromptConfig.MaxContextChunks</c>.</summary>
    /// <remarks>A value of zero or less means no limit was applied.</remarks>
    public required int MaxContextChunks { get; init; }

    /// <summary>Gets the total number of chunks dropped by the context budget.</summary>
    public int EvictedCount => PinnedEvicted + RetrievedEvicted;

    /// <summary>Gets a value indicating whether any chunk was dropped to fit the context budget.</summary>
    /// <remarks>This is the programmatic truncation signal callers should branch on; it is false
    /// whenever the composed context fit within <see cref="MaxContextChunks"/>.</remarks>
    public bool WasTruncated => EvictedCount > 0;
}
