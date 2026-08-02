using Microsoft.Extensions.Configuration;
using Serilog;

namespace TechieDeskDb;

/// <summary>
/// Entry point for the TechieDeskDb migration console (BRD-103 / REQ-FN-030).
/// Applies versioned, idempotent, journaled DbUp migrations for the TechieDesk
/// app-owned schema and exits non-zero on any failure so container start-up can
/// block the app when the schema is broken. All outcomes are logged via Serilog
/// to the console and a daily rolling file in the data directory's <c>logs/</c>
/// folder (REQ-NFR-009, REQ-FN-048).
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the migrator. Provider and connection string come from
    /// <c>--provider Sqlite --connection "..."</c> command-line switches
    /// or the PascalCase environment variables <c>AppDbProvider</c> /
    /// <c>AppDbConnectionString</c> (read via IConfiguration per coding standards).
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, 1 on migration failure, 2 on invalid configuration.</returns>
    public static int Main(string[] args)
    {
        // REQ-FN-048: configuration is read BEFORE the logger is built, because the log location is
        // part of the data directory and AppDb:DataDirectory can move it. Previously the sink was
        // handed the bare relative "logs/techiedeskdb-.log", so a console run dropped its log files
        // wherever it happened to be invoked from — in practice the repository root, which falsified
        // REQ-FN-034's headline claim that no path resolves against the working directory.
        var configuration = BuildConfiguration(args);
        ConfigureLogging(configuration[DataDirectory.ConfigKey]);

        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            Log.Fatal(eventArgs.ExceptionObject as Exception,
                "TechieDeskDb terminated with an unhandled exception");
            Log.CloseAndFlush();
        };

        try
        {
            var providerName = configuration["AppDbProvider"]
                ?? configuration["AppDb:Provider"]
                ?? "Sqlite";
            var connectionString = configuration["AppDbConnectionString"]
                ?? configuration["AppDb:ConnectionString"];

            Log.Information("TechieDeskDb starting (provider: {Provider})", providerName);
            var exitCode = MigrationRunner.Run(providerName, connectionString);
            Log.Information("TechieDeskDb finished with exit code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "TechieDeskDb failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Points Serilog at the data directory's log folder and returns the file it writes
    /// (REQ-NFR-009, REQ-FN-048).
    /// </summary>
    /// <param name="configuredDataDirectory">
    /// The <c>AppDb:DataDirectory</c> override, or null/blank for the per-user OS location.
    /// </param>
    /// <returns>The absolute rolling-file path handed to the Serilog file sink.</returns>
    /// <remarks>
    /// Public so a test can drive the REAL production call site from a sandbox working directory and
    /// assert that nothing lands there. A guard that builds its own path with <c>Path.Combine</c> and
    /// then asserts the two halves match observes no call site at all and stayed green through both
    /// this defect and REQ-FN-048's vector-store half.
    /// </remarks>
    public static string ConfigureLogging(string? configuredDataDirectory)
    {
        var dataDirectory = DataDirectory.ResolveAndCreate(configuredDataDirectory);
        var logFile = Path.Combine(
            DataDirectory.ResolveAndCreateLogDirectory(dataDirectory), "techiedeskdb-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return logFile;
    }

    private static IConfiguration BuildConfiguration(string[] args)
    {
        var switchMappings = new Dictionary<string, string>
        {
            ["--provider"] = "AppDbProvider",
            ["--connection"] = "AppDbConnectionString"
        };

        return new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args, switchMappings)
            .Build();
    }
}
