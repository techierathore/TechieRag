using System.Data.Common;
using System.Globalization;
using System.Text;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The <c>sql-query</c> catalogue skill as a library tool (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>The safety boundary, stated plainly.</b> One read-only <c>SELECT</c>, against one
/// database the operator nominated, which is never the application's own, with values bound as
/// parameters and a hard row cap. <see cref="SqlQueryGuard"/> refuses anything else before a
/// connection is opened, and <see cref="ISqlQueryTarget"/> opens that connection read-only so a
/// statement that somehow got through still cannot write.</para>
/// <para><b>Why the model never supplies the connection.</b> An agent composes SQL from text it
/// read, and a document can influence that text. Letting the tool call name its own target would
/// turn a prompt injection into "read any database this host can reach". The target is
/// configuration; the tool call is only ever the query.</para>
/// <para><b>Unconfigured is the normal state,</b> and it is reported through
/// <see cref="SkillUnavailable"/> rather than as an empty result set — an empty result set is a
/// factual claim about the data, and the tool has no basis to make one.</para>
/// </remarks>
public static class SqlQuerySkill
{
    /// <summary>The JSON Schema for the sql-query tool's parameters.</summary>
    public const string Schema =
        """{"type":"object","properties":{"sql":{"type":"string","description":"A single read-only SELECT statement. Use @name placeholders for values."},"parameters":{"type":"object","description":"Values for the @name placeholders in the statement"},"maxRows":{"type":"integer","description":"How many rows to return, 1 to 1000","default":100}},"required":["sql"]}""";

    /// <summary>The description the model is shown.</summary>
    public const string Description =
        "Runs one read-only SELECT against the workspace's configured reporting database and "
        + "returns the rows. Values must be passed as @name parameters, never pasted into the SQL. "
        + "Writes, schema changes and multiple statements are refused.";

    /// <summary>
    /// Binds the sql-query skill to a database target.
    /// </summary>
    /// <param name="target">
    /// The nominated read-only database, or null when the workspace has nominated none.
    /// </param>
    /// <returns>The skill implementation.</returns>
    public static SkillImplementation Create(ISqlQueryTarget? target) =>
        new(SkillCatalog.SqlQuery, Description, Schema,
            (argumentsJson, cancellationToken) => RunAsync(target, argumentsJson, cancellationToken));

    /// <summary>Runs one query call.</summary>
    /// <param name="target">The database target, or null.</param>
    /// <param name="argumentsJson">The tool-call arguments.</param>
    /// <param name="cancellationToken">Token to cancel the query.</param>
    /// <returns>The formatted rows, a refusal, or an unavailability report.</returns>
    private static async Task<string> RunAsync(
        ISqlQueryTarget? target, string argumentsJson, CancellationToken cancellationToken)
    {
        if (target is null)
        {
            return SkillUnavailable.Because(
                "no database is nominated for this workspace. Point the SQL skill at a read-only "
                + "reporting database in Settings before enabling it.");
        }

        if (!target.IsConfigured)
        {
            return SkillUnavailable.Because(
                target.UnavailableReason ?? "the nominated database cannot be queried.");
        }

        var sql = SkillArguments.ReadString(argumentsJson, "sql");
        var refusal = SqlQueryGuard.Refuse(sql);
        if (refusal is not null)
        {
            return refusal;
        }

        var parameters = SkillArguments.ReadValueMap(argumentsJson, "parameters");
        var maxRows = SqlQueryGuard.ClampRows(SkillArguments.ReadInt(
            argumentsJson, "maxRows", SqlQueryGuard.DefaultMaxRows, 1, SqlQueryGuard.RowCeiling));

        try
        {
            var result = await target
                .ExecuteAsync(sql.Trim(), parameters, maxRows, cancellationToken)
                .ConfigureAwait(false);
            return Format(target.DisplayName, result);
        }
        catch (DbException ex)
        {
            return $"The query failed: {ex.Message}";
        }
    }

    /// <summary>Renders a result set as a compact table the model can read.</summary>
    /// <param name="targetName">What the database is called.</param>
    /// <param name="result">The rows returned.</param>
    /// <returns>The formatted table.</returns>
    private static string Format(string targetName, SqlQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return $"The query ran against {targetName} and returned no rows.";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"{result.Rows.Count} row(s) from {targetName}:");
        text.AppendLine();
        text.Append(string.Join(" | ", result.Columns));

        foreach (var row in result.Rows)
        {
            text.AppendLine();
            text.Append(string.Join(" | ", row.Select(cell => cell ?? "NULL")));
        }

        if (result.IsTruncated)
        {
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture,
                $"[more rows matched than the {result.Rows.Count}-row limit allowed]");
        }

        return text.ToString();
    }
}
