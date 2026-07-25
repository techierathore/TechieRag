using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Npgsql;

namespace TechieDesk.Services.Data;

/// <summary>
/// Default <see cref="IAppDbConnectionFactory"/> reading the <c>AppDb</c> options
/// (provider + connection string). SQLite is the default provider with a database
/// file under the app's <c>data/</c> directory (BRD-102).
/// </summary>
public sealed class AppDbConnectionFactory : IAppDbConnectionFactory
{
    private readonly string connectionString;

    /// <summary>
    /// Initializes the factory from bound <see cref="AppDbOptions"/> and ensures the
    /// SQLite data directory exists when the SQLite provider is active.
    /// </summary>
    /// <param name="options">Bound <c>AppDb</c> configuration options.</param>
    /// <exception cref="InvalidOperationException">Unknown provider name, or Postgres selected without a connection string.</exception>
    public AppDbConnectionFactory(IOptions<AppDbOptions> options)
    {
        var value = options.Value;
        if (!Enum.TryParse<AppDbProvider>(value.Provider, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException(
                $"Unknown AppDb:Provider '{value.Provider}'; expected Sqlite or Postgres.");
        }

        Provider = parsed;
        connectionString = parsed == AppDbProvider.Postgres
            ? RequirePostgresConnectionString(value.ConnectionString)
            : PrepareSqlite(value.ConnectionString);
    }

    /// <inheritdoc />
    public AppDbProvider Provider { get; }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => Provider == AppDbProvider.Postgres
        ? new NpgsqlConnection(connectionString)
        : new SqliteConnection(connectionString);

    private static string RequirePostgresConnectionString(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("AppDb:ConnectionString is required for the Postgres provider.")
            : value;

    private static string PrepareSqlite(string? value)
    {
        var effective = string.IsNullOrWhiteSpace(value)
            ? $"Data Source={Path.Combine(AppContext.BaseDirectory, "data", "techiedesk.db")}"
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
