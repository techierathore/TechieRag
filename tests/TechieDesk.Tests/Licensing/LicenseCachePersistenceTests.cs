using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieDesk.Services.Licensing;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Licensing;

/// <summary>
/// REQ-NFR-010/BRD-101 — the AppManager-outage grace window has to survive a process restart, not
/// just an in-memory cache. These tests drive <see cref="LicenseService"/> against the real
/// Dapper/SQLite <see cref="LicenseCacheRepository"/> on a temporary database file, then rebuild
/// every service on a fresh connection factory to model the restart.
/// <see cref="LicenseServiceTests"/> already covers the grace arithmetic itself with an in-memory
/// repository; what is proven here is the persistence round-trip behind it.
/// </summary>
public sealed class LicenseCachePersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-licensecache-{Guid.NewGuid():N}.db");

    /// <summary>Creates the temporary database with the shipped LicenseCache schema.</summary>
    public LicenseCachePersistenceTests()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        connection.Execute("""
            CREATE TABLE "LicenseCache" (
                "LicenseCacheId" INTEGER PRIMARY KEY AUTOINCREMENT,
                "UserId"         TEXT NOT NULL,
                "PayloadJson"    TEXT NOT NULL,
                "ValidatedAt"    TEXT NOT NULL,
                CONSTRAINT "UcLicenseCacheUserId" UNIQUE ("UserId")
            );
            """);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private LicenseCacheRepository NewRepository()
    {
        var options = Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath}"
        });
        return new LicenseCacheRepository(new AppDbConnectionFactory(options));
    }

    private (LicenseService service, FakeAppManagerClient client) NewService(
        FixedTimeProvider time, int graceHours = 72)
    {
        var client = new FakeAppManagerClient();
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", time.GetUtcNow().AddHours(1));

        var service = new LicenseService(
            client,
            NewRepository(),
            TestFactory.Mode(appManagerEnabled: true),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            Options.Create(new LicensingOptions
            {
                LicenseGraceHours = graceHours,
                LicenseRevalidationMinutes = 60
            }),
            time,
            NullLogger<LicenseService>.Instance);

        return (service, client);
    }

    private static LicenseValidationData ValidLicense() => new()
    {
        IsValid = true,
        License = new ActiveLicenseData
        {
            LicenseId = 1,
            LicenseName = "Professional",
            Status = "Active",
            ExpiryDate = Now.AddDays(200),
            DaysRemaining = 200
        }
    };

    /// <summary>
    /// A live validation writes the last-known-good payload to SQLite, and a completely rebuilt
    /// service (new repository, new connection factory — i.e. a restarted process) still honors it
    /// while AppManager is unreachable.
    /// </summary>
    [Fact]
    public async Task CachedLicenseSurvivesRebuiltServicesWhileAppManagerIsDown()
    {
        var time = new FixedTimeProvider(Now);

        var (online, onlineClient) = NewService(time);
        onlineClient.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());
        var live = await online.ValidateAsync();
        Assert.Equal(LicenseAvailability.Live, live.Availability);

        // The payload really landed in the database file, not just in memory.
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            var rows = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM \"LicenseCache\";");
            Assert.Equal(1, rows);
            var payload = await connection.QuerySingleAsync<string>(
                "SELECT \"PayloadJson\" FROM \"LicenseCache\";");
            var restored = JsonSerializer.Deserialize<LicenseValidationData>(payload, JsonOptions);
            Assert.Equal("Professional", restored!.License!.LicenseName);
        }

        // Restart: brand-new service graph over the same database file, AppManager unreachable.
        time.Advance(TimeSpan.FromHours(2));
        var (restarted, restartedClient) = NewService(time);
        restartedClient.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var cached = await restarted.ValidateAsync();

        Assert.Equal(LicenseAvailability.Cached, cached.Availability);
        Assert.True(cached.IsFromCache);
        Assert.Equal("Professional", cached.LicenseName);
        Assert.Contains("cached license", cached.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Once the persisted validation timestamp is older than the grace window, a restarted
    /// instance degrades instead of silently trusting the stale cache forever.
    /// </summary>
    [Fact]
    public async Task PersistedCacheDegradesAfterTheGraceWindow()
    {
        var time = new FixedTimeProvider(Now);

        var (online, onlineClient) = NewService(time);
        onlineClient.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());
        await online.ValidateAsync();

        time.Advance(TimeSpan.FromHours(73));
        var (restarted, restartedClient) = NewService(time);
        restartedClient.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var expired = await restarted.ValidateAsync();

        Assert.Equal(LicenseAvailability.GraceExpired, expired.Availability);
        Assert.Contains("grace period", expired.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Re-validating overwrites the single per-user row rather than accumulating rows, so the
    /// grace window is always measured from the most recent successful validation.
    /// </summary>
    [Fact]
    public async Task RevalidationUpsertsTheSameRowAndRefreshesTheGraceWindow()
    {
        var time = new FixedTimeProvider(Now);
        var (service, client) = NewService(time);
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());

        await service.ValidateAsync();
        time.Advance(TimeSpan.FromHours(71));
        await service.ValidateAsync();

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        var rows = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM \"LicenseCache\";");
        Assert.Equal(1, rows);

        // Two hours later the cache is only 2h old — well inside the 72h window — because the
        // second validation refreshed ValidatedAt.
        time.Advance(TimeSpan.FromHours(2));
        var (restarted, restartedClient) = NewService(time);
        restartedClient.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var cached = await restarted.ValidateAsync();
        Assert.Equal(LicenseAvailability.Cached, cached.Availability);
    }
}
