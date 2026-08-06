using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Settings;
using Xunit;

namespace TechieDesk.Tests.Settings;

/// <summary>
/// REQ-UI-028 / BRD-75 — the App settings upload ceiling is app-owned state in the
/// <c>InstanceSetting</c> table. These tests drive <see cref="AppDefaultsStore"/> against a
/// temporary SQLite file so the round-trip, not just the arithmetic, is covered.
/// </summary>
public sealed class AppDefaultsStoreTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-appdefaults-{Guid.NewGuid():N}.db");

    /// <summary>Creates the temporary database with the shipped InstanceSetting schema.</summary>
    public AppDefaultsStoreTests()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        connection.Execute("""
            CREATE TABLE "InstanceSetting" (
                "SettingKey"   TEXT NOT NULL,
                "SettingValue" TEXT NOT NULL,
                "UpdatedAt"    TEXT NOT NULL,
                CONSTRAINT "PkInstanceSetting" PRIMARY KEY ("SettingKey")
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

    private InstanceSettingRepository NewSettingRepository()
    {
        var options = Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath}"
        });
        return new InstanceSettingRepository(new AppDbConnectionFactory(options));
    }

    private AppDefaultsStore NewStore() => new(NewSettingRepository());

    private void StoreRawValue(string value)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Execute(
            """
            INSERT INTO "InstanceSetting" ("SettingKey", "SettingValue", "UpdatedAt")
            VALUES (@key, @value, @updatedAt);
            """,
            new { key = AppDefaultsStore.MaxUploadSizeKey, value, updatedAt = DateTime.UtcNow });
    }

    /// <summary>A fresh install has never saved this, and gets the shipped ceiling.</summary>
    [Fact]
    public async Task UnsetValueFallsBackToTheShippedDefault()
    {
        var stored = await NewStore().GetMaxUploadSizeMbAsync();

        Assert.Equal(AppDefaultsStore.DefaultMaxUploadSizeMb, stored);
    }

    /// <summary>The value survives being written and read back through a rebuilt store.</summary>
    [Fact]
    public async Task SavedValueRoundTrips()
    {
        await NewStore().SetMaxUploadSizeMbAsync(120);

        Assert.Equal(120, await NewStore().GetMaxUploadSizeMbAsync());
    }

    /// <summary>Saving twice updates the one row rather than accumulating rows.</summary>
    [Fact]
    public async Task SavingTwiceKeepsASingleRow()
    {
        var store = NewStore();
        await store.SetMaxUploadSizeMbAsync(80);
        await store.SetMaxUploadSizeMbAsync(90);

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        var rows = await connection.QuerySingleAsync<int>("""SELECT COUNT(*) FROM "InstanceSetting";""");

        Assert.Equal(1, rows);
        Assert.Equal(90, await store.GetMaxUploadSizeMbAsync());
    }

    /// <summary>A size the app cannot honour is refused at the boundary, not stored and forgotten.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(AppDefaultsStore.MaximumMaxUploadSizeMb + 1)]
    public async Task OutOfRangeValuesAreRefused(int megabytes)
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetMaxUploadSizeMbAsync(megabytes));
    }

    /// <summary>
    /// A row hand-edited to something that is not a usable size falls back to the shipped default.
    /// Trusting it would let a typo in a text field set the app's upload behaviour.
    /// </summary>
    [Theory]
    [InlineData("unlimited")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("999999")]
    public async Task UnusableStoredValuesFallBackToTheDefault(string raw)
    {
        StoreRawValue(raw);

        Assert.Equal(AppDefaultsStore.DefaultMaxUploadSizeMb, await NewStore().GetMaxUploadSizeMbAsync());
    }
}
