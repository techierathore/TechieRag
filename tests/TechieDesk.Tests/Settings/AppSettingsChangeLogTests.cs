using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Settings;
using TechieDesk.Tests.Support;
using TechieRag;
using Xunit;

namespace TechieDesk.Tests.Settings;

/// <summary>
/// REQ-UI-028 feeding REQ-UI-026 — saving App settings is what puts real, correlated rows into the
/// operator event log. These tests run the change log against the real Dapper repository on a
/// temporary SQLite file and then read the group back the way the event-log Details view does, so
/// the whole loop is covered rather than the diff alone.
/// </summary>
public sealed class AppSettingsChangeLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly AppDefaults Baseline = new(
        LlmSource.LmStudio,
        "qwen2.5-14b",
        EmbeddingSource.Embedded,
        VectorStoreType.SqliteVec,
        50);

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-changelog-{Guid.NewGuid():N}.db");

    /// <summary>Creates the temporary database with the shipped EventLog schema.</summary>
    public AppSettingsChangeLogTests()
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        connection.Execute("""
            CREATE TABLE "EventLog" (
                "EventLogId"    INTEGER PRIMARY KEY AUTOINCREMENT,
                "OccurredAt"    TEXT NOT NULL,
                "Category"      TEXT NOT NULL,
                "Actor"         TEXT NOT NULL,
                "EventName"     TEXT NOT NULL,
                "Detail"        TEXT NULL,
                "Source"        TEXT NULL,
                "CorrelationId" TEXT NULL
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

    private EventLogRepository NewRepository()
    {
        var options = Options.Create(new AppDbOptions
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath}"
        });
        return new EventLogRepository(new AppDbConnectionFactory(options));
    }

    private (AppSettingsChangeLog log, EventLogRepository repository) NewChangeLog()
    {
        var repository = NewRepository();
        return (new AppSettingsChangeLog(repository, new FixedTimeProvider(Noon)), repository);
    }

    /// <summary>A save that altered nothing is not an audit event and writes no row.</summary>
    [Fact]
    public async Task IdenticalSnapshotsRecordNothing()
    {
        var (log, repository) = NewChangeLog();

        var changes = await log.RecordAsync(Baseline, Baseline);

        Assert.Empty(changes);
        Assert.Equal(0, await repository.CountAsync(new EventLogFilter()));
    }

    /// <summary>Only the field that moved is recorded — the other four are not noise in the log.</summary>
    [Fact]
    public async Task OnlyChangedFieldsAreRecorded()
    {
        var (log, repository) = NewChangeLog();

        var changes = await log.RecordAsync(Baseline, Baseline with { VectorStore = VectorStoreType.Qdrant });

        var change = Assert.Single(changes);
        Assert.Equal("Vector store", change.SettingName);
        Assert.Equal("SqliteVec", change.OldValue);
        Assert.Equal("Qdrant", change.NewValue);

        var row = Assert.Single(await repository.QueryAsync(new EventLogFilter()));
        Assert.Equal("Vector store changed to Qdrant", row.EventName);
        Assert.Equal(AppSettingsChangeLog.CategoryName, row.Category);
        Assert.Equal(AppSettingsChangeLog.LocalActor, row.Actor);
        Assert.Equal(AppSettingsChangeLog.SourceName, row.Source);
        Assert.Equal(Noon.UtcDateTime, row.OccurredAt);
    }

    /// <summary>
    /// Everything one save altered shares one correlation id, which is precisely what the event-log
    /// Details view groups on — so the group reads back as the save that produced it.
    /// </summary>
    [Fact]
    public async Task OneSaveBecomesOneCorrelatedGroup()
    {
        var (log, repository) = NewChangeLog();

        var changes = await log.RecordAsync(Baseline, Baseline with
        {
            LlmProvider = LlmSource.Ollama,
            EmbeddingProvider = EmbeddingSource.Ollama,
            MaxUploadSizeMb = 120
        });

        Assert.Equal(3, changes.Count);

        var rows = await repository.QueryAsync(new EventLogFilter());
        var correlationIds = rows.Select(row => row.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);
        Assert.False(string.IsNullOrWhiteSpace(correlationIds[0]));

        var group = await repository.QueryByCorrelationAsync(correlationIds[0]);
        Assert.Equal(3, group.Count);
        Assert.Contains(group, row => row.EventName == "Max upload size changed to 120 MB");
    }

    /// <summary>Two separate saves are two separate groups, not one merged history.</summary>
    [Fact]
    public async Task SeparateSavesGetSeparateGroups()
    {
        var (log, repository) = NewChangeLog();

        var first = Baseline with { MaxUploadSizeMb = 80 };
        await log.RecordAsync(Baseline, first);
        await log.RecordAsync(first, first with { MaxUploadSizeMb = 90 });

        var rows = await repository.QueryAsync(new EventLogFilter());

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.CorrelationId).Distinct().Count());
    }

    /// <summary>
    /// The raw payload behind the row carries both sides of the change, which is what the Details
    /// view's "Raw record" tab shows and the summary line cannot.
    /// </summary>
    [Fact]
    public async Task PayloadCarriesBothSidesOfTheChange()
    {
        var (log, repository) = NewChangeLog();

        await log.RecordAsync(Baseline, Baseline with { LlmModel = "llama3.1" });

        var row = Assert.Single(await repository.QueryAsync(new EventLogFilter()));
        Assert.NotNull(row.Detail);

        using var payload = JsonDocument.Parse(row.Detail!);
        Assert.Equal("Default LLM model", payload.RootElement.GetProperty("setting").GetString());
        Assert.Equal("qwen2.5-14b", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("llama3.1", payload.RootElement.GetProperty("to").GetString());
    }

    /// <summary>The pure comparison is order-stable and covers every field the screen edits.</summary>
    [Fact]
    public void CompareReportsEveryEditableField()
    {
        var changed = new AppDefaults(
            LlmSource.Anthropic,
            "claude",
            EmbeddingSource.Onnx,
            VectorStoreType.Qdrant,
            256);

        var changes = AppSettingsChangeLog.Compare(Baseline, changed);

        Assert.Equal(
            new[] { "Default LLM", "Default LLM model", "Default embeddings", "Vector store", "Max upload size" },
            changes.Select(change => change.SettingName));
    }
}
