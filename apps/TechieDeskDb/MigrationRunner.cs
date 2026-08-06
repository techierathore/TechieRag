using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Data.Sqlite;
using Serilog;

namespace TechieDeskDb;

/// <summary>
/// Builds and executes the DbUp upgrade engine against the app database (BRD-103).
/// Scripts are embedded under <c>Scripts/Sqlite</c>, applied in name order,
/// journaled, and therefore idempotent on re-run.
/// </summary>
/// <remarks>
/// SQLite is the only provider. BRD-102 was amended on 2026-07-26 to Dapper-over-SQLite
/// only (REQ-FN-029) and BRD-104/REQ-FN-031 ("per-provider migration scripts") is
/// <c>N/A (removed)</c>, so the PostgreSQL branch and the <c>dbup-postgresql</c>
/// dependency were removed on 2026-07-28. No <c>Scripts/Postgres</c> directory had
/// ever existed, which means that branch would have silently applied ZERO scripts and
/// then reported success — a stale <c>Postgres</c> configuration now fails loudly instead.
/// </remarks>
public static class MigrationRunner
{
    /// <summary>The only provider name accepted by <see cref="Run"/>.</summary>
    private const string SupportedProvider = "Sqlite";

    /// <summary>Default SQLite database file used when no connection string is supplied.</summary>
    /// <remarks>
    /// Resolved through <see cref="DataDirectory"/>, which since REQ-FN-037 means the per-user OS data
    /// directory and NOT the current working directory, the content root, or
    /// <see cref="AppContext.BaseDirectory"/> (REQ-FN-034). The previous CWD-relative default meant a
    /// standalone <c>dotnet run</c> migrated a different file from the one the app opened, while both
    /// reported success. The desktop head pins an explicit connection string derived from the same
    /// resolver, and <c>DataDirectory.Resolve</c> now takes no root argument, so the console migrator
    /// and the running app cannot address different files.
    /// </remarks>
    public static string DefaultSqliteConnectionString =>
        DataDirectory.AppDbConnectionString(DataDirectory.Resolve(configuredDirectory: null));

    /// <summary>
    /// Applies all pending migrations to the SQLite app database.
    /// </summary>
    /// <param name="providerName">Must be <c>Sqlite</c> (case-insensitive); any other value is rejected.</param>
    /// <param name="connectionString">SQLite connection string; optional (defaults to <see cref="DefaultSqliteConnectionString"/>).</param>
    /// <returns>0 on success, 1 on migration failure, 2 on invalid configuration.</returns>
    public static int Run(string providerName, string? connectionString)
    {
        if (!providerName.Equals(SupportedProvider, StringComparison.OrdinalIgnoreCase))
        {
            var removed = providerName.Contains("Postgres", StringComparison.OrdinalIgnoreCase)
                || providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            Log.Error(
                removed
                    ? "AppDb provider {Provider} was removed by the 2026-07-26 BRD-102 amendment (REQ-FN-029); "
                        + "TechieDesk is SQLite-only. Set AppDb:Provider to '{Supported}' (or remove it) and point "
                        + "AppDb:ConnectionString at a SQLite file."
                    : "Unsupported AppDb provider {Provider}; the only supported value is '{Supported}'.",
                providerName, SupportedProvider);
            return 2;
        }

        connectionString = PrepareSqlite(connectionString);

        var engine = BuildEngine(connectionString);
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

    private static UpgradeEngine BuildEngine(string connectionString)
    {
        const string scriptPrefix = "TechieDeskDb.Scripts.Sqlite.";

        return DeployChanges.To.SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                name => name.StartsWith(scriptPrefix, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogTo(new SerilogUpgradeLog())
            .Build();
    }
}
