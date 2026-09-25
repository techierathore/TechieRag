using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Processors;

/// <summary>
/// REQ-RAG-071 / BRD-115 acceptance end to end: a developer who sets
/// <c>WithChunking(ChunkingStrategy.Markdown)</c> and ingests a markdown file gets chunks that follow
/// the file's heading boundaries.
/// </summary>
/// <remarks>
/// Runs through the builder, <c>IngestAsync</c> and a real temporary SQLite store, then reads the
/// stored chunks back with a search, so the chunker is observed where a developer would observe it.
/// </remarks>
public sealed class MarkdownChunkingIngestionTests : IDisposable
{
    private readonly string workFolder = Path.Combine(Path.GetTempPath(), $"trmdchunk-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder holding the markdown file and the vector database.</summary>
    public MarkdownChunkingIngestionTests() => Directory.CreateDirectory(workFolder);

    /// <summary>
    /// A markdown file with three short sections, ingested with the markdown strategy, is stored as one
    /// chunk per section: each chunk holds exactly one section's heading and its own body, never a
    /// neighbouring section's text.
    /// </summary>
    /// <remarks>
    /// Regression guard: before 2026-09-24 this failed (1 chunk, expected 3) because
    /// <c>MarkdownProcessor</c> stripped the <c>#</c> heading markers before the chunker ran.
    /// </remarks>
    [Fact(DisplayName = "REQ-RAG-071 MarkdownIngestionFollowsHeadingBoundaries")]
    public async Task MarkdownIngestionFollowsHeadingBoundaries()
    {
        var rag = new TechieRagBuilder()
            .WithChunking(ChunkingStrategy.Markdown)
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(workFolder, "vectors.db"))
            .Build();
        await rag.InitializeAsync();

        var path = Path.Combine(workFolder, "handbook.md");
        await File.WriteAllTextAsync(
            path,
            "# Installation\n\nRun the installer and accept the licence.\n\n"
            + "## Configuration\n\nSet the region to eu-west before the first start.\n\n"
            + "## Troubleshooting\n\nRestart the service when the log shows a timeout.\n");

        await rag.IngestAsync(path);
        var chunks = (await rag.SearchAsync("handbook", 20)).Select(result => result.Chunk.Text).ToList();

        Assert.Equal(3, chunks.Count);
        AssertSectionIsOneChunk(chunks, "Installation", "accept the licence");
        AssertSectionIsOneChunk(chunks, "Configuration", "eu-west");
        AssertSectionIsOneChunk(chunks, "Troubleshooting", "shows a timeout");
    }

    /// <summary>
    /// Folder ingestion of the same markdown file with the markdown strategy gives the same one chunk
    /// per section as single-file ingestion.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-071 MarkdownFolderIngestionFollowsHeadingBoundaries")]
    public async Task MarkdownFolderIngestionFollowsHeadingBoundaries()
    {
        var rag = await BuildMarkdownRagAsync();
        var folder = Path.Combine(workFolder, "docs");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "handbook.md"), Handbook);

        await rag.IngestDirectoryAsync(folder);
        var chunks = (await rag.SearchAsync("handbook", 20)).Select(result => result.Chunk.Text).ToList();

        Assert.Equal(3, chunks.Count);
        AssertSectionIsOneChunk(chunks, "Installation", "accept the licence");
        AssertSectionIsOneChunk(chunks, "Configuration", "eu-west");
        AssertSectionIsOneChunk(chunks, "Troubleshooting", "shows a timeout");
    }

    /// <summary>
    /// Text ingestion of markdown under a <c>.md</c> name with the markdown strategy gives the same one
    /// chunk per section as file ingestion.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-071 MarkdownTextIngestionFollowsHeadingBoundaries")]
    public async Task MarkdownTextIngestionFollowsHeadingBoundaries()
    {
        var rag = await BuildMarkdownRagAsync();

        await rag.IngestTextAsync(Handbook, "handbook.md");
        var chunks = (await rag.SearchAsync("handbook", 20)).Select(result => result.Chunk.Text).ToList();

        Assert.Equal(3, chunks.Count);
        AssertSectionIsOneChunk(chunks, "Installation", "accept the licence");
        AssertSectionIsOneChunk(chunks, "Configuration", "eu-west");
        AssertSectionIsOneChunk(chunks, "Troubleshooting", "shows a timeout");
    }

    /// <summary>
    /// With the markdown strategy the processor keeps a fenced code block whole in one chunk, while
    /// inline formatting (emphasis, links) is still reduced to its text, and a section ending in a
    /// fence does not leave a stray heading-only chunk behind.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-071 MarkdownProcessorKeepsCodeFenceForMarkdownStrategy")]
    public async Task MarkdownProcessorKeepsCodeFenceForMarkdownStrategy()
    {
        var code = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"var x{i} = {i};"));
        var markdown = $"# Setup\n\nRead the **guide** at [the site](https://example.com).\n\n```csharp\n{code}\n```\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(markdown));

        var chunks = await new TechieRag.Processors.MarkdownProcessor().ProcessAsync(
            stream,
            "setup.md",
            new TechieRag.Abstractions.DocumentProcessingOptions
            {
                MaxChunkSize = 80,
                ChunkOverlap = 10,
                Chunker = new TechieRag.Processors.Chunking.MarkdownChunker()
            });

        // Two chunks: the section's prose and its fence. No third, heading-only chunk after the fence.
        Assert.Equal(2, chunks.Count);
        var fence = Assert.Single(chunks, chunk => chunk.Text.Contains("```csharp", StringComparison.Ordinal));
        Assert.Contains("var x0 = 0;", fence.Text, StringComparison.Ordinal);
        Assert.Contains("var x29 = 29;", fence.Text, StringComparison.Ordinal);
        var prose = Assert.Single(chunks, chunk => chunk.Text.StartsWith("# Setup", StringComparison.Ordinal));
        Assert.Contains("Read the guide at the site.", prose.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", prose.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// With any other strategy the processor's plain-text output is unchanged: heading markers are
    /// stripped and the three short sections fit in one recursive chunk, exactly as before the fix.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-071 MarkdownProcessorKeepsPlainTextForOtherStrategies")]
    public async Task MarkdownProcessorKeepsPlainTextForOtherStrategies()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Handbook));

        var chunks = await new TechieRag.Processors.MarkdownProcessor().ProcessAsync(
            stream,
            "handbook.md",
            new TechieRag.Abstractions.DocumentProcessingOptions
            {
                Chunker = TechieRag.Processors.Chunking.RecursiveChunker.Instance
            });

        var chunk = Assert.Single(chunks);
        Assert.DoesNotContain("#", chunk.Text, StringComparison.Ordinal);
        Assert.StartsWith("Installation", chunk.Text, StringComparison.Ordinal);
        Assert.Contains("shows a timeout", chunk.Text, StringComparison.Ordinal);
    }

    private const string Handbook =
        "# Installation\n\nRun the installer and accept the licence.\n\n"
        + "## Configuration\n\nSet the region to eu-west before the first start.\n\n"
        + "## Troubleshooting\n\nRestart the service when the log shows a timeout.\n";

    private async Task<ITechieRag> BuildMarkdownRagAsync()
    {
        var rag = new TechieRagBuilder()
            .WithChunking(ChunkingStrategy.Markdown)
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(workFolder, "vectors.db"))
            .Build();
        await rag.InitializeAsync();
        return rag;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
    }

    private static void AssertSectionIsOneChunk(List<string> chunks, string heading, string body)
    {
        var chunk = Assert.Single(chunks, text => text.Contains(heading, StringComparison.Ordinal));
        Assert.Contains(body, chunk, StringComparison.Ordinal);
        Assert.Equal(1, new[] { "Installation", "Configuration", "Troubleshooting" }.Count(h => chunk.Contains(h, StringComparison.Ordinal)));
    }
}
