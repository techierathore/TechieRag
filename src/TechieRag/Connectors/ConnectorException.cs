namespace TechieRag.Connectors;

/// <summary>
/// Raised when a connector run cannot proceed at all (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para><b>Reserved for run-level failure.</b> A single item that will not fetch is a
/// <see cref="ConnectorItemFailure"/>, not an exception — see <see cref="ConnectorRunner"/>. This
/// type is for the failures that make every remaining item pointless: bad credentials, a repository
/// that does not exist, a refused plaintext connection, an exhausted rate-limit budget.</para>
/// <para><b>Messages never carry credentials.</b> The message is built from the source type, the
/// endpoint and the status — never from a request header or a URL that was given a token — because
/// exception messages end up in logs, bug reports and screenshots.</para>
/// </remarks>
public sealed class ConnectorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConnectorException"/> class.</summary>
    /// <param name="sourceType">The connector's source type, e.g. "repository".</param>
    /// <param name="message">What went wrong, in terms an operator can act on.</param>
    public ConnectorException(string sourceType, string message)
        : base(message)
    {
        SourceType = sourceType;
    }

    /// <summary>Initializes a new instance of the <see cref="ConnectorException"/> class.</summary>
    /// <param name="sourceType">The connector's source type, e.g. "repository".</param>
    /// <param name="message">What went wrong, in terms an operator can act on.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ConnectorException(string sourceType, string message, Exception innerException)
        : base(message, innerException)
    {
        SourceType = sourceType;
    }

    /// <summary>Initializes a new instance of the <see cref="ConnectorException"/> class.</summary>
    /// <param name="sourceType">The connector's source type, e.g. "repository".</param>
    /// <param name="message">What went wrong, in terms an operator can act on.</param>
    /// <param name="statusCode">The HTTP status that caused it, when there was one.</param>
    public ConnectorException(string sourceType, string message, int statusCode)
        : base(message)
    {
        SourceType = sourceType;
        StatusCode = statusCode;
    }

    /// <summary>Gets the source type of the connector that failed.</summary>
    public string SourceType { get; } = string.Empty;

    /// <summary>Gets the HTTP status that caused the failure, or null when it was not an HTTP failure.</summary>
    /// <remarks>401 and 403 mean the caller should re-check the credential it supplied; 429 means the run hit a rate limit it could not wait out.</remarks>
    public int? StatusCode { get; }
}
