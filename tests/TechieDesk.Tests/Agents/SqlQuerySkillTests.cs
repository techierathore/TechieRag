using Microsoft.Data.Sqlite;
using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the <c>sql-query</c> skill end to end against a REAL SQLite file, because the two
/// properties that matter most (the connection is genuinely read-only, and values are genuinely
/// bound rather than interpolated) cannot be proved against a fake.
/// </summary>
public sealed class SqlQuerySkillTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "TechieDeskSqlSkill" + Guid.NewGuid().ToString("N"));

    private readonly string databasePath;

    /// <summary>Creates a throwaway reporting database with one table of rows to read.</summary>
    public SqlQuerySkillTests()
    {
        Directory.CreateDirectory(directory);
        databasePath = Path.Combine(directory, "Reporting.db");

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE Invoice (InvoiceId INTEGER PRIMARY KEY, CustomerName TEXT, Total REAL);"
            + "INSERT INTO Invoice VALUES (1, 'Acme', 120.5), (2, 'Globex', 80.0), (3, 'Acme', 40.25);";
        command.ExecuteNonQuery();
    }

    /// <summary>The skill binds to the catalogue name the toggles and the resolver use.</summary>
    [Fact]
    public void BindsToTheCatalogueName()
    {
        Assert.Equal(SkillCatalog.SqlQuery, SqlQuerySkill.Create(null).SkillName);
    }

    /// <summary>With no database nominated the skill reports itself unavailable and says why.</summary>
    [Fact]
    public async Task WithNoTargetItReportsUnavailable()
    {
        var result = await SqlQuerySkill.Create(null)
            .Invoke("""{"sql":"SELECT 1"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("no database is nominated", result, StringComparison.Ordinal);
    }

    /// <summary>A real read-only query against a real file returns the real rows.</summary>
    [Fact]
    public async Task ARealSelectReturnsRealRows()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT CustomerName, Total FROM Invoice ORDER BY InvoiceId"}""",
            CancellationToken.None);

        Assert.Contains("CustomerName | Total", result, StringComparison.Ordinal);
        Assert.Contains("Acme | 120.5", result, StringComparison.Ordinal);
        Assert.Contains("Globex | 80", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Values are bound as parameters, not pasted into the statement. This is the property the
    /// coding standards require and the reason an agent-authored query is safe with user data.
    /// </summary>
    [Fact]
    public async Task ValuesAreBoundAsParameters()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT Total FROM Invoice WHERE CustomerName = @name","parameters":{"name":"Globex"}}""",
            CancellationToken.None);

        Assert.Contains("1 row(s)", result, StringComparison.Ordinal);
        Assert.Contains("80", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// A classic injection payload arriving as a PARAMETER VALUE is data, not syntax: it matches no
    /// customer and changes nothing, which is exactly what binding buys.
    /// </summary>
    [Fact]
    public async Task AnInjectionPayloadInAParameterIsJustData()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT Total FROM Invoice WHERE CustomerName = @name","parameters":{"name":"x'; DROP TABLE Invoice; --"}}""",
            CancellationToken.None);

        Assert.Contains("no rows", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, CountInvoices());
    }

    /// <summary>
    /// The write is refused by the guard before a connection opens, AND the table is still there —
    /// asserting the outcome rather than only the message.
    /// </summary>
    [Fact]
    public async Task AWriteIsRefusedAndTheDataSurvives()
    {
        var result = await Skill().Invoke(
            """{"sql":"DELETE FROM Invoice"}""", CancellationToken.None);

        Assert.StartsWith("Refused:", result, StringComparison.Ordinal);
        Assert.Equal(3, CountInvoices());
    }

    /// <summary>The row cap is applied and the truncation is stated rather than hidden.</summary>
    [Fact]
    public async Task TheRowCapIsAppliedAndReported()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT * FROM Invoice","maxRows":2}""", CancellationToken.None);

        Assert.Contains("2 row(s)", result, StringComparison.Ordinal);
        Assert.Contains("more rows matched", result, StringComparison.Ordinal);
    }

    /// <summary>A query that matches nothing says so; it is not an unavailability report.</summary>
    [Fact]
    public async Task NoRowsIsNotTheSameAsUnavailable()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT * FROM Invoice WHERE Total > 9999"}""", CancellationToken.None);

        Assert.False(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("no rows", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A SQL error becomes a reportable failure the model can correct, not a dead turn.</summary>
    [Fact]
    public async Task ASqlErrorIsReportedNotThrown()
    {
        var result = await Skill().Invoke(
            """{"sql":"SELECT * FROM NoSuchTable"}""", CancellationToken.None);

        Assert.Contains("The query failed", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// The application's own database is off limits, checked at construction so a misconfiguration
    /// fails at startup rather than at the first tool call.
    /// </summary>
    [Fact]
    public async Task TheApplicationsOwnDatabaseIsRefused()
    {
        var target = new SqliteReadOnlySqlQueryTarget(databasePath, "app", [databasePath]);

        Assert.False(target.IsConfigured);

        var result = await SqlQuerySkill.Create(target)
            .Invoke("""{"sql":"SELECT 1"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("application's own", result, StringComparison.Ordinal);
    }

    /// <summary>A nominated file that does not exist is reported, not opened.</summary>
    [Fact]
    public void AMissingDatabaseFileIsReported()
    {
        var target = new SqliteReadOnlySqlQueryTarget(
            Path.Combine(directory, "Absent.db"), "reporting");

        Assert.False(target.IsConfigured);
        Assert.Contains("no database file", target.UnavailableReason!, StringComparison.Ordinal);
    }

    /// <summary>Removes the throwaway database.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds the skill over the throwaway reporting database.</summary>
    /// <returns>The skill implementation.</returns>
    private SkillImplementation Skill() =>
        SqlQuerySkill.Create(new SqliteReadOnlySqlQueryTarget(databasePath, "the reporting database"));

    /// <summary>Counts the rows still in the table, to prove a refusal actually protected data.</summary>
    /// <returns>The row count.</returns>
    private int CountInvoices()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Invoice";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
