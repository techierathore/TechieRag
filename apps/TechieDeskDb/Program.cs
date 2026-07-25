using Microsoft.Extensions.Configuration;
using Serilog;

namespace TechieDeskDb;

/// <summary>
/// Entry point for the TechieDeskDb migration console (BRD-103 / REQ-FN-030).
/// Applies versioned, idempotent, journaled DbUp migrations for the TechieDesk
/// app-owned schema and exits non-zero on any failure so container start-up can
/// block the app when the schema is broken. All outcomes are logged via Serilog
/// to the console and a daily rolling file under logs/ (REQ-NFR-009).
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the migrator. Provider and connection string come from
    /// <c>--provider Sqlite|Postgres --connection "..."</c> command-line switches
    /// or the PascalCase environment variables <c>AppDbProvider</c> /
    /// <c>AppDbConnectionString</c> (read via IConfiguration per coding standards).
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, 1 on migration failure, 2 on invalid configuration.</returns>
    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/techiedeskdb-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            Log.Fatal(eventArgs.ExceptionObject as Exception,
                "TechieDeskDb terminated with an unhandled exception");
            Log.CloseAndFlush();
        };

        try
        {
            var configuration = BuildConfiguration(args);
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
