namespace TechieDesk.Services.AppManager;

/// <summary>
/// Exception thrown for every failed AppManager API call, carrying the raw wire error code,
/// its typed <see cref="AppManagerError"/> mapping, and the HTTP status code.
/// </summary>
public sealed class AppManagerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppManagerException"/> class.
    /// </summary>
    /// <param name="errorCode">The raw wire error code (e.g. <c>INVALID_CREDENTIALS</c>).</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="statusCode">The HTTP status code of the response, or 0 for local failures.</param>
    public AppManagerException(string errorCode, string message, int statusCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
        Error = AppManagerErrorMapper.Map(errorCode);
        StatusCode = statusCode;
    }

    /// <summary>Gets the raw wire error code returned by AppManager.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets the typed error classification.</summary>
    public AppManagerError Error { get; }

    /// <summary>Gets the HTTP status code of the failing response (0 when not applicable).</summary>
    public int StatusCode { get; }
}
