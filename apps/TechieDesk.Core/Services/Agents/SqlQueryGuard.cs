using System.Text.RegularExpressions;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Decides whether a statement an LLM produced may reach a database at all (REQ-RAG-022 safety
/// boundary; coding standards §Security).
/// </summary>
/// <remarks>
/// <para><b>The boundary this enforces:</b> ONE read-only statement. A single <c>SELECT</c> (or a
/// <c>WITH … SELECT</c>), no statement separator, no comment syntax, no DDL or DML verb anywhere in
/// the text, and a row cap applied by the caller. Everything else is refused before a connection is
/// opened.</para>
/// <para><b>Why a deny-list AND a shape check.</b> A verb deny-list alone is defeated by comment
/// splicing and stacked statements; a "starts with SELECT" check alone is defeated by
/// <c>SELECT 1; DROP TABLE …</c>. Requiring the statement to both start as a query and contain
/// none of the forbidden tokens and carry no separator or comment means each trick has to beat all
/// three at once.</para>
/// <para><b>This is the second line, not the first.</b> The first is
/// <see cref="ISqlQueryTarget"/>: the connection itself is opened read-only against a database the
/// operator nominated, which is never the application's own. The guard exists so a
/// misconfiguration cannot become data loss.</para>
/// </remarks>
public static class SqlQueryGuard
{
    /// <summary>The longest statement accepted, in characters.</summary>
    public const int MaxStatementLength = 4000;

    /// <summary>The default number of rows returned when the caller does not choose.</summary>
    public const int DefaultMaxRows = 100;

    /// <summary>The most rows any single query may return, whatever the caller asks for.</summary>
    public const int RowCeiling = 1000;

    /// <summary>
    /// Verbs that write, define or reconfigure. None may appear anywhere in an accepted statement.
    /// </summary>
    private static readonly string[] ForbiddenVerbs =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE", "REPLACE", "MERGE",
        "GRANT", "REVOKE", "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX", "ANALYZE", "EXEC",
        "EXECUTE", "CALL", "SET", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE", "INTO",
        "LOAD", "COPY", "IMPORT", "OUTFILE", "DUMPFILE"
    ];

    private static readonly Regex WordPattern =
        new(@"[A-Za-z][A-Za-z0-9]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks a statement against the read-only boundary.
    /// </summary>
    /// <param name="sql">The statement the model produced.</param>
    /// <returns>
    /// Null when the statement is acceptable, otherwise the refusal to hand back to the model —
    /// phrased so it can correct itself rather than retry the same thing.
    /// </returns>
    public static string? Refuse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "No SQL was supplied, so nothing was run.";
        }

        var statement = sql.Trim();
        if (statement.Length > MaxStatementLength)
        {
            return $"Refused: the statement is longer than the {MaxStatementLength}-character limit.";
        }

        if (statement.Contains("--", StringComparison.Ordinal)
            || statement.Contains("/*", StringComparison.Ordinal))
        {
            return "Refused: comments are not allowed, because they can hide a second statement.";
        }

        var body = statement.TrimEnd(';').Trim();
        if (body.Contains(';', StringComparison.Ordinal))
        {
            return "Refused: only one statement may be run at a time.";
        }

        if (!StartsAsQuery(body))
        {
            return "Refused: only read-only SELECT queries are allowed. Start the statement with "
                + "SELECT or WITH.";
        }

        var forbidden = FirstForbiddenVerb(body);
        return forbidden is null
            ? null
            : $"Refused: '{forbidden}' is not allowed. This tool runs read-only SELECT queries only.";
    }

    /// <summary>
    /// Clamps a requested row count into the range this guard permits.
    /// </summary>
    /// <param name="requested">The row count the model asked for.</param>
    /// <returns>The row count that will actually be applied.</returns>
    public static int ClampRows(int requested) => Math.Clamp(requested, 1, RowCeiling);

    /// <summary>Gets whether the statement opens as a query rather than as a command.</summary>
    /// <param name="body">The trimmed statement.</param>
    /// <returns>True when it starts with SELECT or WITH.</returns>
    private static bool StartsAsQuery(string body)
    {
        var first = WordPattern.Match(body);
        return first.Success
            && (first.Index == 0 || body[..first.Index].Trim('(', ' ', '\t', '\r', '\n').Length == 0)
            && (first.Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
                || first.Value.Equals("WITH", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the first forbidden verb in the statement, if any.</summary>
    /// <param name="body">The trimmed statement.</param>
    /// <returns>The verb in upper case, or null when the statement is clean.</returns>
    /// <remarks>
    /// Matching whole words only. A column called <c>UpdatedOn</c> or a table called
    /// <c>DeletionRequest</c> is ordinary read-only schema and must not be refused.
    /// </remarks>
    private static string? FirstForbiddenVerb(string body) => WordPattern.Matches(body)
        .Select(match => match.Value.ToUpperInvariant())
        .FirstOrDefault(word => ForbiddenVerbs.Contains(word, StringComparer.Ordinal));
}
