namespace TechieRag.Models;

/// <summary>
/// Event payload raised when a workspace's composed context did not fit the configured
/// context budget and chunks had to be evicted.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-RAG-048 (TR-RAG-006). Gives callers a push-based truncation signal
/// in addition to the pull-based <see cref="WorkspaceContext.WasTruncated"/> flag, so a UI can
/// warn the user ("3 of 8 sources were dropped") without inspecting every context build.</para>
/// <para><b>Code Flow:</b> Raised by
/// <see cref="Services.WorkspaceManager.ContextTruncated"/> from every workspace retrieval path —
/// AskAsync, AskStreamWithSourcesAsync, BuildContextAsync — immediately before the context is
/// handed to the prompt template.</para>
/// </remarks>
public class ContextTruncatedEventArgs : EventArgs
{
    /// <summary>Gets the workspace whose context was truncated.</summary>
    public required string WorkspaceId { get; init; }

    /// <summary>Gets the question the context was composed for.</summary>
    public required string Question { get; init; }

    /// <summary>Gets the composed context with its full truncation diagnostics.</summary>
    public required WorkspaceContext Context { get; init; }
}
