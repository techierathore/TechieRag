using System.Text;
using System.Text.Json;

namespace TechieRag.Llm;

/// <summary>
/// Reads claims from the payload of a JSON Web Token without validating it (REQ-RAG-069).
/// </summary>
/// <remarks>
/// The tokens come straight from the vendor's token endpoint over TLS and are only sent back to the
/// same vendor, so the library reads them for routing data (account id, expiry) and never trusts them
/// for an authorisation decision of its own. That is why no signature is checked.
/// </remarks>
internal static class JwtClaimReader
{
    /// <summary>The claim OpenAI nests its account data under.</summary>
    internal const string OpenAIAuthClaim = "https://api.openai.com/auth";

    /// <summary>Reads the ChatGPT account id from an identity or access token.</summary>
    /// <param name="token">The token, or null.</param>
    /// <returns>The account id, or null when the token carries none.</returns>
    public static string? ReadChatGptAccountId(string? token)
    {
        if (ReadPayload(token) is not { } payload) return null;

        if (payload.TryGetProperty(OpenAIAuthClaim, out var auth)
            && auth.ValueKind == JsonValueKind.Object
            && auth.TryGetProperty("chatgpt_account_id", out var nested)
            && nested.ValueKind == JsonValueKind.String)
        {
            return nested.GetString();
        }

        return payload.TryGetProperty("chatgpt_account_id", out var flat) && flat.ValueKind == JsonValueKind.String
            ? flat.GetString()
            : null;
    }

    /// <summary>Reads the <c>exp</c> claim.</summary>
    /// <param name="token">The token, or null.</param>
    /// <returns>The expiry, or null when the token is not a JWT or has no expiry.</returns>
    public static DateTimeOffset? ReadExpiry(string? token)
    {
        if (ReadPayload(token) is not { } payload) return null;

        return payload.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static JsonElement? ReadPayload(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var json = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        return Convert.FromBase64String(base64 + new string('=', padding));
    }
}
