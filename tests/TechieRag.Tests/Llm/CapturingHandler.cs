using System.Net;
using System.Text;

namespace TechieRag.Tests.Llm;

/// <summary>
/// A stub <see cref="HttpMessageHandler"/> that captures the outgoing request body and replies with
/// a canned response.
/// </summary>
/// <remarks>
/// There is no live LLM provider on the build host, so every provider assertion in this folder is a
/// wire-format assertion: the request TechieRag would have sent is inspected, and a recorded response
/// shape is fed back. That proves the serialization and the parsing, which is all the library owns —
/// it cannot prove any model actually reads the image or honours the cache breakpoint.
/// </remarks>
internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly string responseJson;

    /// <summary>Gets the body of the last request that passed through.</summary>
    public string? CapturedBody { get; private set; }

    /// <summary>Creates a handler that always replies with the supplied JSON.</summary>
    /// <param name="responseJson">Canned response body.</param>
    public CapturingHandler(string responseJson) => this.responseJson = responseJson;

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };
    }
}
