using System.Text.Json;
using TechieDesk.Services.Threads;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Threads;

/// <summary>
/// REQ-FN-010 (BRD-35): the thread export serializers round-trip a thread's messages —
/// role, content, cited sources, and timestamps — into valid Markdown and JSON documents.
/// </summary>
public sealed class ThreadExporterTests
{
    private readonly ThreadExporter exporter = new();

    /// <summary>
    /// Builds a representative thread with a user question and a cited assistant answer.
    /// </summary>
    private static (ConversationThread Thread, List<StoredChatMessage> Messages) BuildSample()
    {
        var thread = new ConversationThread
        {
            ThreadId = "thread-1",
            UserId = "user-1",
            WorkspaceId = "ws-1",
            Title = "Onboarding questions",
            CreatedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc)
        };

        var source = new SearchResult
        {
            Score = 0.92f,
            Chunk = new TextChunk
            {
                DocumentId = "doc-42",
                Text = "The onboarding guide covers account setup and first-run configuration.",
                Metadata = new Dictionary<string, object> { ["DocumentName"] = "Onboarding.pdf" }
            }
        };

        var messages = new List<StoredChatMessage>
        {
            new()
            {
                ThreadId = thread.ThreadId,
                Role = "user",
                Content = "How do I onboard a new user?",
                CreatedAt = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                ThreadId = thread.ThreadId,
                Role = "assistant",
                Content = "Follow the onboarding guide to set up the account.",
                Sources = new List<SearchResult> { source },
                CreatedAt = new DateTime(2026, 7, 2, 10, 1, 0, DateTimeKind.Utc)
            }
        };

        return (thread, messages);
    }

    /// <summary>
    /// The Markdown export includes the title, roles, message content, and cited source names.
    /// </summary>
    [Fact]
    public void MarkdownIncludesTitleRolesContentAndSources()
    {
        var (thread, messages) = BuildSample();

        var markdown = exporter.ToMarkdown(thread, messages);

        Assert.Contains("# Onboarding questions", markdown);
        Assert.Contains("## User", markdown);
        Assert.Contains("## Assistant", markdown);
        Assert.Contains("How do I onboard a new user?", markdown);
        Assert.Contains("Follow the onboarding guide", markdown);
        Assert.Contains("**Sources:**", markdown);
        Assert.Contains("Onboarding.pdf", markdown);
        Assert.Contains("0.92", markdown);
    }

    /// <summary>
    /// The JSON export is valid JSON that preserves message roles, content, timestamps and sources.
    /// </summary>
    [Fact]
    public void JsonIsValidAndPreservesMessagesAndSources()
    {
        var (thread, messages) = BuildSample();

        var json = exporter.ToJson(thread, messages);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("thread-1", root.GetProperty("threadId").GetString());
        Assert.Equal("Onboarding questions", root.GetProperty("title").GetString());
        Assert.Equal("ws-1", root.GetProperty("workspaceId").GetString());

        var jsonMessages = root.GetProperty("messages");
        Assert.Equal(2, jsonMessages.GetArrayLength());

        var first = jsonMessages[0];
        Assert.Equal("user", first.GetProperty("role").GetString());
        Assert.Equal("How do I onboard a new user?", first.GetProperty("content").GetString());

        var second = jsonMessages[1];
        Assert.Equal("assistant", second.GetProperty("role").GetString());
        var sources = second.GetProperty("sources");
        Assert.Equal(1, sources.GetArrayLength());
        Assert.Equal("Onboarding.pdf", sources[0].GetProperty("documentName").GetString());
        Assert.Equal(0.92f, sources[0].GetProperty("score").GetSingle(), 3);
    }

    /// <summary>
    /// Empty threads still serialize to valid, non-throwing Markdown and JSON.
    /// </summary>
    [Fact]
    public void EmptyThreadSerializesWithoutError()
    {
        var thread = new ConversationThread { ThreadId = "t", UserId = "u", Title = "Empty" };
        var messages = new List<StoredChatMessage>();

        var markdown = exporter.ToMarkdown(thread, messages);
        var json = exporter.ToJson(thread, messages);

        Assert.Contains("# Empty", markdown);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, document.RootElement.GetProperty("messages").GetArrayLength());
    }

    /// <summary>
    /// A message without sources emits no Sources block and a null JSON sources value.
    /// </summary>
    [Fact]
    public void MessageWithoutSourcesOmitsSourcesBlock()
    {
        var thread = new ConversationThread { ThreadId = "t", UserId = "u", Title = "NoSources" };
        var messages = new List<StoredChatMessage>
        {
            new() { ThreadId = "t", Role = "user", Content = "Hi" }
        };

        var markdown = exporter.ToMarkdown(thread, messages);
        var json = exporter.ToJson(thread, messages);

        Assert.DoesNotContain("**Sources:**", markdown);
        using var document = JsonDocument.Parse(json);
        var sources = document.RootElement.GetProperty("messages")[0].GetProperty("sources");
        Assert.Equal(JsonValueKind.Null, sources.ValueKind);
    }

    /// <summary>
    /// The generated file name is slugified from the title and carries the requested extension.
    /// </summary>
    [Fact]
    public void FileNameIsSlugifiedWithExtension()
    {
        var thread = new ConversationThread
        {
            ThreadId = "t",
            UserId = "u",
            Title = "My Thread: Q&A!",
            UpdatedAt = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal("my-thread-q-a-20260718.md", exporter.BuildFileName(thread, "md"));
        Assert.Equal("my-thread-q-a-20260718.json", exporter.BuildFileName(thread, "json"));
    }
}
