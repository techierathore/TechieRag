using System.Net;
using System.Text;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Records every outgoing request (method, path, body, headers) and answers from a
/// test-supplied responder, so the full AppManager wire contract can be exercised without a
/// live service.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> responder;

    /// <summary>Initializes the handler with a responder callback.</summary>
    /// <param name="responder">Maps a request (and its body text) to a response.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        this.responder = responder;
    }

    /// <summary>Gets every call the handler has seen, in order.</summary>
    public List<RecordedCall> Calls { get; } = new();

    /// <summary>Builds a JSON response with the given status.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="json">The JSON body.</param>
    /// <returns>The response message.</returns>
    public static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value));
        Calls.Add(new RecordedCall(request.Method, request.RequestUri!.PathAndQuery, body, headers));
        return responder(request, body);
    }
}

/// <summary>
/// A single recorded HTTP call.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="PathAndQuery">The request path and query string.</param>
/// <param name="Body">The request body text, or null.</param>
/// <param name="Headers">The request headers (comma-joined values).</param>
public sealed record RecordedCall(
    HttpMethod Method,
    string PathAndQuery,
    string? Body,
    IReadOnlyDictionary<string, string> Headers);
