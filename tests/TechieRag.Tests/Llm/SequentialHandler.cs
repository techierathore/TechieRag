using System.Net;
using System.Text;

namespace TechieRag.Tests.Llm;

/// <summary>
/// A stub <see cref="HttpMessageHandler"/> that replies with a different canned body per request, in
/// order, and records each request body — for a multi-call conversation such as an agent loop.
/// </summary>
internal sealed class SequentialHandler : HttpMessageHandler
{
    private readonly Queue<string> bodies;

    /// <summary>Creates the handler.</summary>
    /// <param name="bodies">One response body per request, in order.</param>
    public SequentialHandler(params string[] bodies) => this.bodies = new Queue<string>(bodies);

    /// <summary>Gets each request body, in order.</summary>
    public List<string> RequestBodies { get; } = new();

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodies.Dequeue(), Encoding.UTF8, "text/event-stream")
        };
    }
}
