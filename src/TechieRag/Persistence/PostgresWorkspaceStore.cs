using System.Data.Common;
using Npgsql;

namespace TechieRag.Persistence;

/// <summary>
/// PostgreSQL-backed persistent workspace store (TrWorkspace / TrWorkspaceDocument tables).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Production-grade workspace persistence for PostgreSQL environments.
/// Created by TechieRagBuilder.WithPersistence when StoreProvider.Postgres is configured.</para>
/// </remarks>
public class PostgresWorkspaceStore : RelationalWorkspaceStore
{
    private readonly string connectionString;

    /// <summary>
    /// Creates a new PostgreSQL workspace store.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException">Thrown when connectionString is null or empty.</exception>
    public PostgresWorkspaceStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        this.connectionString = connectionString;
    }

    /// <inheritdoc/>
    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
