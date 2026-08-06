using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;
using TechieRag.Tests.TestDoubles;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.Ingestion;

/// <summary>
/// Ingestion stamps every document with what produced its vectors, and the stamp survives the store
/// (REQ-RAG-052 / TR-RAG-044).
/// </summary>
/// <remarks>
/// <para><b>Against the REAL <see cref="SqliteVecStore"/>, for the reason TR-RAG-038 taught.</b> A
/// stamp that ingestion writes onto a chunk but the store drops on the way to the document row is
/// worth nothing — that is exactly how the <c>FileSize</c> key was lost, and a test using an
/// in-memory double would have passed while the feature did nothing. The stamp only means something
/// if it comes back out of <c>ListDocumentsAsync</c>, so that is what is asserted.</para>
/// <para><b>Why the stamp exists at all.</b> Correcting BGE-M3's tokenizer put new vectors in a
/// different space from stored ones. Retrieval did not fail — it returned confident, wrong results —
/// and nothing could tell. This is the half that makes it detectable.</para>
/// </remarks>
public sealed class EmbeddingSignatureStampTests : IDisposable
{
    private const int Dimension = 3;

    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"trstamp-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary directory holding the store and ingested files.</summary>
    public EmbeddingSignatureStampTests() => Directory.CreateDirectory(root);

    /// <summary>File ingestion stamps the document, readable after the store round trip.</summary>
    [Fact]
    public async Task FileIngestionStampsTheSignature()
    {
        var path = Path.Combine(root, "notes.txt");
        await File.WriteAllTextAsync(path, "The quick brown fox jumps over the lazy dog.");

        var (client, store, provider) = await CreateClientAsync();
        var documentId = await client.IngestAsync(path);

        var document = Single(await store.ListDocumentsAsync(), documentId);
        Assert.Equal(
            provider.EmbeddingSignature,
            document.Metadata[DocumentMetadataKeys.EmbeddingSignature]?.ToString());
    }

    /// <summary>Text ingestion stamps it too — every write path, not just the one that was tested.</summary>
    [Fact]
    public async Task TextIngestionStampsTheSignature()
    {
        var (client, store, provider) = await CreateClientAsync();
        var documentId = await client.IngestTextAsync("Pasted body text for the corpus.", "pasted.txt");

        var document = Single(await store.ListDocumentsAsync(), documentId);
        Assert.Equal(
            provider.EmbeddingSignature,
            document.Metadata[DocumentMetadataKeys.EmbeddingSignature]?.ToString());
    }

    /// <summary>
    /// A freshly ingested corpus reports clean through the client's own detection.
    /// </summary>
    [Fact]
    public async Task AFreshlyIngestedCorpusIsNotStale()
    {
        var (client, _, _) = await CreateClientAsync();
        await client.IngestTextAsync("first document", "one.txt");
        await client.IngestTextAsync("second document", "two.txt");

        var report = await client.DetectStaleEmbeddingsAsync();

        Assert.True(report.IsDeterminable);
        Assert.False(report.IsStale);
        Assert.Equal(2, report.TotalDocuments);
    }

    /// <summary>
    /// A corpus ingested by a DIFFERENT provider is detected as stale end-to-end — the whole point.
    /// </summary>
    /// <remarks>
    /// The realistic reproduction of what happened: documents are already in the store, the encoding
    /// changes underneath them, and every subsequent query is embedded into a space the stored
    /// vectors do not share. Here the store is kept and the provider swapped, which is the same
    /// situation an upgrade produces.
    /// </remarks>
    [Fact]
    public async Task ACorpusEmbeddedByAnEarlierRevisionIsDetectedAsStale()
    {
        var store = await CreateStoreAsync();

        var oldClient = ClientOver(store, new StampedEmbeddingProvider("Embedded-ONNX", "bge-m3", revision: 1));
        await oldClient.IngestTextAsync("ingested before the encoding changed", "legacy.txt");

        // Same store, new encoding revision — exactly what a version upgrade does.
        var newClient = ClientOver(store, new StampedEmbeddingProvider("Embedded-ONNX", "bge-m3", revision: 2));
        var report = await newClient.DetectStaleEmbeddingsAsync();

        Assert.True(report.IsDeterminable);
        Assert.True(report.IsStale);
        Assert.True(report.IsEntirelyStale);

        var stale = Assert.Single(report.StaleDocuments);
        Assert.Equal(EmbeddingStalenessReason.DifferentSignature, stale.Reason);
        Assert.Equal("Embedded-ONNX/bge-m3/r1", stale.Signature);
    }

    /// <summary>
    /// A store holding both old and new documents is reported as MIXED — the state a partial
    /// re-ingest leaves behind, and the one that is otherwise invisible.
    /// </summary>
    [Fact]
    public async Task APartiallyReIngestedCorpusIsDetectedAsMixed()
    {
        var store = await CreateStoreAsync();

        var oldClient = ClientOver(store, new StampedEmbeddingProvider("Embedded-ONNX", "bge-m3", revision: 1));
        await oldClient.IngestTextAsync("never re-ingested", "legacy.txt");

        var newClient = ClientOver(store, new StampedEmbeddingProvider("Embedded-ONNX", "bge-m3", revision: 2));
        await newClient.IngestTextAsync("re-ingested after the upgrade", "current.txt");

        var report = await newClient.DetectStaleEmbeddingsAsync();

        Assert.True(report.IsMixed);
        Assert.False(report.IsEntirelyStale);
        Assert.Equal(1, report.StaleCount);
        Assert.Equal(2, report.TotalDocuments);
    }

    /// <summary>
    /// A provider that publishes no signature makes the report say so, rather than reporting clean.
    /// </summary>
    /// <remarks>
    /// <see cref="FakeEmbeddingProvider"/> does not override <c>EmbeddingSignature</c>, so it
    /// exercises the interface default that every existing implementation inherits.
    /// </remarks>
    [Fact]
    public async Task AProviderWithoutASignatureReportsThatItCannotDetermine()
    {
        var client = ClientOver(await CreateStoreAsync(), new FakeEmbeddingProvider());
        await client.IngestTextAsync("some text", "one.txt");

        var report = await client.DetectStaleEmbeddingsAsync();

        Assert.False(report.IsDeterminable);
        Assert.False(report.IsStale);
        Assert.Equal(EmbeddingStaleness.UnknownSignature, report.CurrentSignature);
    }

    /// <summary>Finds one document by id, failing clearly when ingestion produced nothing.</summary>
    /// <param name="documents">The store's documents.</param>
    /// <param name="documentId">The id to find.</param>
    /// <returns>The document.</returns>
    private static Document Single(IReadOnlyList<Document> documents, string documentId) =>
        Assert.Single(documents, document => document.Id == documentId);

    /// <summary>Builds a client over an existing store with a given provider.</summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="provider">The embedding provider.</param>
    /// <returns>The client.</returns>
    private static TechieRagClient ClientOver(SqliteVecStore store, IEmbeddingProvider provider) =>
        new(store,
            provider,
            new IDocumentProcessor[] { new TextProcessor(), new MarkdownProcessor() },
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance);

    /// <summary>Builds a client, its store, and the signature-publishing provider behind it.</summary>
    /// <returns>The client, store and provider.</returns>
    private async Task<(TechieRagClient Client, SqliteVecStore Store, IEmbeddingProvider Provider)>
        CreateClientAsync()
    {
        var store = await CreateStoreAsync();
        var provider = new StampedEmbeddingProvider("Embedded-ONNX", "bge-m3", revision: 2);
        return (ClientOver(store, provider), store, provider);
    }

    /// <summary>Creates a real SQLite-vec store in this test's own directory.</summary>
    /// <returns>The initialized store.</returns>
    private async Task<SqliteVecStore> CreateStoreAsync()
    {
        var store = new SqliteVecStore(
            $"Data Source={Path.Combine(root, $"store-{Guid.NewGuid():N}.db")}", dimensions: Dimension);

        await store.InitializeAsync();
        return store;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A provider that publishes a signature, so an encoding revision can be simulated.</summary>
    /// <param name="name">Provider name.</param>
    /// <param name="model">Model name.</param>
    /// <param name="revision">The encoding revision to advertise.</param>
    private sealed class StampedEmbeddingProvider(string name, string model, int revision)
        : IEmbeddingProvider
    {
        /// <inheritdoc />
        public string Name { get; } = name;

        /// <inheritdoc />
        public string ModelName { get; } = model;

        /// <inheritdoc />
        public int Dimensions => Dimension;

        /// <inheritdoc />
        public string EmbeddingSignature { get; } = EmbeddingStaleness.Signature(name, model, revision);

        /// <inheritdoc />
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

        /// <inheritdoc />
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Vector(text));

        /// <inheritdoc />
        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());

        /// <summary>Builds a deterministic unit vector, so a store round trip is exercised for real.</summary>
        /// <param name="text">The text being embedded.</param>
        /// <returns>The vector.</returns>
        private static float[] Vector(string text)
        {
            var vector = new float[Dimension];
            vector[Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % Dimension] = 1f;
            return vector;
        }
    }
}
