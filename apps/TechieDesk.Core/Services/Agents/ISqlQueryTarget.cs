namespace TechieDesk.Services.Agents;

/// <summary>
/// The rows one read-only query returned.
/// </summary>
/// <param name="Columns">The column names, in select order.</param>
/// <param name="Rows">The rows, each aligned to <paramref name="Columns"/>. Nulls stay null.</param>
/// <param name="IsTruncated">True when the query had more rows than the cap allowed.</param>
public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool IsTruncated);

/// <summary>
/// The database the <c>sql-query</c> skill is allowed to read (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Why the target is a dependency rather than a connection string in the tool call.</b> If
/// the model supplied the target, an agent could be talked into reading any database the host can
/// reach, including the application's own. The target is nominated by the operator once, out of
/// band; the tool call only ever supplies the query.</para>
/// <para><b>Read-only is the target's job, not the model's.</b> Implementations open the connection
/// in a read-only mode so a statement that somehow got past <see cref="SqlQueryGuard"/> still
/// cannot write. The guard is the second line of defence, not the only one.</para>
/// <para><b>No target configured is the normal state.</b> The skill then reports itself
/// <see cref="SkillUnavailable">unavailable</see> with the reason, which is what a workspace that
/// has never nominated a database should see.</para>
/// </remarks>
public interface ISqlQueryTarget
{
    /// <summary>Gets a short description of what is being queried, shown to the model.</summary>
    string DisplayName { get; }

    /// <summary>Gets whether a database is nominated and reachable enough to try.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets why no query can run, in terms the workspace owner can act on, or null when
    /// <see cref="IsConfigured"/> is true.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>Runs one validated read-only query.</summary>
    /// <param name="sql">The statement, already accepted by <see cref="SqlQueryGuard"/>.</param>
    /// <param name="parameters">Named parameter values, bound rather than interpolated.</param>
    /// <param name="maxRows">The most rows to return.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The rows the query produced.</returns>
    Task<SqlQueryResult> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        int maxRows,
        CancellationToken cancellationToken = default);
}
