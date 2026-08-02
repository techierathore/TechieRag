using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Mcp;

/// <summary>
/// Reaches a remote MCP server over the streamable-HTTP transport: each JSON-RPC message is an
/// HTTP POST, and the reply is either a JSON body or a Server-Sent Events stream (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The default-safe way to use MCP — no process is created, so a bad
/// configuration costs a failed HTTP call rather than arbitrary code execution.</para>
/// <para><b>Trust:</b> The endpoint must have passed
/// <see cref="McpServerConfig.Validate(McpTrustPolicy)"/>, which refuses plaintext HTTP to anything
/// but loopback unless the host opted in — otherwise the bearer token in
/// <see cref="McpServerConfig.Headers"/> would travel in the clear. Header <i>values</i> are set on
/// the request and never logged; <see cref="McpServerConfig.Describe"/> reports only their names.</para>
/// <para><b>Sessions:</b> If the server issues an <c>Mcp-Session-Id</c> on initialize, it is echoed
/// on every later request, as the transport specification requires.</para>
/// <para><b>Dependencies:</b> raw <see cref="HttpClient"/> and <c>System.Text.Json</c> only.</para>
/// </remarks>
public sealed class HttpMcpTransport : IMcpTransport
{
    private const string SessionHeaderName = "Mcp-Session-Id";

    private readonly HttpClient httpClient;
    private readonly McpServerConfig config;
    private readonly ILogger<HttpMcpTransport> logger;
    private readonly bool ownsHttpClient;
    private readonly string requestPath;
    private string? sessionId;
    private long nextId;

    /// <inheritdoc/>
    public string ServerName => config.Name;

    /// <summary>
    /// Creates an HTTP transport for a validated server configuration.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    /// <param name="policy">The host's trust policy; the configuration is validated against it here.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="McpConfigurationException">Thrown when the configuration is not permitted by the policy.</exception>
    public HttpMcpTransport(McpServerConfig config, McpTrustPolicy policy, ILogger<HttpMcpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(policy);

        config.Validate(policy);

        this.config = config;
        this.logger = logger ?? NullLogger<HttpMcpTransport>.Instance;

        var uri = new Uri(config.Endpoint!, UriKind.Absolute);
        requestPath = uri.PathAndQuery;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(uri.GetLeftPart(UriPartial.Authority)),
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };
        ownsHttpClient = true;

        foreach (var header in config.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>
    /// Creates an HTTP transport over a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured client; its <c>BaseAddress</c> must be set.</param>
    /// <param name="config">The server configuration (used for its name, timeout and tool allow-list).</param>
    /// <param name="requestPath">The path to POST JSON-RPC messages to; defaults to the client's base path.</param>
    /// <param name="logger">Logger instance.</param>
    /// <remarks>
    /// Test seam and integration seam: lets a stubbed <see cref="HttpMessageHandler"/> intercept
    /// requests, and lets a host supply a pooled or instrumented client. The configuration is not
    /// re-validated here because no endpoint of ours is being chosen — the caller already owns it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public HttpMcpTransport(HttpClient httpClient, McpServerConfig config, string? requestPath = null, ILogger<HttpMcpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);

        this.httpClient = httpClient;
        this.config = config;
        this.requestPath = requestPath ?? "/";
        this.logger = logger ?? NullLogger<HttpMcpTransport>.Instance;
        ownsHttpClient = false;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var id = Interlocked.Increment(ref nextId);
        var payload = JsonRpc.SerializeRequest(id, method, parameters);

        using var response = await PostAsync(payload, cancellationToken).ConfigureAwait(false);
        CaptureSessionId(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = ExtractMessage(body, id)
            ?? throw new McpException(ServerName, $"MCP server '{ServerName}' returned no JSON-RPC response for '{method}'.");

        return JsonRpc.ReadResult(ServerName, message);
    }

    /// <inheritdoc/>
    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var payload = JsonRpc.SerializeNotification(method, parameters);
        using var response = await PostAsync(payload, cancellationToken).ConfigureAwait(false);
        CaptureSessionId(response);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (ownsHttpClient) httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<HttpResponseMessage> PostAsync(string payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation(SessionHeaderName, sessionId);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException(ServerName, $"MCP server '{ServerName}' is unreachable: {ex.Message}", null, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();

            // The status line only. A failing MCP endpoint often echoes the request — including its
            // Authorization header — in the body, and that must not reach a log.
            throw new McpException(ServerName, $"MCP server '{ServerName}' returned HTTP {status}.");
        }

        return response;
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(SessionHeaderName, out var values))
        {
            var issued = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(issued) && issued != sessionId)
            {
                sessionId = issued;
                logger.LogDebug("MCP server {ServerName} issued a session id", ServerName);
            }
        }
    }

    /// <summary>
    /// Pulls the JSON-RPC message matching <paramref name="id"/> out of a response body that may be
    /// a bare JSON object, a JSON array of messages, or an SSE stream.
    /// </summary>
    /// <param name="body">The raw response body.</param>
    /// <param name="id">The awaited request id.</param>
    /// <returns>The matching message, or null when the body held none.</returns>
    private static JsonElement? ExtractMessage(string body, long id)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        foreach (var candidate in EnumerateJsonPayloads(body))
        {
            JsonElement parsed;
            try
            {
                parsed = JsonRpc.ParseDetached(candidate);
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in parsed.EnumerateArray())
                {
                    if (JsonRpc.IsResponseTo(item, id)) return item.Clone();
                }
                continue;
            }

            if (JsonRpc.IsResponseTo(parsed, id)) return parsed;
        }

        return null;
    }

    /// <summary>
    /// Yields each JSON payload in a body: the whole body for a plain JSON response, or the
    /// <c>data:</c> field of each SSE event.
    /// </summary>
    /// <param name="body">The raw response body.</param>
    /// <returns>Candidate JSON texts in the order they appear.</returns>
    private static IEnumerable<string> EnumerateJsonPayloads(string body)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            yield return trimmed;
            yield break;
        }

        foreach (var line in body.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.StartsWith("data:", StringComparison.Ordinal))
            {
                yield return text[5..].Trim();
            }
        }
    }
}
