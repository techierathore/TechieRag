using System.Text;

namespace TechieDesk.Services.Agents;

/// <summary>
/// A parsed <c>@handle</c> invocation taken off the front of a composer message (REQ-RAG-021).
/// </summary>
/// <param name="Handle">The normalized handle, lowercase and without its leading '@'.</param>
/// <param name="Message">The rest of the message, with the mention removed.</param>
public sealed record AgentMention(string Handle, string Message);

/// <summary>
/// Recognises <c>@handle</c> at the start of a chat message so the turn can be routed through that
/// named agent (BRD-83 / REQ-RAG-021).
/// </summary>
/// <remarks>
/// <para><b>Deliberately strict, and only at the start.</b> A mention is recognised only as the
/// first non-whitespace token, and only when it is terminated by whitespace or end-of-text. That
/// keeps an email address ("write to sales@acme.com") and an inline aside ("the @ sign") from
/// silently rerouting a turn through an agent the user never asked for — a wrong route is worse
/// than a missed one, because the user cannot see that it happened.</para>
/// <para><b>Handles are matched case-insensitively</b> and stored lowercase, so <c>@Analyst</c> and
/// <c>@analyst</c> are the same agent rather than two rows that collide on save.</para>
/// </remarks>
public static class AgentMentionParser
{
    /// <summary>The longest handle accepted, matching the storage column's practical use.</summary>
    public const int MaxHandleLength = 32;

    /// <summary>
    /// Parses a leading <c>@handle</c> off a message.
    /// </summary>
    /// <param name="text">The raw composer text.</param>
    /// <returns>The mention and the remaining message, or null when the text has no leading mention.</returns>
    public static AgentMention? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length < 2 || trimmed[0] != '@')
        {
            return null;
        }

        var end = 1;
        while (end < trimmed.Length && IsHandleCharacter(trimmed[end]))
        {
            end++;
        }

        var handle = trimmed[1..end];
        if (!IsValidHandle(handle))
        {
            return null;
        }

        // The token must END here: "@analyst" and "@analyst compare X" are mentions,
        // "sales@acme.com" never reaches this point, and "@analyst.com" is not one either.
        if (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            return null;
        }

        return new AgentMention(Normalize(handle), trimmed[end..].Trim());
    }

    /// <summary>
    /// Normalizes a handle typed anywhere in the UI to its stored form: no leading '@', trimmed,
    /// lowercase.
    /// </summary>
    /// <param name="handle">The handle as typed, with or without '@'.</param>
    /// <returns>The stored form, or an empty string when nothing usable remains.</returns>
    public static string Normalize(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return string.Empty;
        }

        var value = handle.Trim().TrimStart('@').Trim();
        return value.ToLowerInvariant();
    }

    /// <summary>Gets whether a normalized handle is usable as a chat mention.</summary>
    /// <param name="handle">The handle, with or without '@'.</param>
    /// <returns>True when the handle is non-empty, within length, and all handle characters.</returns>
    public static bool IsValidHandle(string? handle)
    {
        var value = Normalize(handle);
        return value.Length > 0
            && value.Length <= MaxHandleLength
            && value.All(IsHandleCharacter);
    }

    /// <summary>
    /// Derives a usable handle from a display name, so creating "Contract Analyst" suggests
    /// <c>@contract-analyst</c> instead of leaving the field empty.
    /// </summary>
    /// <param name="displayName">The agent's display name.</param>
    /// <returns>A normalized handle, or an empty string when the name has no usable characters.</returns>
    public static string SuggestHandle(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length == MaxHandleLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool IsHandleCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '-';
}
