using System.Text.Json;

namespace TechieRag.Mcp;

/// <summary>
/// Minimal JSON-RPC 2.0 helpers shared by the MCP transports.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled over <c>System.Text.Json</c>: MCP needs three message shapes and no
/// more, and the library's standing rule is to keep the core dependency-light rather than take a
/// JSON-RPC package for ninety lines of code.
/// </remarks>
internal static class JsonRpc
{
    internal const string Version = "2.0";

    /// <summary>Serialises a request envelope.</summary>
    /// <param name="id">The correlation id.</param>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="parameters">The params object, or null to omit.</param>
    /// <returns>The serialised JSON text, with no trailing newline.</returns>
    internal static string SerializeRequest(long id, string method, object? parameters)
    {
        var envelope = new Dictionary<string, object>
        {
            ["jsonrpc"] = Version,
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null) envelope["params"] = parameters;
        return JsonSerializer.Serialize(envelope);
    }

    /// <summary>Serialises a notification envelope (no id, no response expected).</summary>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="parameters">The params object, or null to omit.</param>
    /// <returns>The serialised JSON text, with no trailing newline.</returns>
    internal static string SerializeNotification(string method, object? parameters)
    {
        var envelope = new Dictionary<string, object>
        {
            ["jsonrpc"] = Version,
            ["method"] = method
        };
        if (parameters is not null) envelope["params"] = parameters;
        return JsonSerializer.Serialize(envelope);
    }

    /// <summary>
    /// Determines whether a parsed message is the response to the given request id.
    /// </summary>
    /// <param name="message">A parsed JSON-RPC message.</param>
    /// <param name="id">The request id being awaited.</param>
    /// <returns><see langword="true"/> when the message carries a matching id.</returns>
    /// <remarks>
    /// Servers legitimately interleave notifications and their own requests (sampling, logging) with
    /// responses, so a reader must skip anything that is not the awaited id rather than assume the
    /// next message is the answer.
    /// </remarks>
    internal static bool IsResponseTo(JsonElement message, long id)
    {
        if (message.ValueKind != JsonValueKind.Object) return false;
        if (!message.TryGetProperty("id", out var idElement)) return false;
        return idElement.ValueKind == JsonValueKind.Number
            && idElement.TryGetInt64(out var value)
            && value == id;
    }

    /// <summary>
    /// Extracts the <c>result</c> from a JSON-RPC response, converting an <c>error</c> member into
    /// an exception.
    /// </summary>
    /// <param name="serverName">The MCP server name, for attribution.</param>
    /// <param name="response">The parsed response message.</param>
    /// <returns>The <c>result</c> member, or an empty object when the server omitted it.</returns>
    /// <exception cref="McpException">The response carried a JSON-RPC error.</exception>
    internal static JsonElement ReadResult(string serverName, JsonElement response)
    {
        if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsed)
                ? parsed
                : (int?)null;
            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "no message"
                : "no message";

            throw new McpException(serverName, $"MCP server '{serverName}' returned error {code}: {message}", code);
        }

        if (response.TryGetProperty("result", out var result)) return result.Clone();

        return EmptyObject();
    }

    /// <summary>Produces a detached empty JSON object element.</summary>
    /// <returns>An empty JSON object.</returns>
    /// <remarks>Cloned once from a disposed document, so repeated calls neither allocate nor leak
    /// the pooled buffers a live <see cref="JsonDocument"/> holds.</remarks>
    internal static JsonElement EmptyObject() => Empty;

    private static readonly JsonElement Empty = ParseDetached("{}");

    /// <summary>Parses JSON into an element that outlives the document it came from.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>A detached element.</returns>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    internal static JsonElement ParseDetached(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
