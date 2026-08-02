using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDeskDb;

namespace TechieDesk.Services.Data;

/// <summary>
/// Default <see cref="IAppDbConnectionFactory"/> reading the <c>AppDb</c> options
/// (provider + connection string). SQLite is the ONLY supported provider, with a
/// database file inside the one <see cref="DataDirectory"/> (BRD-102, REQ-FN-037).
/// </summary>
/// <remarks>
/// BRD-102 was amended on 2026-07-26 to Dapper-over-SQLite only; the PostgreSQL
/// alternative is dropped (REQ-FN-029). The provider name is still read from
/// configuration so an installation that still carries <c>AppDb:Provider=Postgres</c>
/// fails loudly at start-up instead of silently opening a SQLite file that nothing
/// migrated for it — a silent fall-through would recreate the REQ-FN-034 class of
/// defect, where two components disagreed about which database the app was using.
/// </remarks>
public sealed class AppDbConnectionFactory : IAppDbConnectionFactory
{
    /// <summary>The only provider name accepted in <c>AppDb:Provider</c>.</summary>
    private const string SupportedProvider = "Sqlite";

    private readonly string connectionString;

    /// <summary>
    /// Initializes the factory from bound <see cref="AppDbOptions"/> and ensures the
    /// SQLite data directory exists.
    /// </summary>
    /// <param name="options">Bound <c>AppDb</c> configuration options.</param>
    /// <exception cref="InvalidOperationException">
    /// <c>AppDb:Provider</c> names anything other than <c>Sqlite</c>, including the
    /// removed <c>Postgres</c> provider.
    /// </exception>
    public AppDbConnectionFactory(IOptions<AppDbOptions> options)
    {
        var value = options.Value;
        RequireSupportedProvider(value.Provider);
        connectionString = PrepareSqlite(value.ConnectionString);
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => new SqliteConnection(connectionString);

    /// <summary>
    /// Throws when the configured provider is anything other than SQLite, naming the
    /// removed PostgreSQL provider explicitly so the message is actionable.
    /// </summary>
    /// <param name="provider">The configured <c>AppDb:Provider</c> value.</param>
    /// <exception cref="InvalidOperationException">The provider is not supported.</exception>
    private static void RequireSupportedProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)
            || provider.Equals(SupportedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var removedHint = provider.Contains("Postgres", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? " The PostgreSQL provider was removed by the 2026-07-26 BRD-102 amendment (REQ-FN-029);"
                    + " TechieDesk is Dapper-over-SQLite only."
                : string.Empty;

        throw new InvalidOperationException(
            $"Unsupported AppDb:Provider '{provider}'. The only supported value is '{SupportedProvider}'."
                + removedHint
                + " Remove the AppDb:Provider setting (or set it to 'Sqlite') and point"
                + " AppDb:ConnectionString at a SQLite file, e.g. 'Data Source=<data-dir>/techiedesk.db'.");
    }

    private static string PrepareSqlite(string? value)
    {
        // REQ-FN-034/REQ-FN-037: this default MUST come from DataDirectory, not from a hand-rolled
        // path. A literal here was a second authority on where the app database lives — exactly the
        // divergence that let DbUp migrate one file while the repositories opened another.
        var effective = string.IsNullOrWhiteSpace(value)
            ? DataDirectory.AppDbConnectionString(DataDirectory.Resolve(configuredDirectory: null))
            : value;
        var dataSource = new SqliteConnectionStringBuilder(effective).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return effective;
    }
}
