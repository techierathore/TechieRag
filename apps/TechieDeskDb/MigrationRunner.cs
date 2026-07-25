using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Data.Sqlite;
using Serilog;

namespace TechieDeskDb;

/// <summary>
/// Builds and executes the DbUp upgrade engine for the selected provider
/// (BRD-103/BRD-104). Scripts are embedded per provider under
/// <c>Scripts/Sqlite</c> and <c>Scripts/Postgres</c>, applied in name order,
/// journaled, and therefore idempotent on re-run.
/// </summary>
public static class MigrationRunner
{
    /// <summary>Default SQLite database file used when no connection string is supplied.</summary>
    public const string DefaultSqliteConnectionString = "Data Source=data/techiedesk.db";

    /// <summary>
    /// Applies all pending migrations for the given provider.
    /// </summary>
    /// <param name="providerName">Either <c>Sqlite</c> or <c>Postgres</c> (case-insensitive).</param>
    /// <param name="connectionString">Provider connection string; optional for SQLite (defaults to <see cref="DefaultSqliteConnectionString"/>).</param>
    /// <returns>0 on success, 1 on migration failure, 2 on invalid configuration.</returns>
    public static int Run(string providerName, string? connectionString)
    {
        var isSqlite = providerName.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
        var isPostgres = providerName.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
        if (!isSqlite && !isPostgres)
        {
            Log.Error("Unknown provider {Provider}; expected Sqlite or Postgres", providerName);
            return 2;
        }

        if (isPostgres && string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Error("A connection string is required for the Postgres provider");
            return 2;
        }

        if (isSqlite)
        {
            connectionString = PrepareSqlite(connectionString);
        }

        var engine = BuildEngine(isSqlite, connectionString!);
        var result = engine.PerformUpgrade();
        if (!result.Successful)
        {
            Log.Error(result.Error, "Migration failed on script {Script}",
                result.ErrorScript?.Name ?? "(unknown)");
            return 1;
        }

        var applied = result.Scripts.Count();
        Log.Information(applied == 0
            ? "Database is already up to date; 0 new scripts applied"
            : "Applied {Count} migration script(s)", applied);
        return 0;
    }

    private static string PrepareSqlite(string? connectionString)
    {
        var effective = string.IsNullOrWhiteSpace(connectionString)
            ? DefaultSqliteConnectionString
            : connectionString;
        var dataSource = new SqliteConnectionStringBuilder(effective).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return effective;
    }

    private static UpgradeEngine BuildEngine(bool isSqlite, string connectionString)
    {
        var scriptPrefix = isSqlite ? "TechieDeskDb.Scripts.Sqlite." : "TechieDeskDb.Scripts.Postgres.";
        var builder = isSqlite
            ? DeployChanges.To.SqliteDatabase(connectionString)
            : DeployChanges.To.PostgresqlDatabase(connectionString);

        return builder
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                name => name.StartsWith(scriptPrefix, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogTo(new SerilogUpgradeLog())
            .Build();
    }
}
