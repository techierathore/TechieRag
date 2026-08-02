using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// A REAL MCP server on a real loopback socket, used to prove REQ-RAG-023 end to end.
/// </summary>
/// <remarks>
/// <para><b>Why a socket and not a stubbed <c>HttpMessageHandler</c>.</b> The library's own MCP tests
/// stub the handler, which is right for testing the transport's parsing. It is not enough here:
/// "registered MCP servers expose tools to the agent" is a claim about a whole path — a row in
/// SQLite, a trust policy, a transport, a handshake, a tool list, a tool call, and an agent loop —
/// and a stubbed handler is exactly the place that path could be broken while every assertion still
/// passed. This listens on 127.0.0.1, so the bytes are real bytes.</para>
/// <para><b>It is also the egress meter.</b> <see cref="BytesReceived"/> and
/// <see cref="ConnectionCount"/> are incremented at the socket, before anything is parsed, which is
/// what lets the zero-egress guard for REQ-NFR-008 be a MEASUREMENT rather than a reading of a flag.
/// A test that asserted "the config says nothing is enabled" would pass against code that dialled
/// out anyway; this one cannot.</para>
/// <para><b>Deliberately minimal HTTP.</b> One request per connection, <c>Connection: close</c>, no
/// keep-alive and no chunked encoding — enough for what <c>HttpMcpTransport</c> actually sends
/// (a POST with a JSON body and a Content-Length) and nothing more.</para>
/// </remarks>
public sealed class LoopbackMcpServer : IAsyncDisposable
{
    /// <summary>The tool this fixture advertises.</summary>
    public const string ToolName = "echo";

    private readonly TcpListener listener;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task acceptLoop;
    private readonly ConcurrentQueue<string> toolCallArguments = new();
    private readonly string? requiredAuthorization;
    private long bytesReceived;
    private int connectionCount;
    private int authorizedCallCount;

    /// <summary>Starts a server on an ephemeral loopback port.</summary>
    /// <param name="requiredAuthorization">
    /// When set, a <c>tools/call</c> is only answered if the request carried exactly this
    /// <c>Authorization</c> header — which is how the credential path is proven to reach the wire
    /// rather than merely to be stored.
    /// </param>
    public LoopbackMcpServer(string? requiredAuthorization = null)
    {
        this.requiredAuthorization = requiredAuthorization;

        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/mcp");
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Gets the absolute endpoint URL to register.</summary>
    public Uri Endpoint { get; }

    /// <summary>Gets every byte this server has received on the wire, headers included.</summary>
    public long BytesReceived => Interlocked.Read(ref bytesReceived);

    /// <summary>Gets how many TCP connections have been accepted.</summary>
    public int ConnectionCount => Volatile.Read(ref connectionCount);

    /// <summary>Gets how many tool calls arrived carrying the required Authorization header.</summary>
    public int AuthorizedCallCount => Volatile.Read(ref authorizedCallCount);

    /// <summary>Gets the arguments JSON of every tool call, in arrival order.</summary>
    public IReadOnlyList<string> ToolCallArguments => toolCallArguments.ToList();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync();
        listener.Stop();

        try
        {
            await acceptLoop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Shutting a listener down races its own accept; none of these mean a test failed.
        }

        shutdown.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(shutdown.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            Interlocked.Increment(ref connectionCount);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var (headers, body) = await ReadRequestAsync(stream);
                var response = BuildResponse(headers, body);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                await stream.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // A client that hangs up mid-request is not a server fault.
            }
        }
    }

    /// <summary>Reads one HTTP request, counting every byte that arrives.</summary>
    /// <param name="stream">The accepted connection.</param>
    /// <returns>The raw header block and the decoded body.</returns>
    private async Task<(string Headers, string Body)> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var received = new List<byte>();
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, shutdown.Token);
            if (read == 0) break;

            Interlocked.Add(ref bytesReceived, read);
            received.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = IndexOfHeaderEnd(received);
        }

        if (headerEnd < 0) return (string.Empty, string.Empty);

        var headerText = Encoding.UTF8.GetString(received.ToArray(), 0, headerEnd);
        var contentLength = ReadContentLength(headerText);
        var bodyStart = headerEnd + 4;

        while (received.Count - bodyStart < contentLength)
        {
            var read = await stream.ReadAsync(buffer, shutdown.Token);
            if (read == 0) break;

            Interlocked.Add(ref bytesReceived, read);
            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        var bodyLength = Math.Min(contentLength, Math.Max(0, received.Count - bodyStart));
        var body = Encoding.UTF8.GetString(received.ToArray(), bodyStart, bodyLength);
        return (headerText, body);
    }

    /// <summary>Answers one JSON-RPC message the way an MCP server would.</summary>
    /// <param name="headers">The raw request headers.</param>
    /// <param name="body">The JSON-RPC request body.</param>
    /// <returns>A complete HTTP response.</returns>
    private string BuildResponse(string headers, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Http("{}");

        JsonElement request;
        try
        {
            using var document = JsonDocument.Parse(body);
            request = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Http("""{"jsonrpc":"2.0","id":0,"error":{"code":-32700,"message":"parse error"}}""");
        }

        var method = request.TryGetProperty("method", out var methodElement)
            ? methodElement.GetString()
            : null;

        // A notification carries no id and expects no JSON-RPC reply, only a successful status.
        if (!request.TryGetProperty("id", out var idElement)) return Http("{}");

        var id = idElement.GetRawText();

        return method switch
        {
            "initialize" => Http(Envelope(id,
                "\"result\":{\"protocolVersion\":\"2025-06-18\",\"serverInfo\":{\"name\":\"loopback-mcp\"}}")),
            "tools/list" => Http(Envelope(id,
                "\"result\":{\"tools\":[{\"name\":\"" + ToolName + "\","
                + "\"description\":\"Repeats the text it is given.\","
                + "\"inputSchema\":{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},"
                + "\"required\":[\"text\"]}}]}")),
            "tools/call" => CallTool(headers, request, id),
            _ => Http(Envelope(id, "\"error\":{\"code\":-32601,\"message\":\"method not found\"}"))
        };
    }

    /// <summary>Wraps a result or error member in a JSON-RPC response envelope.</summary>
    /// <param name="id">The raw JSON text of the request's id.</param>
    /// <param name="member">The <c>result</c> or <c>error</c> member, already serialised.</param>
    /// <returns>The complete JSON-RPC message.</returns>
    private static string Envelope(string id, string member) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + "," + member + "}";

    private string CallTool(string headers, JsonElement request, string id)
    {
        var arguments = request.TryGetProperty("params", out var parameters)
            && parameters.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetRawText()
                : "{}";

        toolCallArguments.Enqueue(arguments);

        if (requiredAuthorization is not null)
        {
            var supplied = ReadHeader(headers, "Authorization");
            if (!string.Equals(supplied, requiredAuthorization, StringComparison.Ordinal))
            {
                return Http(Envelope(id,
                    "\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"unauthorized\"}],\"isError\":true}"));
            }

            Interlocked.Increment(ref authorizedCallCount);
        }

        var text = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("arguments", out var suppliedArguments)
            && suppliedArguments.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

        var payload = JsonSerializer.Serialize($"loopback echoed: {text}");
        return Http(Envelope(id,
            "\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + payload + "}],\"isError\":false}"));
    }

    private static string Http(string json)
    {
        var body = Encoding.UTF8.GetByteCount(json);
        return "HTTP/1.1 200 OK\r\n"
            + "Content-Type: application/json\r\n"
            + $"Content-Length: {body}\r\n"
            + "Connection: close\r\n"
            + "\r\n"
            + json;
    }

    private static int IndexOfHeaderEnd(List<byte> received)
    {
        for (var index = 0; index + 3 < received.Count; index++)
        {
            if (received[index] == (byte)'\r' && received[index + 1] == (byte)'\n'
                && received[index + 2] == (byte)'\r' && received[index + 3] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static int ReadContentLength(string headers) =>
        int.TryParse(ReadHeader(headers, "Content-Length"), out var length) ? length : 0;

    private static string? ReadHeader(string headers, string name)
    {
        foreach (var line in headers.Split("\r\n"))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            if (line.AsSpan(0, separator).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }
}
