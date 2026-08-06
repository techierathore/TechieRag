namespace TechieRag.Web;

/// <summary>
/// Raised when a URL could not be fetched or was not usable as a document (REQ-RAG-031).
/// </summary>
public sealed class WebFetchException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="WebFetchException"/> class.</summary>
    /// <param name="url">The URL that failed.</param>
    /// <param name="message">What went wrong, in terms an operator can act on.</param>
    public WebFetchException(string url, string message)
        : base(message)
    {
        Url = url;
    }

    /// <summary>Initializes a new instance of the <see cref="WebFetchException"/> class.</summary>
    /// <param name="url">The URL that failed.</param>
    /// <param name="message">What went wrong, in terms an operator can act on.</param>
    /// <param name="innerException">The underlying failure.</param>
    public WebFetchException(string url, string message, Exception innerException)
        : base(message, innerException)
    {
        Url = url;
    }

    /// <summary>Gets the URL that failed.</summary>
    public string Url { get; } = string.Empty;
}
