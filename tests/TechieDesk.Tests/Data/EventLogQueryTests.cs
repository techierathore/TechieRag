using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using Xunit;

namespace TechieDesk.Tests.Data;

/// <summary>
/// REQ-UI-026 / BRD-73 — the operator event log reads real rows through
/// <see cref="EventLogRepository"/>, so what the screen can show is exactly what these queries
/// return. They run against a temporary SQLite file carrying the shipped schema (0001 plus the
/// 0003 correlation column) rather than an in-memory stand-in, because the filtering, the paging
/// window and the correlation lookup are all SQL, not C#.
/// </summary>
public sealed class EventLogQueryTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-eventlog-{Guid.NewGuid():N}.db");

    /// <summary>Creates the temporary database with the shipped EventLog schema.</summary>
    public EventLogQueryTests()
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

    private static EventLog Event(
        DateTime occurredAt,
        string category,
        string eventName,
        string? detail = null,
        string? source = null,
        string? correlationId = null) => new()
        {
            OccurredAt = occurredAt,
            Category = category,
            Actor = "you",
            EventName = eventName,
            Detail = detail,
            Source = source,
            CorrelationId = correlationId
        };

    /// <summary>
    /// Everything written is readable again, including the two columns the Details view depends on
    /// — the raw payload and the correlation id added by migration 0003.
    /// </summary>
    [Fact]
    public async Task AppendRoundTripsPayloadAndCorrelation()
    {
        var repository = NewRepository();

        var id = await repository.AppendAsync(Event(
            Monday, "Configuration", "Vector store changed to Qdrant",
            detail: """{"setting":"Vector store","from":"SqliteVec","to":"Qdrant"}""",
            source: "admin:settings",
            correlationId: "cfg1"));

        var stored = await repository.GetAsync(id);

        Assert.NotNull(stored);
        Assert.Equal("Configuration", stored!.Category);
        Assert.Equal("cfg1", stored.CorrelationId);
        Assert.Contains("Qdrant", stored.Detail!);
        Assert.Equal("admin:settings", stored.Source);
    }

    /// <summary>An unknown key is absent, not an exception and not someone else's row.</summary>
    [Fact]
    public async Task GetReturnsNullForAnUnknownEvent()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "Login OK"));

        Assert.Null(await repository.GetAsync(9999));
    }

    /// <summary>
    /// The search box matches the event name, the actor, the source and the payload, ignores case,
    /// and does not drag in rows that merely sit nearby.
    /// </summary>
    [Fact]
    public async Task SearchMatchesNameActorSourceAndPayload()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Ingestion", "Embedded SOW-Q3.docx", source: "workspace:Contracts"));
        await repository.AppendAsync(Event(Monday.AddMinutes(1), "Auth", "Login OK", source: "app"));
        await repository.AppendAsync(Event(Monday.AddMinutes(2), "Configuration", "Vector store changed",
            detail: """{"to":"Qdrant"}"""));

        var byName = await repository.QueryAsync(new EventLogFilter { SearchText = "sow-q3" });
        var bySource = await repository.QueryAsync(new EventLogFilter { SearchText = "CONTRACTS" });
        var byPayload = await repository.QueryAsync(new EventLogFilter { SearchText = "qdrant" });
        var noMatch = await repository.QueryAsync(new EventLogFilter { SearchText = "nothing here" });

        Assert.Single(byName);
        Assert.Equal("Embedded SOW-Q3.docx", byName[0].EventName);
        Assert.Single(bySource);
        Assert.Single(byPayload);
        Assert.Equal("Vector store changed", byPayload[0].EventName);
        Assert.Empty(noMatch);
    }

    /// <summary>Whitespace in the search box is not a search for a space, which would match everything.</summary>
    [Fact]
    public async Task BlankSearchTextIsNotAFilter()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "Login OK"));
        await repository.AppendAsync(Event(Monday.AddMinutes(1), "Ingestion", "Embedded a document"));

        var rows = await repository.QueryAsync(new EventLogFilter { SearchText = "   " });

        Assert.Equal(2, rows.Count);
    }

    /// <summary>Category and date bounds narrow the set, and combine rather than override.</summary>
    [Fact]
    public async Task CategoryAndDateRangeNarrowTheResults()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "Login OK"));
        await repository.AppendAsync(Event(Monday.AddDays(1), "Ingestion", "Embedded one"));
        await repository.AppendAsync(Event(Monday.AddDays(2), "Ingestion", "Embedded two"));
        await repository.AppendAsync(Event(Monday.AddDays(5), "Ingestion", "Embedded three"));

        var ingestion = await repository.QueryAsync(new EventLogFilter { Category = "Ingestion" });
        var window = await repository.QueryAsync(new EventLogFilter
        {
            Category = "Ingestion",
            From = Monday.AddDays(1),
            To = Monday.AddDays(2)
        });

        Assert.Equal(3, ingestion.Count);
        Assert.Equal(2, window.Count);
        Assert.All(window, row => Assert.StartsWith("Embedded", row.EventName));
        Assert.DoesNotContain(window, row => row.EventName == "Embedded three");
    }

    /// <summary>Newest first, which is the order the grid presents without re-sorting.</summary>
    [Fact]
    public async Task ResultsComeBackNewestFirst()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "oldest"));
        await repository.AppendAsync(Event(Monday.AddHours(2), "Auth", "newest"));
        await repository.AppendAsync(Event(Monday.AddHours(1), "Auth", "middle"));

        var rows = await repository.QueryAsync(new EventLogFilter());

        Assert.Equal(new[] { "newest", "middle", "oldest" }, rows.Select(row => row.EventName));
    }

    /// <summary>
    /// The paging window moves through the result set without repeating or skipping a row, and the
    /// count behind the footer reports the whole filtered set rather than the page.
    /// </summary>
    [Fact]
    public async Task PagingWindowsTheResultsWhileCountIgnoresIt()
    {
        var repository = NewRepository();
        for (var index = 0; index < 7; index++)
        {
            await repository.AppendAsync(Event(Monday.AddMinutes(index), "Auth", $"event {index}"));
        }

        var firstPage = await repository.QueryAsync(new EventLogFilter { Limit = 3, Offset = 0 });
        var secondPage = await repository.QueryAsync(new EventLogFilter { Limit = 3, Offset = 3 });
        var lastPage = await repository.QueryAsync(new EventLogFilter { Limit = 3, Offset = 6 });
        var total = await repository.CountAsync(new EventLogFilter { Limit = 3, Offset = 0 });

        Assert.Equal(3, firstPage.Count);
        Assert.Equal(3, secondPage.Count);
        Assert.Single(lastPage);
        Assert.Equal(7, total);
        Assert.Equal(new[] { "event 6", "event 5", "event 4" }, firstPage.Select(row => row.EventName));
        Assert.Equal(new[] { "event 3", "event 2", "event 1" }, secondPage.Select(row => row.EventName));
        Assert.Equal("event 0", lastPage[0].EventName);
    }

    /// <summary>The footer count honours the same filters the grid was built with.</summary>
    [Fact]
    public async Task CountHonoursTheFilters()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "Login OK"));
        await repository.AppendAsync(Event(Monday.AddMinutes(1), "Ingestion", "Embedded one"));
        await repository.AppendAsync(Event(Monday.AddMinutes(2), "Ingestion", "Embedded two"));

        Assert.Equal(3, await repository.CountAsync(new EventLogFilter()));
        Assert.Equal(2, await repository.CountAsync(new EventLogFilter { Category = "Ingestion" }));
        Assert.Equal(1, await repository.CountAsync(new EventLogFilter { SearchText = "login" }));
    }

    /// <summary>
    /// The Related events tab reads a job forwards: every sibling of the group, oldest first, and
    /// nothing from any other group.
    /// </summary>
    [Fact]
    public async Task CorrelationLookupReturnsTheGroupOldestFirst()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday.AddSeconds(3), "Ingestion", "third", correlationId: "job1"));
        await repository.AppendAsync(Event(Monday.AddSeconds(1), "Ingestion", "first", correlationId: "job1"));
        await repository.AppendAsync(Event(Monday.AddSeconds(2), "Ingestion", "second", correlationId: "job1"));
        await repository.AppendAsync(Event(Monday.AddSeconds(2), "Ingestion", "other job", correlationId: "job2"));
        await repository.AppendAsync(Event(Monday.AddSeconds(2), "Auth", "uncorrelated"));

        var group = await repository.QueryByCorrelationAsync("job1");

        Assert.Equal(new[] { "first", "second", "third" }, group.Select(row => row.EventName));
    }

    /// <summary>
    /// A blank correlation id is not a group. Matching on it would present every unrelated event in
    /// the database as belonging together.
    /// </summary>
    [Fact]
    public async Task CorrelationLookupRefusesBlankIds()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Auth", "uncorrelated one"));
        await repository.AppendAsync(Event(Monday.AddMinutes(1), "Auth", "uncorrelated two"));

        Assert.Empty(await repository.QueryByCorrelationAsync(null));
        Assert.Empty(await repository.QueryByCorrelationAsync(string.Empty));
        Assert.Empty(await repository.QueryByCorrelationAsync("   "));
    }

    /// <summary>
    /// The category filter offers what the log holds. A fixed list would offer categories that can
    /// never match anything on this install.
    /// </summary>
    [Fact]
    public async Task ListCategoriesReturnsDistinctNamesInOrder()
    {
        var repository = NewRepository();
        await repository.AppendAsync(Event(Monday, "Ingestion", "one"));
        await repository.AppendAsync(Event(Monday.AddMinutes(1), "Auth", "two"));
        await repository.AppendAsync(Event(Monday.AddMinutes(2), "Ingestion", "three"));
        await repository.AppendAsync(Event(Monday.AddMinutes(3), "Configuration", "four"));

        var categories = await repository.ListCategoriesAsync();

        Assert.Equal(new[] { "Auth", "Configuration", "Ingestion" }, categories);
    }
}
