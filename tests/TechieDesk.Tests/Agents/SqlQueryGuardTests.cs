using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 safety boundary — the guard that decides whether an LLM-authored statement may reach
/// a database at all. Everything here is a refusal the product depends on: an agent composes SQL
/// from text it read, and a document can influence that text.
/// </summary>
public class SqlQueryGuardTests
{
    /// <summary>An ordinary parameterized SELECT is accepted.</summary>
    [Fact]
    public void APlainSelectIsAccepted()
    {
        Assert.Null(SqlQueryGuard.Refuse("SELECT Name, Total FROM Invoice WHERE CustomerId = @customerId"));
    }

    /// <summary>A common table expression is a read, so WITH is accepted too.</summary>
    [Fact]
    public void ACommonTableExpressionIsAccepted()
    {
        Assert.Null(SqlQueryGuard.Refuse("WITH Recent AS (SELECT * FROM Invoice) SELECT * FROM Recent"));
    }

    /// <summary>A trailing semicolon is tolerated; it does not stack a second statement.</summary>
    [Fact]
    public void ATrailingSemicolonIsTolerated()
    {
        Assert.Null(SqlQueryGuard.Refuse("SELECT 1;"));
    }

    /// <summary>
    /// Every write, schema and session verb is refused wherever it appears — this is the list that
    /// turns "the agent got it wrong" into "the agent could not have done damage".
    /// </summary>
    [Theory]
    [InlineData("DELETE FROM Invoice")]
    [InlineData("UPDATE Invoice SET Total = 0")]
    [InlineData("INSERT INTO Invoice VALUES (1)")]
    [InlineData("DROP TABLE Invoice")]
    [InlineData("ALTER TABLE Invoice ADD COLUMN X TEXT")]
    [InlineData("CREATE TABLE X (Y TEXT)")]
    [InlineData("ATTACH DATABASE 'other.db' AS other")]
    [InlineData("PRAGMA table_info(Invoice)")]
    [InlineData("VACUUM")]
    [InlineData("SELECT * INTO Copy FROM Invoice")]
    public void AWriteOrSchemaStatementIsRefused(string sql)
    {
        Assert.NotNull(SqlQueryGuard.Refuse(sql));
    }

    /// <summary>
    /// Stacking a destructive statement behind a harmless one is the classic escape, and the
    /// separator check catches it before the verb check ever has to.
    /// </summary>
    [Fact]
    public void AStackedStatementIsRefused()
    {
        var refusal = SqlQueryGuard.Refuse("SELECT 1; DROP TABLE Invoice");

        Assert.Contains("one statement", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Comment syntax is refused outright, because a comment is how a stacked statement is hidden
    /// from a verb scan that only looks at the visible text.
    /// </summary>
    [Theory]
    [InlineData("SELECT 1 -- DROP TABLE Invoice")]
    [InlineData("SELECT /* DELETE */ 1")]
    public void CommentSyntaxIsRefused(string sql)
    {
        Assert.Contains("comments", SqlQueryGuard.Refuse(sql)!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An empty statement is reported rather than sent.</summary>
    [Fact]
    public void AnEmptyStatementIsRefused()
    {
        Assert.NotNull(SqlQueryGuard.Refuse("   "));
    }

    /// <summary>A statement past the length limit is refused before a connection is opened.</summary>
    [Fact]
    public void AnOverlongStatementIsRefused()
    {
        var sql = "SELECT " + new string('x', SqlQueryGuard.MaxStatementLength);

        Assert.Contains("longer than", SqlQueryGuard.Refuse(sql)!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verb list matches whole words only. A schema with an <c>UpdatedOn</c> column or a
    /// <c>DeletionRequest</c> table is ordinary read-only data and must stay queryable, or the
    /// guard would be useless in practice and get switched off.
    /// </summary>
    [Fact]
    public void AColumnNameThatContainsAVerbIsStillQueryable()
    {
        Assert.Null(SqlQueryGuard.Refuse("SELECT UpdatedOn FROM DeletionRequest WHERE Id = @id"));
    }

    /// <summary>A row count beyond the ceiling is clamped rather than honoured.</summary>
    [Fact]
    public void RowCountsAreClampedToTheCeiling()
    {
        Assert.Equal(SqlQueryGuard.RowCeiling, SqlQueryGuard.ClampRows(1000000));
        Assert.Equal(1, SqlQueryGuard.ClampRows(-5));
    }
}
