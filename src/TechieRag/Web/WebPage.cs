namespace TechieRag.Web;

/// <summary>
/// A fetched web page reduced to the parts ingestion cares about (REQ-RAG-031 / BRD-112).
/// </summary>
/// <param name="RequestedUrl">The URL that was asked for.</param>
/// <param name="FinalUrl">Where it ended up after redirects. Crawl bookkeeping keys on this.</param>
/// <param name="Title">The document title, or the host when the page carries none.</param>
/// <param name="Text">Readable text with scripts, styles and chrome removed.</param>
/// <param name="Links">Absolute links discovered on the page, de-duplicated, in document order.</param>
public sealed record WebPage(
    string RequestedUrl,
    string FinalUrl,
    string Title,
    string Text,
    IReadOnlyList<string> Links);
