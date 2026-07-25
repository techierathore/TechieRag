using System.Data.Common;
using Npgsql;

namespace TechieRag.Persistence;

/// <summary>
/// PostgreSQL-backed persistent conversation store (TrThread / TrMessage tables).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Production-grade persistent chat history for PostgreSQL
/// environments. Created by TechieRagBuilder.WithPersistence when StoreProvider.Postgres
/// is configured.</para>
/// </remarks>
public class PostgresConversationStore : RelationalConversationStore
{
    private readonly string connectionString;

    /// <summary>
    /// Creates a new PostgreSQL conversation store.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException">Thrown when connectionString is null or empty.</exception>
    public PostgresConversationStore(string connectionString)
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
