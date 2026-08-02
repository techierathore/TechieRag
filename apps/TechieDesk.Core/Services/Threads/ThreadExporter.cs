using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TechieRag.Models;

namespace TechieDesk.Services.Threads;

/// <summary>
/// Serializes a conversation thread's messages — role, content, cited sources, and
/// timestamps — to portable Markdown and JSON documents for download (REQ-FN-010 / BRD-35).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Produces a self-contained, human-readable Markdown transcript and a
/// machine-readable JSON export so a user can archive or move a thread's full history off the
/// application. Pure and stateless — operates only on the supplied thread and messages, so it is
/// trivially unit-testable and carries no LLM or store dependency.</para>
/// <para>
/// <b>REQ-UI-055 / BRD-91 — the export is deliberately NOT localized.</b> Every heading, field label
/// and placeholder written below stays invariant English in every culture, and that is a decision
/// rather than an oversight.
/// </para>
/// <list type="bullet">
/// <item><b>What this produces is a FILE, not a screen.</b> It leaves the app the moment it is
/// written and it outlives the setting that made it. A transcript exported by a Hindi install and
/// mailed to a colleague, attached to a support issue, or opened three years later by a different
/// person on a different machine has to be readable by whoever holds it — and the one thing the
/// author knows about that reader is that they are not necessarily the author.</item>
/// <item><b>The JSON half is a data contract.</b> <c>ToJson</c> emits camelCase property names that
/// anything downstream parses by name; the Markdown half is the same document for a human. Splitting
/// them — a translated <c>.md</c> beside an English <c>.json</c> — would mean the same export
/// disagreed with itself about what a field is called.</item>
/// <item><b>The user's own words are already in their language.</b> Nothing here translates or
/// reorders content: the titles, questions, answers and cited snippets are carried through verbatim,
/// so a Hindi thread exports as a Hindi thread. Only the six-word scaffolding around it is English,
/// and that scaffolding is what makes the file portable.</item>
/// <item><b>REQ-FN-010 recorded thread export as a silent-false-success defect once already.</b> The
/// write path is not touched by this requirement, and the strings below deliberately stay
/// byte-identical so an export produced after REQ-UI-055 is diffable against one produced before it.
/// </item>
/// </list>
/// <para>
/// The counterpart is that the export BUTTON, its label, its toast and its error messages are
/// ordinary UI and are localized where they live, in the razor tree.
/// </para>
/// </remarks>
public sealed class ThreadExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Serializes a thread and its messages to a Markdown transcript.
    /// </summary>
    /// <param name="thread">The thread being exported.</param>
    /// <param name="messages">The thread's messages in chronological order.</param>
    /// <returns>A Markdown document with a title, metadata, and one section per message.</returns>
    public string ToMarkdown(ConversationThread thread, IReadOnlyList<StoredChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(messages);

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(thread.Title);
        builder.AppendLine();
        builder.Append("- **Thread ID:** ").AppendLine(thread.ThreadId);
        if (!string.IsNullOrEmpty(thread.WorkspaceId))
        {
            builder.Append("- **Workspace:** ").AppendLine(thread.WorkspaceId);
        }
        builder.Append("- **Created:** ").AppendLine(thread.CreatedAt.ToString("u"));
        builder.Append("- **Updated:** ").AppendLine(thread.UpdatedAt.ToString("u"));
        builder.Append("- **Messages:** ").AppendLine(messages.Count.ToString());
        builder.AppendLine();

        foreach (var message in messages)
        {
            var role = string.IsNullOrEmpty(message.Role) ? "message" : message.Role;
            var heading = char.ToUpperInvariant(role[0]) + role[1..];
            builder.Append("## ").Append(heading).Append(" — ").AppendLine(message.CreatedAt.ToString("u"));
            builder.AppendLine();
            builder.AppendLine(string.IsNullOrEmpty(message.Content) ? "_(no content)_" : message.Content);
            builder.AppendLine();

            if (message.Sources is { Count: > 0 })
            {
                builder.AppendLine("**Sources:**");
                builder.AppendLine();
                foreach (var source in message.Sources)
                {
                    var name = ResolveSourceName(source);
                    var snippet = Snippet(source.Chunk.Text);
                    builder
                        .Append("- ")
                        .Append(name)
                        .Append(" (relevance ")
                        .Append(source.Score.ToString("0.00"))
                        .Append("): ")
                        .AppendLine(snippet);
                }
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Serializes a thread and its messages to an indented JSON document.
    /// </summary>
    /// <param name="thread">The thread being exported.</param>
    /// <param name="messages">The thread's messages in chronological order.</param>
    /// <returns>A JSON string capturing the thread metadata and every message with its sources.</returns>
    public string ToJson(ConversationThread thread, IReadOnlyList<StoredChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(messages);

        var export = new ThreadExport
        {
            ThreadId = thread.ThreadId,
            Title = thread.Title,
            WorkspaceId = thread.WorkspaceId,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            Messages = messages.Select(m => new MessageExport
            {
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                Sources = m.Sources is null
                    ? null
                    : m.Sources.Select(s => new SourceExport
                    {
                        DocumentName = ResolveSourceName(s),
                        Snippet = Snippet(s.Chunk.Text),
                        Score = s.Score
                    }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    /// <summary>
    /// Builds a safe, timestamped download file name for a thread export.
    /// </summary>
    /// <param name="thread">The thread being exported.</param>
    /// <param name="extension">The file extension without a leading dot (e.g. "md" or "json").</param>
    /// <returns>A sanitized file name such as <c>my-thread-20260718.md</c>.</returns>
    public string BuildFileName(ConversationThread thread, string extension)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var title = string.IsNullOrWhiteSpace(thread.Title) ? "thread" : thread.Title;
        var slug = new string(title.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }
        slug = slug.Trim('-');
        if (slug.Length == 0)
        {
            slug = "thread";
        }
        if (slug.Length > 48)
        {
            slug = slug[..48].Trim('-');
        }

        return $"{slug}-{thread.UpdatedAt:yyyyMMdd}.{extension}";
    }

    private static string ResolveSourceName(SearchResult source)
    {
        if (source.Chunk.Metadata.TryGetValue("DocumentName", out var value)
            && value is string name && !string.IsNullOrEmpty(name))
        {
            return name;
        }
        return source.Chunk.DocumentId;
    }

    private static string Snippet(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length > 220 ? normalized[..220] + "…" : normalized;
    }

    /// <summary>Serializable projection of a thread for JSON export.</summary>
    private sealed class ThreadExport
    {
        /// <summary>Gets the thread identifier.</summary>
        public required string ThreadId { get; init; }

        /// <summary>Gets the thread title.</summary>
        public required string Title { get; init; }

        /// <summary>Gets the owning workspace identifier, or null for the global scope.</summary>
        public string? WorkspaceId { get; init; }

        /// <summary>Gets when the thread was created (UTC).</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets when the thread was last updated (UTC).</summary>
        public DateTime UpdatedAt { get; init; }

        /// <summary>Gets the exported messages in chronological order.</summary>
        public required IReadOnlyList<MessageExport> Messages { get; init; }
    }

    /// <summary>Serializable projection of a single message for JSON export.</summary>
    private sealed class MessageExport
    {
        /// <summary>Gets the message role.</summary>
        public required string Role { get; init; }

        /// <summary>Gets the message text content.</summary>
        public string? Content { get; init; }

        /// <summary>Gets when the message was created (UTC).</summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>Gets the cited sources, or null when none.</summary>
        public IReadOnlyList<SourceExport>? Sources { get; init; }
    }

    /// <summary>Serializable projection of a cited source for JSON export.</summary>
    private sealed class SourceExport
    {
        /// <summary>Gets the source document name.</summary>
        public required string DocumentName { get; init; }

        /// <summary>Gets a trimmed snippet of the cited chunk text.</summary>
        public required string Snippet { get; init; }

        /// <summary>Gets the retrieval relevance score.</summary>
        public float Score { get; init; }
    }
}
