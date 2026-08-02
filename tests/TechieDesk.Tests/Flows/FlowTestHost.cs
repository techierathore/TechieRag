using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Flows;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// A disposable TechieDesk data directory and app database for the flow tests, built by running the
/// SHIPPED DbUp migrations (REQ-UI-040).
/// </summary>
/// <remarks>
/// <para><b>The migration is exercised, not bypassed.</b> Hand-writing <c>CREATE TABLE</c> here would
/// let <c>0008-Flow.sql</c> fail to apply, or drift from the Dapper column names, and every test
/// would still pass — the failure would land on a user's first launch instead.</para>
/// <para><b><see cref="NewRepository"/> models a restart.</b> Every call builds a completely new
/// object graph over the same file: new connection factory, new repository, nothing carried in
/// memory. A test that reused one instance would pass against a dictionary, which is exactly the
/// thing this requirement exists to replace.</para>
/// </remarks>
public sealed class FlowTestHost : IDisposable
{
    private readonly string dataDirectory;

    /// <summary>Creates the data directory, the database, and applies every shipped migration.</summary>
    public FlowTestHost()
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), $"techiedesk-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        DatabasePath = Path.Combine(dataDirectory, "techiedesk.db");
        Assert.Equal(0, MigrationRunner.Run("Sqlite", ConnectionString));
    }

    /// <summary>Gets the app database file path.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the SQLite connection string for the app database.</summary>
    public string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Builds a connection factory over the same database file.</summary>
    /// <returns>A new factory.</returns>
    public IAppDbConnectionFactory NewConnectionFactory() =>
        new AppDbConnectionFactory(Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = ConnectionString
        }));

    /// <summary>Builds a completely fresh repository over the same file, modelling a restart.</summary>
    /// <returns>A new repository.</returns>
    public IFlowRepository NewRepository() => new SqliteFlowRepository(NewConnectionFactory());

    /// <summary>Opens a raw connection, for assertions about what is actually on the row.</summary>
    /// <returns>An open-on-demand SQLite connection.</returns>
    public SqliteConnection OpenConnection() => new(ConnectionString);

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }
}
