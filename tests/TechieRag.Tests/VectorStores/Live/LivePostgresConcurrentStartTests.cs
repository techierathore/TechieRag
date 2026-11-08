using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TechieRag.Persistence;
using TechieRag.VectorStores;
using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// Stores that start at the same moment on a new PostgreSQL schema all succeed (REQ-RAG-152 / BRD-210).
/// </summary>
/// <remarks>
/// Found by the live run on 2026-11-08: two <see cref="PgVectorStore"/>s initialising together on an empty database
/// failed with <c>23505</c> on <c>pg_type_typname_nsp_index</c>, because <c>CREATE TABLE IF NOT EXISTS</c> is not
/// safe to run concurrently. Each test here works in a schema of its own, so the tables are new every time and the
/// race is real, then drops it.
/// </remarks>
[Trait("Category", LivePostgresFactAttribute.CategoryName)]
public sealed class LivePostgresConcurrentStartTests
{
    /// <summary>
    /// Four vector stores, two conversation stores and two workspace stores initialised in parallel on one new schema
    /// all succeed, and the tables exist afterwards.
    /// </summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-152 StoresStartingTogetherOnANewDatabaseAllSucceed")]
    public async Task StoresStartingTogetherAllSucceed()
    {
        var schema = $"techierag_test_{Guid.NewGuid():N}";
        var baseConnection = LivePostgresFactAttribute.ConnectionString!;
        await RunAsync(baseConnection, $"CREATE SCHEMA {schema}");
        var connection = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = $"{schema},public" }.ConnectionString;

        var vectorStores = Enumerable.Range(0, 4)
            .Select(_ => new PgVectorStore(connection, NullLogger<PgVectorStore>.Instance, 8))
            .ToList();
        try
        {
            var starts = vectorStores.Select(store => store.InitializeAsync())
                .Concat(Enumerable.Range(0, 2).Select(_ => new PostgresConversationStore(connection).InitializeAsync()))
                .Concat(Enumerable.Range(0, 2).Select(_ => new PostgresWorkspaceStore(connection).InitializeAsync()));

            await Task.WhenAll(starts);

            var tables = await CountAsync(baseConnection,
                $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}' "
                + "AND table_name IN ('documents','chunks','trthread','trmessage','trworkspace','trworkspacedocument')");
            Assert.Equal(6, tables);
        }
        finally
        {
            foreach (var store in vectorStores)
            {
                await store.DisposeAsync();
            }

            await RunAsync(baseConnection, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    /// <summary>Runs one statement.</summary>
    private static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs one count query.</summary>
    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
