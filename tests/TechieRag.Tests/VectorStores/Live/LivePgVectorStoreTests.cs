using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// The open half of BRD-125's acceptance: <c>PgVectorStore</c> against a REAL PostgreSQL server
/// (REQ-RAG-044).
/// </summary>
/// <remarks>
/// <para>BRD-125 was re-scoped on owner decision to describe the three stores that actually shipped —
/// Qdrant, SqliteVec and PgVector — and left one condition on the last: it <i>"has no test and has
/// never been run against a real Postgres — it cannot count as delivered until it has both."</i>
/// <see cref="PgVectorStoreTests"/> closed the first. This closes the second, on any host that
/// declares a server.</para>
/// <para><b>These skip rather than pass when no server is configured.</b> A skipped test is a visible
/// gap; a hermetic substitute pretending to be a round trip is a false claim of delivery, and this
/// row was demoted once already for exactly that kind of overstatement.</para>
/// <para><b>Every test cleans up after itself</b> and works in its own document id namespace, so a
/// run against a shared development server neither collides with another run nor leaves rows behind.
/// The server needs the <c>vector</c> extension available; <c>InitializeAsync</c> creates it.</para>
/// </remarks>
[Trait("Category", LivePostgresFactAttribute.CategoryName)]
public sealed class LivePgVectorStoreTests
{
    private const int Dimension = 1024;

    /// <summary>A chunk written to a real server comes back from a search over the same vector.</summary>
    /// <remarks>
    /// The core round trip — embed, store, retrieve — and the one thing that cannot be proven
    /// without a server. Search is asserted to return the chunk's TEXT, not merely a row count, so a
    /// store that persisted an empty body would fail.
    /// </remarks>
    [LivePostgresFact]
    public async Task AChunkSurvivesAnUpsertAndComesBackFromASearch()
    {
        var documentId = NewDocumentId();
        await using var store = NewStore();
        await store.InitializeAsync();

        try
        {
            var embedding = UnitVectorAt(index: 7);
            await store.UpsertAsync(new TextChunk
            {
                DocumentId = documentId,
                Text = "the quick brown fox",
                Vector = embedding
            });

            var results = await store.SearchAsync(embedding, topK: 5, documentFilter: documentId);

            var match = Assert.Single(results);
            Assert.Equal("the quick brown fox", match.Chunk.Text);
            Assert.Equal(documentId, match.Chunk.DocumentId);
        }
        finally
        {
            await store.DeleteByDocumentAsync(documentId);
        }
    }

    /// <summary>A document filter scopes a search to one document, and excludes the others.</summary>
    [LivePostgresFact]
    public async Task ADocumentFilterExcludesOtherDocuments()
    {
        var mine = NewDocumentId();
        var theirs = NewDocumentId();
        await using var store = NewStore();
        await store.InitializeAsync();

        try
        {
            var embedding = UnitVectorAt(index: 3);

            await store.UpsertBatchAsync(
            [
                new TextChunk { DocumentId = mine, Text = "mine", Vector = embedding },
                new TextChunk { DocumentId = theirs, Text = "theirs", Vector = embedding }
            ]);

            var results = await store.SearchAsync(embedding, topK: 10, documentFilter: mine);

            Assert.All(results, result => Assert.Equal(mine, result.Chunk.DocumentId));
            Assert.Contains(results, result => result.Chunk.Text == "mine");
            Assert.DoesNotContain(results, result => result.Chunk.Text == "theirs");
        }
        finally
        {
            await store.DeleteByDocumentAsync(mine);
            await store.DeleteByDocumentAsync(theirs);
        }
    }

    /// <summary>Deleting a document removes its chunks, and a later search cannot find them.</summary>
    /// <remarks>
    /// Asserted through a search rather than a count, because "the row is gone" and "the vector is no
    /// longer retrievable" are different claims, and it is the second one a user experiences.
    /// </remarks>
    [LivePostgresFact]
    public async Task DeletingADocumentRemovesItsVectorsFromSearch()
    {
        var documentId = NewDocumentId();
        await using var store = NewStore();
        await store.InitializeAsync();

        var embedding = UnitVectorAt(index: 11);
        await store.UpsertAsync(new TextChunk
        {
            DocumentId = documentId,
            Text = "temporary",
            Vector = embedding
        });

        Assert.NotEmpty(await store.SearchAsync(embedding, topK: 5, documentFilter: documentId));

        await store.DeleteByDocumentAsync(documentId);

        Assert.Empty(await store.SearchAsync(embedding, topK: 5, documentFilter: documentId));
    }

    /// <summary>Stats count what was actually stored, so a dashboard reads real numbers.</summary>
    [LivePostgresFact]
    public async Task StatsReflectWhatWasStored()
    {
        var documentId = NewDocumentId();
        await using var store = NewStore();
        await store.InitializeAsync();

        try
        {
            var before = await store.GetStatsAsync();

            await store.UpsertBatchAsync(
            [
                new TextChunk { DocumentId = documentId, Text = "one", Vector = UnitVectorAt(1) },
                new TextChunk { DocumentId = documentId, Text = "two", Vector = UnitVectorAt(2) }
            ]);

            var after = await store.GetStatsAsync();

            Assert.Equal(before.TotalChunks + 2, after.TotalChunks);
            Assert.Contains(await store.ListDocumentsAsync(), document => document.Id == documentId);
        }
        finally
        {
            await store.DeleteByDocumentAsync(documentId);
        }
    }

    /// <summary>Text carrying a null byte is stored rather than rejected by the server.</summary>
    /// <remarks>
    /// PostgreSQL refuses <c>0x00</c> inside a UTF-8 text column, and real extracted text — from a
    /// PDF especially — contains it. The store sanitizes; without a server there is no way to prove
    /// the sanitizing is sufficient, only that it happens.
    /// </remarks>
    [LivePostgresFact]
    public async Task TextContainingANullByteIsStoredRatherThanRejected()
    {
        var documentId = NewDocumentId();
        await using var store = NewStore();
        await store.InitializeAsync();

        try
        {
            var embedding = UnitVectorAt(index: 5);
            await store.UpsertAsync(new TextChunk
            {
                DocumentId = documentId,
                Text = "before\0after",
                Vector = embedding
            });

            var match = Assert.Single(
                await store.SearchAsync(embedding, topK: 5, documentFilter: documentId));

            Assert.DoesNotContain('\0', match.Chunk.Text);
            Assert.Contains("before", match.Chunk.Text, StringComparison.Ordinal);
            Assert.Contains("after", match.Chunk.Text, StringComparison.Ordinal);
        }
        finally
        {
            await store.DeleteByDocumentAsync(documentId);
        }
    }

    /// <summary>Builds a store bound to the host's configured server.</summary>
    /// <returns>The store.</returns>
    private static PgVectorStore NewStore() => new(
        LivePostgresFactAttribute.ConnectionString!,
        NullLogger<PgVectorStore>.Instance,
        Dimension);

    /// <summary>Builds a document id unique to this run, so concurrent runs cannot collide.</summary>
    /// <returns>The id.</returns>
    private static string NewDocumentId() => $"techierag-test-{Guid.NewGuid():N}";

    /// <summary>Builds a unit vector with a single non-zero component.</summary>
    /// <param name="index">Which component carries the 1.</param>
    /// <returns>The vector.</returns>
    /// <remarks>
    /// Distinct one-hot vectors keep cosine distance unambiguous, so an assertion about WHICH chunk
    /// came back is about the store's filtering rather than about floating-point luck.
    /// </remarks>
    private static float[] UnitVectorAt(int index)
    {
        var vector = new float[Dimension];
        vector[index % Dimension] = 1f;
        return vector;
    }
}
