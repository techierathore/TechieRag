using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;
using TechieRag.Tests.TestDoubles;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.Ingestion;

/// <summary>
/// REQ-UI-021 / TR-RAG-004: the ingestion pipeline must record the source artefact's byte size in
/// <see cref="Document.Metadata"/>, and it must still be there after a real vector-store round trip.
/// </summary>
/// <remarks>
/// <para>Everything here runs against the REAL <see cref="SqliteVecStore"/> on a real file, because
/// both halves of the defect lived in the round trip. The document library's Size column rendered an
/// em-dash on every row for two reasons at once: nothing wrote a size key, and the store hardcoded
/// the document row's metadata column to a literal <c>{}</c> so nothing it wrote could have
/// survived. A test that asserted the chunk dictionary, or that used an in-memory store double,
/// would have passed while the screen stayed blank.</para>
/// <para>The third trap is asserted too. Reading the column back with
/// <c>JsonSerializer.Deserialize&lt;Dictionary&lt;string, object&gt;&gt;</c> yields
/// <c>JsonElement</c> values, which are not <c>IConvertible</c>; the size would then be present and
/// still unreadable to the consumer, which is indistinguishable from the original bug on screen.</para>
/// </remarks>
public sealed class DocumentSizeMetadataTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"trdocsize-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary directory holding the store and the ingested files.</summary>
    public DocumentSizeMetadataTests() => Directory.CreateDirectory(root);

    /// <summary>
    /// Ingesting a file of known length records exactly that many bytes on the document, readable
    /// from <c>ListDocumentsAsync</c> after the store round trip.
    /// </summary>
    [Fact]
    public async Task FileIngestionRecordsTheSourceByteSize()
    {
        var path = WriteFile("notes.txt", new string('a', 4096));
        var expected = new FileInfo(path).Length;

        var (client, store) = await CreateClientAsync();
        var documentId = await client.IngestAsync(path);

        var document = Single(await store.ListDocumentsAsync(), documentId);
        Assert.True(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
        Assert.Equal(expected, bytes);
    }

    /// <summary>
    /// The stored size comes back as an ordinary CLR integer, so the obvious consumer conversion
    /// works instead of throwing on a <c>JsonElement</c>.
    /// </summary>
    [Fact]
    public async Task StoredSizeSurvivesTheRoundTripAsAConvertibleNumber()
    {
        var path = WriteFile("notes.txt", new string('b', 2048));

        var (client, store) = await CreateClientAsync();
        var documentId = await client.IngestAsync(path);

        var document = Single(await store.ListDocumentsAsync(), documentId);
        var value = document.Metadata[DocumentMetadataKeys.FileSize];
        Assert.IsNotType<System.Text.Json.JsonElement>(value);

        // A whole byte count comes back as a whole number. It arrived as a double once, because the
        // obvious unwrapping ternary widens long to double before boxing it.
        Assert.IsType<long>(value);
        Assert.Equal(new FileInfo(path).Length, Convert.ToInt64(value));
    }

    /// <summary>
    /// Text ingestion records the UTF-8 byte count of the text it stored — the artefact in that
    /// route — rather than leaving the size unknown.
    /// </summary>
    [Fact]
    public async Task TextIngestionRecordsTheUtfEightByteCount()
    {
        const string text = "The quick brown fox jumps over the lazy dog. Ingested as pasted text.";

        var (client, store) = await CreateClientAsync();
        var documentId = await client.IngestTextAsync(text, "pasted.txt");

        var document = Single(await store.ListDocumentsAsync(), documentId);
        Assert.True(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
        Assert.Equal(Encoding.UTF8.GetByteCount(text), bytes);
    }

    /// <summary>
    /// A caller that knows a truer size than the text it is handing over — a connector holding the
    /// original item's length — wins over the computed default.
    /// </summary>
    [Fact]
    public async Task CallerSuppliedSizeOverridesTheComputedTextSize()
    {
        var (client, store) = await CreateClientAsync();
        var documentId = await client.IngestTextAsync(
            "short body",
            "report.pdf",
            new Dictionary<string, object> { [DocumentMetadataKeys.FileSize] = 987654L });

        var document = Single(await store.ListDocumentsAsync(), documentId);
        Assert.True(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
        Assert.Equal(987654L, bytes);
    }

    /// <summary>
    /// Folder ingestion gives each walked file its OWN size, not the first file's, so a library
    /// listing a whole folder is not uniformly wrong.
    /// </summary>
    [Fact]
    public async Task FolderIngestionRecordsEachFilesOwnSize()
    {
        WriteFile("small.txt", new string('a', 300));
        WriteFile("large.txt", new string('b', 9000));

        var (client, store) = await CreateClientAsync();
        await client.IngestDirectoryAsync(root, "*.txt");

        var sizes = new List<long>();
        foreach (var document in await store.ListDocumentsAsync())
        {
            Assert.True(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
            sizes.Add(bytes);
        }

        Assert.Equal([300L, 9000L], sizes.OrderBy(size => size).ToArray());
    }

    /// <summary>
    /// A document written before the size key existed reads back with no size and does not throw —
    /// the em-dash fallback is the correct rendering for it and must not be backfilled with a guess.
    /// </summary>
    [Fact]
    public async Task DocumentIngestedBeforeTheSizeKeyExistedReadsBackWithoutOne()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync(new TextChunk
        {
            DocumentId = "legacy-document",
            Text = "ingested by an earlier build",
            Vector = [0.1f, 0.2f, 0.3f],
            Metadata = new Dictionary<string, object>
            {
                ["DocumentName"] = "legacy.txt",
                ["SourcePath"] = "/somewhere/legacy.txt"
            }
        });

        var document = Single(await store.ListDocumentsAsync(), "legacy-document");
        Assert.False(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
        Assert.Equal(0L, bytes);
    }

    /// <summary>
    /// Chunk-local metadata stays on the chunk: a page number lifted onto the document row would be
    /// published as a fact about the whole document.
    /// </summary>
    [Fact]
    public async Task ChunkLocalMetadataIsNotLiftedOntoTheDocument()
    {
        var store = await CreateStoreAsync();
        await store.UpsertAsync(new TextChunk
        {
            DocumentId = "paged-document",
            Text = "page one",
            PageNumber = 1,
            Vector = [0.1f, 0.2f, 0.3f],
            Metadata = new Dictionary<string, object>
            {
                ["DocumentName"] = "manual.pdf",
                ["SourcePath"] = "/somewhere/manual.pdf",
                ["PageNumber"] = 1,
                [DocumentMetadataKeys.FileSize] = 5120L
            }
        });

        var document = Single(await store.ListDocumentsAsync(), "paged-document");
        Assert.False(document.Metadata.ContainsKey("PageNumber"));
        Assert.True(DocumentMetadataKeys.TryGetFileSizeBytes(document, out var bytes));
        Assert.Equal(5120L, bytes);
    }

    private static Document Single(IReadOnlyList<Document> documents, string documentId) =>
        Assert.Single(documents, document => document.Id == documentId);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<SqliteVecStore> CreateStoreAsync()
    {
        var store = new SqliteVecStore($"Data Source={Path.Combine(root, "vectors.db")}", dimensions: 3);
        await store.InitializeAsync();
        return store;
    }

    private async Task<(TechieRagClient Client, SqliteVecStore Store)> CreateClientAsync()
    {
        var store = await CreateStoreAsync();
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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
