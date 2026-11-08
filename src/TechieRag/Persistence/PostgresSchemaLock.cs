using System.Data.Common;

namespace TechieRag.Persistence;

/// <summary>
/// A PostgreSQL session advisory lock held while a store creates its tables, so stores starting at the same moment
/// on one database, in one process or several, take turns (REQ-RAG-152).
/// </summary>
/// <remarks>
/// <c>CREATE TABLE IF NOT EXISTS</c> and <c>CREATE EXTENSION IF NOT EXISTS</c> are not safe to run concurrently: on a
/// new database two sessions can both decide the object is missing, and the second fails with <c>23505</c> on
/// <c>pg_type_typname_nsp_index</c>. Found by the live tests on 2026-11-08, where test classes start in parallel.
/// </remarks>
internal static class PostgresSchemaLock
{
    /// <summary>The advisory lock key every TechieRag store shares ("TechieRa" as a 64-bit number).</summary>
    public const long Key = 0x5465636869655261;

    /// <summary>Takes the lock on an open connection; dispose the result to release it.</summary>
    /// <param name="connection">An open PostgreSQL connection, kept open until the result is disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The held lock.</returns>
    public static async Task<IAsyncDisposable> AcquireAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await RunAsync(connection, "SELECT pg_advisory_lock(" + Key + ")", cancellationToken).ConfigureAwait(false);
        return new Held(connection);
    }

    /// <summary>Runs one statement.</summary>
    private static async Task RunAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the lock when disposed.</summary>
    /// <param name="connection">The connection that holds it.</param>
    private sealed class Held(DbConnection connection) : IAsyncDisposable
    {
        /// <inheritdoc/>
        /// <remarks>A failed unlock is not thrown over the error that may be in flight: a session lock also ends when
        /// the session does, and Npgsql resets a pooled session (<c>DISCARD ALL</c>) before reusing it.</remarks>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await RunAsync(connection, "SELECT pg_advisory_unlock(" + Key + ")", CancellationToken.None).ConfigureAwait(false);
            }
            catch (DbException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
