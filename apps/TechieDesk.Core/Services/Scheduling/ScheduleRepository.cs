using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Dapper implementation of <see cref="IScheduleRepository"/> (REQ-FN-028, ADR-005).
/// </summary>
/// <remarks>
/// Columns are listed explicitly rather than <c>SELECT *</c> only where the shape differs from the
/// entity; the schedule table maps one-to-one, so the star form is safe here and keeps the SQL short.
/// </remarks>
public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public ScheduleRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Schedule>> ListAsync()
    {
        const string sql = """
            SELECT * FROM "Schedule" ORDER BY "CreatedUtc" DESC;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Schedule>(sql).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<Schedule?> GetAsync(long scheduleId)
    {
        const string sql = """
            SELECT * FROM "Schedule" WHERE "ScheduleId" = @scheduleId;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Schedule>(
            sql, new { scheduleId }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> CreateAsync(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        const string sql = """
            INSERT INTO "Schedule" (
                "Name", "JobKind", "JobPayload", "ActionSummary", "CronExpression", "TimeZoneId",
                "ScheduleText", "SourceInstruction", "IsEnabled", "CatchUpMissedRuns",
                "NotifyOnFailure", "LastRunUtc", "NextRunUtc", "CreatedUtc", "UpdatedUtc")
            VALUES (
                @Name, @JobKind, @JobPayload, @ActionSummary, @CronExpression, @TimeZoneId,
                @ScheduleText, @SourceInstruction, @IsEnabled, @CatchUpMissedRuns,
                @NotifyOnFailure, @LastRunUtc, @NextRunUtc, @CreatedUtc, @UpdatedUtc)
            RETURNING "ScheduleId";
            """;
        using var connection = connectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<long>(sql, schedule).ConfigureAwait(false);
        schedule.ScheduleId = id;
        return id;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        const string sql = """
            UPDATE "Schedule" SET
                "Name" = @Name,
                "JobKind" = @JobKind,
                "JobPayload" = @JobPayload,
                "ActionSummary" = @ActionSummary,
                "CronExpression" = @CronExpression,
                "TimeZoneId" = @TimeZoneId,
                "ScheduleText" = @ScheduleText,
                "SourceInstruction" = @SourceInstruction,
                "IsEnabled" = @IsEnabled,
                "CatchUpMissedRuns" = @CatchUpMissedRuns,
                "NotifyOnFailure" = @NotifyOnFailure,
                "LastRunUtc" = @LastRunUtc,
                "NextRunUtc" = @NextRunUtc,
                "UpdatedUtc" = @UpdatedUtc
            WHERE "ScheduleId" = @ScheduleId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, schedule).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long scheduleId)
    {
        // The run history deliberately survives: ON DELETE SET NULL detaches it rather than erasing
        // the record of what the automation did while it existed.
        const string sql = """
            DELETE FROM "Schedule" WHERE "ScheduleId" = @scheduleId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, new { scheduleId }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(long scheduleId, bool isEnabled, DateTime? nextRunUtc)
    {
        const string sql = """
            UPDATE "Schedule" SET
                "IsEnabled" = @isEnabled,
                "NextRunUtc" = @nextRunUtc,
                "UpdatedUtc" = @updatedUtc
            WHERE "ScheduleId" = @scheduleId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            sql,
            new { scheduleId, isEnabled, nextRunUtc, updatedUtc = DateTime.UtcNow }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Schedule>> ListDueAsync(DateTime asOfUtc)
    {
        const string sql = """
            SELECT * FROM "Schedule"
            WHERE "IsEnabled" = 1 AND "NextRunUtc" IS NOT NULL AND "NextRunUtc" <= @asOfUtc
            ORDER BY "NextRunUtc";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Schedule>(sql, new { asOfUtc }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task RecordRunAsync(long scheduleId, DateTime lastRunUtc, DateTime? nextRunUtc)
    {
        const string sql = """
            UPDATE "Schedule" SET
                "LastRunUtc" = @lastRunUtc,
                "NextRunUtc" = @nextRunUtc
            WHERE "ScheduleId" = @scheduleId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            sql, new { scheduleId, lastRunUtc, nextRunUtc }).ConfigureAwait(false);
    }
}
