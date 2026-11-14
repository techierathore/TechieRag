using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace TechieRag.Persistence;

/// <summary>
/// Keeps every token usage in the <c>TrTokenUsage</c> table of a SQLite database (REQ-RAG-162, Sevak TR-RAG-059).
/// </summary>
public class SqliteTokenUsageLog : RelationalTokenUsageLog
{
    private readonly string connectionString;

    /// <summary>Initializes a new instance of the <see cref="SqliteTokenUsageLog"/> class.</summary>
    /// <param name="connectionString">The SQLite connection string, for example <c>Data Source=app.db</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty.</exception>
    public SqliteTokenUsageLog(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        this.connectionString = connectionString;
    }

    /// <inheritdoc/>
    protected override DbConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
