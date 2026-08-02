using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Dapper implementation of <see cref="IScheduleRunRepository"/> (BRD-93, BRD-65).
/// </summary>
public sealed class ScheduleRunRepository : IScheduleRunRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public ScheduleRunRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<long> StartAsync(ScheduleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        const string sql = """
            INSERT INTO "ScheduleRun" (
                "ScheduleId", "JobName", "JobKind", "TriggerKind", "StartedUtc", "CompletedUtc",
                "Outcome", "ItemsProcessed", "ItemsFailed", "ItemsSkipped", "Detail", "DetailJson",
                "FailureReason", "FailureReasonJson")
            VALUES (
                @ScheduleId, @JobName, @JobKind, @TriggerKind, @StartedUtc, @CompletedUtc,
                @Outcome, @ItemsProcessed, @ItemsFailed, @ItemsSkipped, @Detail, @DetailJson,
                @FailureReason, @FailureReasonJson)
            RETURNING "ScheduleRunId";
            """;
        using var connection = connectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<long>(sql, ToParameters(run)).ConfigureAwait(false);
        run.ScheduleRunId = id;
        return id;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(ScheduleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        const string sql = """
            UPDATE "ScheduleRun" SET
                "CompletedUtc" = @CompletedUtc,
                "Outcome" = @Outcome,
                "ItemsProcessed" = @ItemsProcessed,
                "ItemsFailed" = @ItemsFailed,
                "ItemsSkipped" = @ItemsSkipped,
                "Detail" = @Detail,
                "DetailJson" = @DetailJson,
                "FailureReason" = @FailureReason,
                "FailureReasonJson" = @FailureReasonJson
            WHERE "ScheduleRunId" = @ScheduleRunId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, ToParameters(run)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddItemsAsync(long scheduleRunId, IReadOnlyList<ScheduleRunItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO "ScheduleRunItem" (
                "ScheduleRunId", "ItemId", "ItemName", "Status", "Reason", "ReasonJson",
                "RecordedUtc")
            VALUES (
                @scheduleRunId, @itemId, @itemName, @status, @reason, @reasonJson, @recordedUtc);
            """;

        var rows = items.Select(item => new
        {
            scheduleRunId,
            itemId = item.ItemId,
            itemName = item.ItemName,
            status = item.Status.ToString(),
            reason = item.Reason,
            reasonJson = item.ReasonJson,
            recordedUtc = item.RecordedUtc == default ? DateTime.UtcNow : item.RecordedUtc
        }).ToList();

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, rows).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRun>> ListRecentAsync(int limit)
    {
        const string sql = """
            SELECT * FROM "ScheduleRun" ORDER BY "StartedUtc" DESC, "ScheduleRunId" DESC LIMIT @limit;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ScheduleRun>(sql, new { limit }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRun>> ListForScheduleAsync(long scheduleId, int limit)
    {
        const string sql = """
            SELECT * FROM "ScheduleRun"
            WHERE "ScheduleId" = @scheduleId
            ORDER BY "StartedUtc" DESC, "ScheduleRunId" DESC
            LIMIT @limit;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ScheduleRun>(
            sql, new { scheduleId, limit }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRunItem>> ListItemsAsync(long scheduleRunId)
    {
        // Failures first: the reason a user opens this list is to find out what went wrong, and
        // making them page past 200 successes to reach it is the same as not recording it.
        const string sql = """
            SELECT * FROM "ScheduleRunItem"
            WHERE "ScheduleRunId" = @scheduleRunId
            ORDER BY CASE "Status" WHEN 'Failed' THEN 0 WHEN 'Skipped' THEN 1 ELSE 2 END,
                     "ScheduleRunItemId";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ScheduleRunItem>(
            sql, new { scheduleRunId }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<int> CloseAbandonedRunsAsync(JobMessage reason, DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(reason);

        const string sql = """
            UPDATE "ScheduleRun" SET
                "Outcome" = 'Failed',
                "CompletedUtc" = @asOfUtc,
                "FailureReason" = @reason,
                "FailureReasonJson" = @reasonJson
            WHERE "Outcome" = 'Running';
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            sql,
            new
            {
                reason = reason.ToInvariantString(),
                reasonJson = reason.ToStorage(),
                asOfUtc
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Projects a run onto its SQL parameters, writing the enums as their names.
    /// </summary>
    /// <param name="run">The run to project.</param>
    /// <returns>The parameter object.</returns>
    /// <remarks>
    /// Dapper would otherwise bind an enum as its integer, and the stored value would stop being
    /// readable the moment a member is inserted in the middle of the enum. The columns are TEXT and
    /// hold names for exactly that reason; <c>CloseAbandonedRunsAsync</c> filters on <c>'Running'</c>
    /// and would break silently under integer storage.
    /// </remarks>
    private static object ToParameters(ScheduleRun run) => new
    {
        run.ScheduleRunId,
        run.ScheduleId,
        run.JobName,
        run.JobKind,
        TriggerKind = run.TriggerKind.ToString(),
        run.StartedUtc,
        run.CompletedUtc,
        Outcome = run.Outcome.ToString(),
        run.ItemsProcessed,
        run.ItemsFailed,
        run.ItemsSkipped,
        run.Detail,
        run.DetailJson,
        run.FailureReason,
        run.FailureReasonJson
    };
}
