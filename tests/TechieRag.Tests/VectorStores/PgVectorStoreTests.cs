using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Models;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// The self-hosted Postgres store BRD-125 names, alongside Qdrant and SqliteVec (REQ-RAG-044).
/// </summary>
/// <remarks>
/// <para><b>This file exists because there was nothing.</b> BRD-125 was re-scoped on owner decision
/// (2026-07-31) to describe what shipped — Qdrant, SqliteVec, PgVector — and left exactly one piece
/// of acceptance open: <i>"<c>PgVectorStore</c> has no test and has never been run against a real
/// Postgres — it cannot count as delivered until it has both."</i> This closes the first half.</para>
/// <para><b>The second half is deliberately not faked.</b> Everything here is hermetic: it proves the
/// store validates its arguments, builds a real Npgsql data source, and surfaces an honest connection
/// failure instead of hanging or pretending to succeed. It does NOT prove a round trip — that needs a
/// server, and it lives in <see cref="LivePgVectorStoreTests"/>, which runs only when one is
/// configured. A store that is never exercised against Postgres must not read as though it were.</para>
/// </remarks>
public sealed class PgVectorStoreTests
{
    /// <summary>A connection string that parses but points nowhere, with a short timeout.</summary>
    /// <remarks>
    /// Port 1 is reserved and never listening, and <c>Timeout=1</c> keeps a failing test fast. The
    /// point is to reach the driver, not to wait for it.
    /// </remarks>
    private const string Unreachable =
        "Host=127.0.0.1;Port=1;Database=techierag_absent;Username=nobody;Password=nothing;Timeout=1;Command Timeout=1";

    /// <summary>The store refuses to be built without a connection string.</summary>
    [Fact]
    public void ANullConnectionStringIsRejected() =>
        Assert.Throws<ArgumentNullException>(
            () => new PgVectorStore(null!, NullLogger<PgVectorStore>.Instance));

    /// <summary>The store refuses to be built without a logger.</summary>
    [Fact]
    public void ANullLoggerIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new PgVectorStore(Unreachable, null!));

    /// <summary>
    /// A connection string the driver cannot parse fails AT CONSTRUCTION, not on first use.
    /// </summary>
    /// <remarks>
    /// Failing early is what makes a typo in configuration a startup error the owner sees, rather
    /// than an exception thrown deep inside the first ingest.
    /// </remarks>
    [Fact]
    public void AMalformedConnectionStringFailsAtConstruction() =>
        Assert.ThrowsAny<ArgumentException>(
            () => new PgVectorStore("this is not a connection string", NullLogger<PgVectorStore>.Instance));

    /// <summary>The store names itself distinctly, so config and traces can say which one is bound.</summary>
    [Fact]
    public async Task ItReportsItsOwnName()
    {
        await using var store = new PgVectorStore(Unreachable, NullLogger<PgVectorStore>.Instance);

        Assert.Equal("PGVector", store.Name);
    }

    /// <summary>
    /// Every read operation against an unreachable server FAILS, rather than returning an empty
    /// result that a caller would read as "there is nothing there".
    /// </summary>
    /// <remarks>
    /// This is the assertion that matters most in a hermetic suite. A store that swallowed a
    /// connection error and returned <c>[]</c> would make a down database indistinguishable from an
    /// empty one — the search returns no sources, the document list looks cleared, and nothing says
    /// why. Each operation is asserted separately because they are separate code paths.
    /// </remarks>
    [Fact]
    public async Task ReadsAgainstAnUnreachableServerFailLoudlyRatherThanReturningEmpty()
    {
        await using var store = new PgVectorStore(Unreachable, NullLogger<PgVectorStore>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => store.ListDocumentsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => store.GetStatsAsync());
        await Assert.ThrowsAnyAsync<Exception>(
            () => store.SearchAsync(new float[1024], 5));
    }

    /// <summary>
    /// Every write operation against an unreachable server fails too, so nothing reports a store it
    /// did not achieve.
    /// </summary>
    [Fact]
    public async Task WritesAgainstAnUnreachableServerFailLoudly()
    {
        await using var store = new PgVectorStore(Unreachable, NullLogger<PgVectorStore>.Instance);

        var chunk = new TextChunk
        {
            DocumentId = "doc-1",
            Text = "some text",
            Vector = new float[1024]
        };

        await Assert.ThrowsAnyAsync<Exception>(() => store.UpsertAsync(chunk));
        await Assert.ThrowsAnyAsync<Exception>(() => store.DeleteAsync("chunk-1"));
        await Assert.ThrowsAnyAsync<Exception>(() => store.DeleteByDocumentAsync("doc-1"));
        await Assert.ThrowsAnyAsync<Exception>(() => store.ClearAsync());
    }

    /// <summary>Disposing a store that never connected is safe.</summary>
    [Fact]
    public async Task DisposingAStoreThatNeverConnectedIsSafe()
    {
        var store = new PgVectorStore(Unreachable, NullLogger<PgVectorStore>.Instance);

        await store.DisposeAsync();
        await store.DisposeAsync();
    }
}
