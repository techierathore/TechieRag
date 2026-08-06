using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TechieDesk.Services.Agents;

/// <summary>
/// A read-only SQLite file an agent may query (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Three independent guarantees, in order.</b> First, the file must not be one of the
/// application's own databases — a reporting tool has no business reading the app's users, sessions
/// or licence cache, and that check happens at construction so a misconfiguration fails loudly at
/// startup rather than quietly at the first tool call. Second, the connection is opened
/// <see cref="SqliteOpenMode.ReadOnly"/>, so the driver itself refuses a write even if a statement
/// slipped past. Third, <see cref="SqlQueryGuard"/> has already refused anything that is not a
/// single SELECT.</para>
/// <para><b>Values are bound, never interpolated.</b> Named parameters from the tool call become
/// <see cref="SqliteParameter"/> values, which is what makes an agent-authored query with
/// user-controlled values safe (coding standards §Security).</para>
/// </remarks>
public sealed class SqliteReadOnlySqlQueryTarget : ISqlQueryTarget
{
    private readonly string databasePath;
    private readonly string? refusalReason;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteReadOnlySqlQueryTarget"/> class.
    /// </summary>
    /// <param name="databasePath">Full path to the SQLite file the operator nominated.</param>
    /// <param name="displayName">What to call this database when describing it to the model.</param>
    /// <param name="protectedPaths">
    /// Databases the agent must never read — the application's own files. A target that resolves to
    /// one of these is rejected rather than served.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="databasePath"/> is blank.</exception>
    public SqliteReadOnlySqlQueryTarget(
        string databasePath, string displayName, IEnumerable<string>? protectedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        this.databasePath = Path.GetFullPath(databasePath);
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(this.databasePath)
            : displayName;
        refusalReason = Reject(this.databasePath, protectedPaths);
    }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public bool IsConfigured => refusalReason is null;

    /// <inheritdoc />
    public string? UnavailableReason => refusalReason;

    /// <inheritdoc />
    public async Task<SqlQueryResult> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Key.StartsWith('@') ? parameter.Key : "@" + parameter.Key,
                parameter.Value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(reader, maxRows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads at most one row past the cap, so truncation can be reported honestly.</summary>
    /// <param name="reader">The open reader.</param>
    /// <param name="maxRows">The most rows to return.</param>
    /// <param name="cancellationToken">Token to cancel the read.</param>
    /// <returns>The result set.</returns>
    private static async Task<SqlQueryResult> ReadAsync(
        SqliteDataReader reader, int maxRows, CancellationToken cancellationToken)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<IReadOnlyList<string?>>();
        var isTruncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count >= maxRows)
            {
                isTruncated = true;
                break;
            }

            rows.Add(Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index)
                    ? null
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture))
                .ToList());
        }

        return new SqlQueryResult(columns, rows, isTruncated);
    }

    /// <summary>Decides whether this file may be queried at all.</summary>
    /// <param name="path">The resolved database path.</param>
    /// <param name="protectedPaths">Databases the agent must never read.</param>
    /// <returns>The refusal reason, or null when the file is acceptable.</returns>
    private static string? Reject(string path, IEnumerable<string>? protectedPaths)
    {
        var forbidden = (protectedPaths ?? [])
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(Path.GetFullPath)
            .ToList();

        if (forbidden.Any(candidate => string.Equals(candidate, path, PathComparison)))
        {
            return "the nominated database is the application's own, which agents may not read. "
                + "Point the SQL skill at a separate reporting database.";
        }

        return File.Exists(path)
            ? null
            : $"no database file at '{path}'. Nominate an existing SQLite file for this workspace.";
    }

    /// <summary>The path comparison this platform uses.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
