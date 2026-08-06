using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Data;

/// <summary>
/// Guards the SQLite-only app-database path (REQ-FN-029, BRD-102 amended 2026-07-26).
/// </summary>
/// <remarks>
/// The PostgreSQL alternative was removed on 2026-07-28. The dangerous failure mode is not a
/// missing driver — it is a stale <c>AppDb:Provider=Postgres</c> setting quietly resolving to a
/// SQLite file nobody migrated, which is the REQ-FN-034 class of defect where two components
/// disagreed about which database was live and both reported success. These tests pin the loud
/// failure instead.
/// </remarks>
public class SqliteOnlyProviderTests
{
    /// <summary>The configured SQLite provider still produces a SQLite connection.</summary>
    [Fact]
    public void SqliteProviderCreatesSqliteConnection()
    {
        var factory = new AppDbConnectionFactory(Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }));

        using IDbConnection connection = factory.CreateConnection();

        Assert.IsType<SqliteConnection>(connection);
    }

    /// <summary>An unset provider defaults to SQLite rather than failing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sqlite")]
    [InlineData("SQLITE")]
    public void BlankOrAnyCaseSqliteIsAccepted(string provider)
    {
        var factory = new AppDbConnectionFactory(Options.Create(new AppDbOptions
        {
            Provider = provider,
            ConnectionString = "Data Source=:memory:"
        }));

        Assert.IsType<SqliteConnection>(factory.CreateConnection());
    }

    /// <summary>
    /// A stale Postgres configuration throws at construction — i.e. at start-up, when the DI
    /// container builds the singleton — and never silently falls through to SQLite.
    /// </summary>
    [Theory]
    [InlineData("Postgres")]
    [InlineData("postgres")]
    [InlineData("PostgreSQL")]
    [InlineData("Npgsql")]
    public void PostgresConfigurationFailsLoudlyWithAnActionableMessage(string provider)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AppDbConnectionFactory(Options.Create(new AppDbOptions
            {
                Provider = provider,
                ConnectionString = "Host=localhost;Database=techiedesk;Username=x;Password=y"
            })));

        Assert.Contains(provider, exception.Message, StringComparison.Ordinal);
        Assert.Contains("REQ-FN-029", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Sqlite", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AppDb:ConnectionString", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Any other unknown provider name is rejected too, not defaulted away.</summary>
    [Fact]
    public void UnknownProviderIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AppDbConnectionFactory(Options.Create(new AppDbOptions { Provider = "SqlServer" })));

        Assert.Contains("SqlServer", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The migrator rejects Postgres with the invalid-configuration exit code (2), which
    /// <c>MauiProgram.RunMigrations</c> turns into a fatal start-up abort. Before removal this
    /// branch built a PostgreSQL engine against a <c>Scripts/Postgres</c> resource prefix that
    /// never existed, so it would have applied zero scripts and logged success.
    /// </summary>
    [Theory]
    [InlineData("Postgres")]
    [InlineData("postgresql")]
    [InlineData("SqlServer")]
    public void MigratorRejectsEveryNonSqliteProvider(string provider)
    {
        Assert.Equal(2, MigrationRunner.Run(provider, "Host=localhost;Database=techiedesk"));
    }

    /// <summary>
    /// The acceptance grep for REQ-FN-029, asserted at runtime: the assembly holding the
    /// repositories references neither EF Core nor the removed Npgsql driver. A grep passes as
    /// soon as a source file is deleted; this fails if either package returns transitively.
    /// </summary>
    [Fact]
    public void CoreAssemblyReferencesNeitherEfCoreNorNpgsql()
    {
        var referenced = typeof(AppDbConnectionFactory).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, name =>
            name.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name =>
            name.Equals("Npgsql", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only SQLite migration scripts are embedded. BRD-104/REQ-FN-031 (per-provider scripts) is
    /// <c>N/A (removed)</c>, so a re-added Postgres script would be dead weight shipped in the
    /// installer.
    /// </summary>
    [Fact]
    public void OnlySqliteMigrationScriptsAreEmbedded()
    {
        var resources = typeof(MigrationRunner).Assembly.GetManifestResourceNames();

        Assert.Contains(resources, name =>
            name.StartsWith("TechieDeskDb.Scripts.Sqlite.", StringComparison.Ordinal));
        Assert.DoesNotContain(resources, name =>
            name.Contains(".Scripts.Postgres.", StringComparison.OrdinalIgnoreCase));
    }
}
