using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Connectors;
using TechieRag.Models;
using TechieRag.Tests.Connectors;
using TechieRag.Tests.TestDoubles;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.Ingestion;

/// <summary>
/// Text ingestion under a caller key replaces the earlier document (REQ-RAG-120 / BRD-179, Sevak
/// feedback TR-RAG-026), against a real SQLite vector store.
/// </summary>
public sealed class KeyedTextIngestionTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trkeyed-{Guid.NewGuid():N}.db");
    private readonly TechieRagClient client;

    /// <summary>Creates a client over a unique temp SQLite vector store.</summary>
    public KeyedTextIngestionTests() =>
        client = new TechieRagClient(
            new SqliteVecStore($"Data Source={dbPath}", 3),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance);

    /// <summary>
    /// A developer ingests text twice under the same key: the document list holds one document, and
    /// its stored text is the second text.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-120 SameKeyReplacesEarlierDocument")]
    public async Task SameKeyReplacesEarlierDocument()
    {
        await client.InitializeAsync();

        var firstId = await client.IngestTextAsync("The first text about refunds.", "policy.txt", "policy-key", null);
        var secondId = await client.IngestTextAsync("The second text about returns.", "policy.txt", "policy-key", null);

        var documents = await client.ListDocumentsAsync();
        var document = Assert.Single(documents);
        Assert.Equal(firstId, secondId);
        Assert.Equal("policy-key", document.Metadata[DocumentMetadataKeys.SourceKey]);

        var stored = await client.SearchAsync("returns", 10);
        Assert.All(stored, r => Assert.Equal("The second text about returns.", r.Chunk.Text));
        Assert.NotEmpty(stored);
    }

    /// <summary>Text ingested without a key, or under a different key, is never replaced.</summary>
    [Fact(DisplayName = "REQ-RAG-120 DifferentKeysAndNoKeyAddDocuments")]
    public async Task DifferentKeysAndNoKeyAddDocuments()
    {
        await client.InitializeAsync();

        await client.IngestTextAsync("one", "a.txt", "key-a", null);
        await client.IngestTextAsync("two", "b.txt", "key-b", null);
        await client.IngestTextAsync("three", "c.txt");
        await client.IngestTextAsync("three", "c.txt");

        Assert.Equal(4, (await client.ListDocumentsAsync()).Count);
    }

    /// <summary>
    /// <c>IngestConnectorAsync</c> passes the connector's item id as the key, so running the same
    /// connector twice leaves one document per item.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-120 ConnectorResyncReplacesItemDocuments")]
    public async Task ConnectorResyncReplacesItemDocuments()
    {
        await client.InitializeAsync();
        var item = new ConnectorItem("readme", "README.md", "https://example.test/readme", Version: "v1");

        await ((ITechieRag)client).IngestConnectorAsync(new FakeDataConnector().Page(item).WithText("readme", "old text"));
        await ((ITechieRag)client).IngestConnectorAsync(new FakeDataConnector().Page(item with { Version = "v2" }).WithText("readme", "new text"));

        var document = Assert.Single(await client.ListDocumentsAsync());
        Assert.Equal("readme", document.Metadata[DocumentMetadataKeys.ItemId]);
        Assert.All(await client.SearchAsync("text", 10), r => Assert.Equal("new text", r.Chunk.Text));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
