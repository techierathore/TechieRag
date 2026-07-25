using TechieRag.Abstractions;
using TechieRag.Processors.Chunking;
using Xunit;

namespace TechieRag.Tests.Processors;

/// <summary>
/// Unit tests for the pluggable <see cref="IChunker"/> implementations (REQ-RAG-026):
/// recursive, token-based, sentence, and markdown/code-aware strategies. Verifies the shared
/// contract (no empty/whitespace chunks, reading order preserved) and each strategy's
/// distinguishing behavior.
/// </summary>
public class ChunkerTests
{
    /// <summary>Every strategy must expose a stable, non-empty display name.</summary>
    [Theory]
    [InlineData(typeof(RecursiveChunker), "Recursive")]
    [InlineData(typeof(TokenChunker), "Token")]
    [InlineData(typeof(SentenceChunker), "Sentence")]
    [InlineData(typeof(MarkdownChunker), "Markdown")]
    public void ChunkerReportsExpectedName(Type chunkerType, string expectedName)
    {
        var chunker = (IChunker)Activator.CreateInstance(chunkerType)!;
        Assert.Equal(expectedName, chunker.Name);
    }

    /// <summary>All chunkers return an empty sequence for whitespace-only input.</summary>
    [Theory]
    [MemberData(nameof(AllChunkers))]
    public void ChunkerReturnsNothingForWhitespace(IChunker chunker)
    {
        var chunks = chunker.Chunk("   \n\t  ", 100, 10).ToList();
        Assert.Empty(chunks);
    }

    /// <summary>No chunker may emit an empty or whitespace-only chunk (shared contract).</summary>
    [Theory]
    [MemberData(nameof(AllChunkers))]
    public void ChunkerNeverEmitsBlankChunks(IChunker chunker)
    {
        var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i}."));
        var chunks = chunker.Chunk(text, 120, 20).ToList();

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    /// <summary>The recursive chunker splits a long text into multiple ordered chunks.</summary>
    [Fact]
    public void RecursiveChunkerSplitsLongText()
    {
        var text = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"token{i}"));
        var chunks = RecursiveChunker.Instance.Chunk(text, 100, 10).ToList();

        Assert.True(chunks.Count > 1);
        Assert.Contains("token0", chunks[0]);
    }

    /// <summary>The token chunker keeps each chunk within its (character-derived) token budget.</summary>
    [Fact]
    public void TokenChunkerRespectsTokenBudget()
    {
        var text = string.Join(" ", Enumerable.Range(0, 300).Select(i => $"w{i}"));
        // maxChunkSize 200 chars => ~50 token budget.
        var chunks = new TokenChunker().Chunk(text, 200, 20).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 260, $"chunk length {c.Length} exceeded budget"));
    }

    /// <summary>The sentence chunker never splits inside a sentence.</summary>
    [Fact]
    public void SentenceChunkerKeepsSentencesIntact()
    {
        var text = "The quick brown fox. Jumps over the lazy dog. A third short sentence here. And a fourth one too.";
        var chunks = new SentenceChunker().Chunk(text, 45, 0).ToList();

        Assert.True(chunks.Count > 1);
        // Each emitted chunk should end on a sentence terminator.
        Assert.All(chunks, c => Assert.Contains(c.Trim()[^1], new[] { '.', '!', '?' }));
    }

    /// <summary>The markdown chunker keeps a fenced code block intact even when oversized.</summary>
    [Fact]
    public void MarkdownChunkerKeepsCodeFenceWhole()
    {
        var code = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"var x{i} = {i};"));
        var text = $"# Heading\n\nSome intro prose.\n\n```csharp\n{code}\n```\n";

        var chunks = new MarkdownChunker().Chunk(text, 80, 10).ToList();

        var fenceChunk = chunks.Single(c => c.Contains("```csharp"));
        Assert.Contains("var x0 = 0;", fenceChunk);
        Assert.Contains("var x39 = 39;", fenceChunk);
    }

    /// <summary>Provides one fresh instance of every chunker strategy for theory data.</summary>
    public static IEnumerable<object[]> AllChunkers() =>
    [
        [RecursiveChunker.Instance],
        [new TokenChunker()],
        [new SentenceChunker()],
        [new MarkdownChunker()]
    ];
}
