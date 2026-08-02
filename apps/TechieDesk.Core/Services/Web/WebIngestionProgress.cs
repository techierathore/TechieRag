namespace TechieDesk.Services.Web;

/// <summary>
/// What a web ingestion is doing right now (REQ-RAG-016/017/018).
/// </summary>
public enum WebIngestionStage
{
    /// <summary>The request has been accepted and nothing has been fetched yet.</summary>
    Starting,

    /// <summary>A page request is in flight.</summary>
    Fetching,

    /// <summary>A page came back and was readable.</summary>
    Fetched,

    /// <summary>A page could not be read. The run continues; the reason is kept.</summary>
    Failed,

    /// <summary>Fetching is finished and the text is being chunked and embedded.</summary>
    Embedding,

    /// <summary>The run is over.</summary>
    Done,
}

/// <summary>
/// One progress report from a web ingestion (REQ-RAG-017).
/// </summary>
/// <param name="Stage">What is happening.</param>
/// <param name="Url">The URL the stage refers to.</param>
/// <param name="Completed">Fetches attempted so far.</param>
/// <param name="Total">Fetches this run may make at most.</param>
/// <param name="Message">A line the operator can read, already phrased for display.</param>
/// <remarks>
/// A crawl of 25 pages with a politeness delay takes minutes. Without reports the screen has nothing
/// to show but a spinner, and a spinner cannot be told apart from a hang — which is the state that
/// makes people force-quit an app that was working.
/// </remarks>
public sealed record WebIngestionProgress(
    WebIngestionStage Stage,
    string Url,
    int Completed,
    int Total,
    string Message)
{
    /// <summary>Gets the completion percentage, clamped to 0-100.</summary>
    /// <remarks>
    /// <see cref="Total"/> is the page BUDGET, not a count of pages that exist, so a crawl that runs
    /// out of links finishes at less than 100%. Clamping stops the opposite lie — a bar past the end
    /// — which is the one that looks like a bug.
    /// </remarks>
    public int Percent => Total <= 0
        ? 0
        : (int)Math.Round(Math.Clamp(Completed / (double)Total, 0d, 1d) * 100);
}
