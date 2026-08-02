using TechieDesk.Services.Workspaces;
using TechieRag;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// REQ-UI-021 (BRD-46): the document library's <b>Size</b> column, driven through the real
/// ingestion pipeline into a real SqliteVec store and read back by the real production probe.
/// </summary>
/// <remarks>
/// <para>The column rendered an em-dash on every row of a three-row library while every other
/// column was correct. The probe was never the problem — it was right all along — so the assertion
/// that matters is the WHOLE path: ingest a file of known length, let it round-trip the store, and
/// ask the shipping <see cref="DocumentSizeDisplay"/> what the cell says. Asserting the formatter
/// against a hand-built dictionary is what let the defect ship, because that assertion passes with
/// nothing in the pipeline writing the key.</para>
/// <para>Only the embedding model is substituted, and it is not under test.</para>
/// </remarks>
public sealed class DocumentSizeDisplayTests : IDisposable
{
    private const string WorkspaceName = "Library";

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"techiedesk-docsize-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary directory holding the store and the ingested file.</summary>
    public DocumentSizeDisplayTests() => Directory.CreateDirectory(root);

    /// <summary>
    /// A file added to a workspace shows a human-readable size in the library table instead of the
    /// unknown-size em-dash.
    /// </summary>
    [Fact]
    public async Task AnIngestedFileRendersAHumanReadableSize()
    {
        var (rag, manager, workspaceId) = await CreateStackAsync();
        var path = WriteFile("notes.txt", new string('a', 4096));

        var documentId = await manager.IngestFileAsync(workspaceId, path);
        var document = FindDocument(await rag.ListDocumentsAsync(), documentId);

        Assert.Equal("4.0 KB", DocumentSizeDisplay.FromMetadata(document));
    }

    /// <summary>
    /// A file under a kilobyte reports its exact byte count, so a small document is not rounded
    /// into looking empty.
    /// </summary>
    [Fact]
    public async Task ASmallFileRendersItsExactByteCount()
    {
        var (rag, manager, workspaceId) = await CreateStackAsync();
        var path = WriteFile("tiny.txt", new string('b', 640));

        var documentId = await manager.IngestFileAsync(workspaceId, path);
        var document = FindDocument(await rag.ListDocumentsAsync(), documentId);

        Assert.Equal("640 B", DocumentSizeDisplay.FromMetadata(document));
    }

    /// <summary>
    /// Pasted text reports the size of the text that was stored, which is the artefact for that
    /// ingestion route.
    /// </summary>
    [Fact]
    public async Task PastedTextRendersTheSizeOfTheStoredText()
    {
        var (rag, manager, workspaceId) = await CreateStackAsync();
        var text = new string('c', 2048);

        var documentId = await manager.IngestTextAsync(workspaceId, text, "pasted.txt");
        var document = FindDocument(await rag.ListDocumentsAsync(), documentId);

        Assert.Equal("2.0 KB", DocumentSizeDisplay.FromMetadata(document));
    }

    /// <summary>
    /// A document ingested before the pipeline recorded a size still renders — as the em-dash,
    /// which is the honest answer — rather than throwing and taking the table down with it.
    /// </summary>
    [Fact]
    public async Task ADocumentWithNoRecordedSizeRendersTheEmDash()
    {
        var (_, _, _) = await CreateStackAsync();

        var legacy = new Document
        {
            Id = "legacy",
            Name = "legacy.txt",
            SourcePath = "/somewhere/legacy.txt",
            ChunkCount = 1,
            IngestedAt = DateTime.UtcNow
        };

        Assert.Equal("—", DocumentSizeDisplay.FromMetadata(legacy));
    }

    /// <summary>A library row with no catalogue entry at all renders the em-dash, not an exception.</summary>
    [Fact]
    public void AMissingDocumentRendersTheEmDash() =>
        Assert.Equal("—", DocumentSizeDisplay.FromMetadata(null));

    private static Document FindDocument(IReadOnlyList<Document> documents, string documentId) =>
        Assert.Single(documents, document => document.Id == documentId);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<(ITechieRag Rag, WorkspaceManager Manager, string WorkspaceId)> CreateStackAsync()
    {
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new StubEmbeddingProvider())
            .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(root, "vectors.db")}")
            .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(root, "rag.db")}")
            .Build();

        await rag.InitializeAsync();

        var manager = rag.GetWorkspaceManager()
            ?? throw new InvalidOperationException("The builder produced no workspace manager.");
        var workspace = await manager.CreateWorkspaceAsync(WorkspaceName);
        return (rag, manager, workspace.WorkspaceId);
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
