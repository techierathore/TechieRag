namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Standard AppManager API envelope: every response carries <c>success</c>, an optional
/// <c>data</c> payload, and on failure an <c>error</c> code with a human-readable message.
/// </summary>
/// <typeparam name="T">The type of the <c>data</c> payload.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>Gets or sets a value indicating whether the call succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the response payload (null on failure).</summary>
    public T? Data { get; set; }

    /// <summary>Gets or sets the human-readable message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the wire error code (e.g. <c>INVALID_CREDENTIALS</c>) on failure.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the HTTP status code echoed in error envelopes.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Gets or sets the server trace identifier for support correlation.</summary>
    public string? TraceId { get; set; }
}
