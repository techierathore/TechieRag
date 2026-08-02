using Microsoft.Extensions.Logging.Abstractions;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Ingestion;

/// <summary>
/// Verification tests for REQ-RAG-003 (folder/pattern ingestion). Before these existed the
/// requirement was carried as <c>Done (pre-existing)</c> with no test asserting that
/// <see cref="ITechieRag.IngestDirectoryAsync"/> honours its search pattern, recurses, skips
/// unsupported extensions, and actually embeds and stores what it walked.
/// </summary>
public sealed class DirectoryIngestionTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"trdiring-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary directory tree the tests ingest from.</summary>
    public DirectoryIngestionTests() => Directory.CreateDirectory(root);

    /// <summary>
    /// The default <c>*.*</c> pattern walks the tree and ingests every file whose extension a
    /// registered processor supports, returning one document id per ingested file.
    /// </summary>
    [Fact]
    public async Task DefaultPatternIngestsEverySupportedFileInTheTree()
    {
        Write("a.txt", "alpha alpha alpha");
        Write("b.md", "# beta\n\nbeta body");
        Write(Path.Combine("nested", "c.txt"), "gamma gamma");

        var (client, store) = CreateClient();
        var ids = await client.IngestDirectoryAsync(root);

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(3, store.Chunks.Select(c => c.DocumentId).Distinct().Count());
    }

    /// <summary>
    /// A narrower search pattern restricts what is walked: only the matching files are ingested,
    /// which is the "pattern" half of the acceptance.
    /// </summary>
    [Fact]
    public async Task SearchPatternRestrictsWhatIsIngested()
    {
        Write("a.txt", "alpha alpha");
        Write("b.md", "# beta");
        Write(Path.Combine("nested", "c.txt"), "gamma gamma");

        var (client, store) = CreateClient();
        var ids = await client.IngestDirectoryAsync(root, "*.md");

        Assert.Single(ids);
        Assert.All(store.Chunks, c => Assert.Equal("b.md", c.Metadata["FileName"]));
    }

    /// <summary>
    /// Files whose extension no registered processor claims are skipped rather than failing the
    /// run, so an ordinary project folder can be pointed at without curating it first.
    /// </summary>
    [Fact]
    public async Task UnsupportedExtensionsAreSkippedNotFailed()
    {
        Write("a.txt", "alpha alpha");
        Write("picture.png", "not really an image");
        Write("archive.zip", "not really a zip");

        var (client, store) = CreateClient();
        var ids = await client.IngestDirectoryAsync(root);

        Assert.Single(ids);
        Assert.All(store.Chunks, c => Assert.Equal("a.txt", c.Metadata["FileName"]));
    }

    /// <summary>
    /// Every ingested chunk is embedded and stored with its vector and its source path, so the
    /// folder walk really produces retrievable content rather than just document ids.
    /// </summary>
    [Fact]
    public async Task IngestedChunksAreEmbeddedAndCarryTheirSourcePath()
    {
        Write("a.txt", "alpha alpha alpha");

        var (client, store) = CreateClient();
        await client.IngestDirectoryAsync(root);

        Assert.NotEmpty(store.Chunks);
        Assert.All(store.Chunks, c =>
        {
            Assert.NotNull(c.Vector);
            Assert.Equal(3, c.Vector!.Length);
            Assert.Equal(Path.Combine(root, "a.txt"), c.Metadata["SourcePath"]);
        });
    }

    /// <summary>A directory that does not exist is reported rather than silently ingesting nothing.</summary>
    [Fact]
    public async Task MissingDirectoryIsReported()
    {
        var (client, _) = CreateClient();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => client.IngestDirectoryAsync(Path.Combine(root, "absent")));
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static (TechieRagClient Client, RecordingVectorStore Store) CreateClient()
    {
        var store = new RecordingVectorStore();
        var client = new TechieRagClient(
            store,
            new FakeEmbeddingProvider(),
            new IDocumentProcessor[] { new TextProcessor(), new MarkdownProcessor() },
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance);
        return (client, store);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Vector store double that keeps every chunk it was asked to store, so ingestion tests can
/// assert what was actually embedded and persisted rather than only what was returned.
/// </summary>
internal sealed class RecordingVectorStore : IVectorStore
{
    /// <summary>Gets every chunk handed to the store, in the order it arrived.</summary>
    public List<TextChunk> Chunks { get; } = [];

    /// <inheritdoc/>
    public string Name => "RecordingVectorStore";

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        Chunks.Add(chunk);
        return Task.FromResult(chunk.Id);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default)
    {
        var list = chunks.ToList();
        Chunks.AddRange(list);
        return Task.FromResult<IReadOnlyList<string>>(list.Select(c => c.Id).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>([]);

    /// <inheritdoc/>
    public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>([]);

    /// <inheritdoc/>
    public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new IngestionStats());

    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
