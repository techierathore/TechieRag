using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace TechieRag.Persistence;

/// <summary>
/// SQLite-backed persistent conversation store (TrThread / TrMessage tables).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Zero-configuration persistent chat history for local and
/// single-machine deployments. Created by TechieRagBuilder.WithPersistence when
/// StoreProvider.Sqlite is configured.</para>
/// </remarks>
public class SqliteConversationStore : RelationalConversationStore
{
    private readonly string connectionString;

    /// <summary>
    /// Creates a new SQLite conversation store.
    /// </summary>
    /// <remarks>Uses the per-app default database, <see cref="Models.DataRoot.DefaultDatabasePath"/>
    /// (REQ-RAG-122 / BRD-182): an existing <c>techierag.db</c> in the folder the app runs from, otherwise
    /// <c>&lt;per-user TechieRag folder&gt;/data/&lt;app name&gt;/techierag.db</c>, whose folder is created.</remarks>
    public SqliteConversationStore()
        : this(Models.DataRoot.ResolveDefaultConnectionString(null))
    {
    }

    /// <summary>
    /// Creates a new SQLite conversation store over the given database.
    /// </summary>
    /// <param name="connectionString">SQLite connection string (e.g. "Data Source=techierag.db").</param>
    /// <exception cref="ArgumentException">Thrown when connectionString is null or empty.</exception>
    public SqliteConversationStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        this.connectionString = connectionString;
    }

    /// <inheritdoc/>
    protected override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
