using Microsoft.Data.Sqlite;
using TechieDeskDb;

namespace TechieDesk.Services.Scheduling.Jobs;

/// <summary>
/// Compacts the local SQLite databases on a schedule — the "Nightly vector compaction · Maintenance"
/// row in the Automations mockup.
/// </summary>
/// <remarks>
/// <para><b>Why this handler exists at all.</b> The scheduler needs at least one action that is real,
/// self-contained and runnable on any install, or the whole cluster would be untestable end to end
/// until the connector framework lands. Compaction qualifies: it touches only files this app owns, it
/// has a natural per-item shape (one database each) and it is genuinely useful — SQLite does not
/// return deleted pages to the file system without it.</para>
/// <para><b>It is also the worked example of the seam.</b> A connector's handler will look exactly
/// like this one: list items, do the work, report each result through
/// <see cref="IJobProgressReporter"/>, let <see cref="JobRunner"/> decide the outcome.</para>
/// <para><b>A locked database is a per-item failure, not a run failure.</b> The app itself holds the
/// application database open; a VACUUM that cannot get the lock reports that one file as failed and
/// the run continues to the next, which is precisely the BRD-65 shape.</para>
/// </remarks>
public sealed class DatabaseMaintenanceJobHandler : IScheduledJobHandler
{
    /// <summary>The handler key stored on schedules that run maintenance.</summary>
    public const string Kind = "Maintenance";

    private readonly string dataDirectory;
    private readonly ILogger<DatabaseMaintenanceJobHandler> logger;

    /// <summary>Initializes the handler.</summary>
    /// <param name="configuration">Application configuration; used to resolve the data directory.</param>
    /// <param name="logger">Logger.</param>
    public DatabaseMaintenanceJobHandler(
        IConfiguration configuration, ILogger<DatabaseMaintenanceJobHandler> logger)
        : this(DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]), logger)
    {
    }

    /// <summary>Initializes the handler against an explicit directory, for tests.</summary>
    /// <param name="dataDirectory">The directory holding the databases.</param>
    /// <param name="logger">Logger.</param>
    public DatabaseMaintenanceJobHandler(string dataDirectory, ILogger<DatabaseMaintenanceJobHandler> logger)
    {
        this.dataDirectory = dataDirectory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string JobKind => Kind;

    /// <inheritdoc />
    public string DisplayNameKey => "JobKindMaintenanceName";

    /// <inheritdoc />
    public string DescriptionKey => "JobKindMaintenanceDescription";

    /// <inheritdoc />
    public JobMessage DescribeAction(string? payload) => JobMessage.Of("JobKindMaintenanceAction");

    /// <inheritdoc />
    public JobMessage? ValidatePayload(string? payload) =>
        Directory.Exists(dataDirectory)
            ? null
            : JobMessage.Of("MaintenanceDataDirectoryMissingForSave", dataDirectory);

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Directory.Exists(dataDirectory))
        {
            return JobRunResult.FailedWith("MaintenanceDataDirectoryMissing", dataDirectory);
        }

        var databases = Directory.GetFiles(dataDirectory, "*.db");
        context.Progress.Report(
            0,
            databases.Length,
            JobMessage.Of(
                databases.Length == 1 ? "MaintenanceCompactingOne" : "MaintenanceCompactingMany",
                databases.Length));

        var reclaimed = 0L;
        foreach (var path in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(path);
            try
            {
                var before = new FileInfo(path).Length;
                await CompactAsync(path, cancellationToken).ConfigureAwait(false);
                var after = new FileInfo(path).Length;
                reclaimed += Math.Max(0, before - after);

                context.Progress.RecordItem(
                    RunItemStatus.Processed,
                    name,
                    name,
                    JobMessage.Of("MaintenanceItemCompacted", FormatBytes(before), FormatBytes(after)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Named, kept, and not fatal to the run — the whole point of BRD-65's per-item shape.
                logger.LogWarning(exception, "Could not compact {Database}", name);
                // Verbatim: the words are SQLite's, not ours, so there is no code to name them by.
                context.Progress.RecordItem(
                    RunItemStatus.Failed, name, name, JobMessage.Text(exception.Message));
            }
        }

        return new JobRunResult(
            JobMessage
                .Of(
                    databases.Length == 1 ? "MaintenanceDetailOneDatabase" : "MaintenanceDetailDatabases",
                    databases.Length)
                .Then("MaintenanceDetailReclaimed", FormatBytes(reclaimed)));
    }

    private static async Task CompactAsync(string path, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} B"
    };
}
