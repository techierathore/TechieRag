using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>
/// A loopback HTTP server that serves byte arrays by path, honours <c>Range: bytes=N-</c> with a 206,
/// and records every request, so downloads run for real without leaving the machine.
/// </summary>
internal sealed class LocalFileServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, byte[]> files = new(StringComparer.Ordinal);
    private readonly Task loop;

    /// <summary>Starts the server on a free loopback port.</summary>
    public LocalFileServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        BaseUrl = $"http://localhost:{port}/";
        listener.Prefixes.Add(BaseUrl);
        listener.Start();
        loop = Task.Run(ServeAsync);
    }

    /// <summary>Gets the server's base URL, ending in a slash.</summary>
    public string BaseUrl { get; }

    /// <summary>Gets the requests received: path and Range header, in order.</summary>
    public ConcurrentQueue<(string Path, string? Range)> Requests { get; } = new();

    /// <summary>Gets the errors the server hit while responding, for a failing test's message.</summary>
    public ConcurrentQueue<string> Errors { get; } = new();

    /// <summary>Serves a file at a path.</summary>
    /// <param name="path">The path, without a leading slash.</param>
    /// <param name="content">The bytes.</param>
    public void Add(string path, byte[] content) => files[path] = content;

    /// <inheritdoc/>
    public void Dispose()
    {
        listener.Stop();
        listener.Close();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener was stopped while waiting for a request.
        }
    }

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                Respond(context);
            }
            catch (Exception exception) when (exception is HttpListenerException or IOException or InvalidOperationException or ArgumentException)
            {
                Errors.Enqueue(exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath.TrimStart('/');
        var range = context.Request.Headers["Range"];
        Requests.Enqueue((path, range));
        using var response = context.Response;

        // No keep-alive: the shared download service's pooled connection must never outlive a server.
        response.KeepAlive = false;
        if (!files.TryGetValue(path, out var content))
        {
            response.StatusCode = 404;
            return;
        }

        var from = range is not null && range.StartsWith("bytes=", StringComparison.Ordinal)
            ? long.Parse(range[6..].TrimEnd('-'), System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        if (from > 0)
        {
            response.StatusCode = 206;
            response.AddHeader("Content-Range", $"bytes {from}-{content.Length - 1}/{content.Length}");
        }

        response.ContentLength64 = content.Length - from;
        response.OutputStream.Write(content, (int)from, (int)(content.Length - from));
    }
}
