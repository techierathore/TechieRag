using System.Data.Common;
using Npgsql;

namespace TechieRag.Persistence;

/// <summary>
/// Keeps every token usage in the <c>TrTokenUsage</c> table of a PostgreSQL database (REQ-RAG-162, Sevak TR-RAG-059).
/// </summary>
/// <remarks>The table is created under the same advisory lock as the other TechieRag stores (REQ-RAG-152).</remarks>
public class PostgresTokenUsageLog : RelationalTokenUsageLog
{
    private readonly string connectionString;

    /// <summary>Initializes a new instance of the <see cref="PostgresTokenUsageLog"/> class.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty.</exception>
    public PostgresTokenUsageLog(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        this.connectionString = connectionString;
    }

    /// <inheritdoc/>
    protected override DbConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    /// <inheritdoc/>
    protected override void WithSchemaLock(DbConnection connection, Action createSchema)
    {
        Execute(connection, "SELECT pg_advisory_lock(" + PostgresSchemaLock.Key + ")");
        try
        {
            createSchema();
        }
        finally
        {
            Execute(connection, "SELECT pg_advisory_unlock(" + PostgresSchemaLock.Key + ")");
        }
    }
}
