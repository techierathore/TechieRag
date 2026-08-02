using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Scheduling;
using TechieDeskDb;
using TechieRag;
using TechieRag.Embedded;

namespace TechieDeskScheduler;

/// <summary>
/// The background scheduler helper: a headless host for the same <see cref="SchedulerService"/> the
/// app window runs (REQ-FN-042 / BRD-139, ADR-009).
/// </summary>
/// <remarks>
/// <para><b>This process is the difference between a schedule and a reminder.</b> A desktop app that
/// only runs schedules while its window is open does not have schedules. Started by a launchd user
/// agent or a Windows per-user logon task, this executable keeps the scheduler alive with the window
/// closed — and does so by loading the same services, opening the same data directory and writing the
/// same run history, so there is exactly one scheduler implementation to keep correct.</para>
/// <para><b>It deliberately does not migrate the database.</b> Migrations belong to the app
/// (ADR-007): a helper that migrated on its own could upgrade a schema out from under an older app
/// binary that is still installed. If the schema is behind, the helper's first query fails, it logs,
/// and it exits rather than guessing.</para>
/// <para><b>No window, no port, no tray by default.</b> BRD-139 rules out a job server, and an
/// always-running tray application that is really the whole app was the rejected alternative in
/// ADR-009.</para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the helper until it is asked to stop.</summary>
    /// <param name="args">Command-line arguments. <c>--once</c> polls a single time and exits.</param>
    /// <returns>0 on a clean exit, 1 when the helper could not start.</returns>
    public static async Task<int> Main(string[] args)
    {
        // The data directory is passed in by the launchd plist / logon task so the helper and the app
        // can never resolve different files — the REQ-FN-034 defect class, which is exactly as
        // available to a second process as it was to a second code path.
        var configuredDirectory =
            Environment.GetEnvironmentVariable(LaunchAgentSchedulerHelper.DataDirectoryEnvironmentVariable);
        var dataDirectory = DataDirectory.ResolveAndCreate(configuredDirectory);

        ConfigureLogging(dataDirectory);
        Log.Information("TechieDesk scheduler helper starting against {DataDirectory}", dataDirectory);
        ReportEmbeddingCapability();

        try
        {
            var configuration = BuildConfiguration(dataDirectory);
            using var provider = BuildServices(configuration);
            var scheduler = provider.GetRequiredService<ISchedulerService>();

            using var stopping = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                RequestStop(stopping);
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestStop(stopping);

            if (args.Contains("--once", StringComparer.OrdinalIgnoreCase))
            {
                // The provable path: prime, poll once, report, exit. Used to demonstrate that a run
                // happens with no app process alive.
                await scheduler.PrimeAsync(stopping.Token).ConfigureAwait(false);
                var runs = await scheduler.PollAsync(stopping.Token).ConfigureAwait(false);
                Log.Information("Single poll complete; {Count} run(s) started", runs.Count);
                foreach (var run in runs)
                {
                    Log.Information(
                        "  {JobName}: {Outcome} — {Detail}", run.JobName, run.Outcome, run.Detail);
                }

                return 0;
            }

            await scheduler.RunAsync(stopping.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Log.Information("TechieDesk scheduler helper stopped");
            return 0;
        }
        catch (Exception exception)
        {
            // A launchd KeepAlive would restart a crash loop forever, so the failure has to be
            // legible in the log the plist points at.
            Log.Fatal(exception, "TechieDesk scheduler helper could not run");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Asks the scheduler loop to stop, tolerating a source already torn down.</summary>
    /// <param name="stopping">The cancellation source.</param>
    /// <remarks>
    /// <c>ProcessExit</c> fires <i>after</i> <c>Main</c> has returned and disposed its
    /// <see cref="CancellationTokenSource"/>, so cancelling it there throws
    /// <see cref="ObjectDisposedException"/> — on a clean exit. Unguarded that surfaces as an
    /// unhandled exception and a non-zero exit code, which launchd's KeepAlive reads as a crash and
    /// answers by restarting the helper. A tidy shutdown would become a restart loop.
    /// </remarks>
    private static void RequestStop(CancellationTokenSource stopping)
    {
        try
        {
            stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already shutting down; there is nothing left to stop.
        }
    }

    /// <summary>
    /// Installs the ONNX native-library resolver and records whether this host can embed
    /// (TR-RAG-025, REQ-FN-042).
    /// </summary>
    /// <remarks>
    /// <para><b>This is the helper's reason for existing, checked out loud.</b> Ingesting with the
    /// window closed means running an ONNX embedding model in a plain <c>net10.0</c> process, which is
    /// precisely what TR-RAG-025 made impossible: <c>Microsoft.ML.OnnxRuntime</c> declares its imports
    /// against the literal name <c>onnxruntime.dll</c>, so on macOS nothing ever found the
    /// <c>libonnxruntime.dylib</c> the package actually ships. Every ingest died on the first call and
    /// the run history said only that the job failed.</para>
    /// <para><b>Run first, and deliberately not lazily.</b> The resolver reaches the process through a
    /// module initializer in <c>TechieRag.Embedded</c>, which fires when that assembly loads — until
    /// now, incidentally, somewhere inside the first embedding call. Calling the probe here makes the
    /// install the first thing the helper does and turns "did ONNX load in this host" into a line in
    /// the log the launchd plist already points at, answerable without reproducing a failed run.</para>
    /// <para><b>A failure is a warning, not an exit.</b> Database maintenance needs no model, so a
    /// host that cannot embed still has jobs worth running; what it must not do is fail them silently.
    /// </para>
    /// </remarks>
    private static void ReportEmbeddingCapability()
    {
        var status = OnnxRuntimeProbe.Check();
        if (status.Loaded)
        {
            // Passed as a property, never as the template: the text is data, and a stray brace in it
            // would otherwise be parsed as a hole and swallow the line.
            Log.Information("{Status}", status.Describe());
            return;
        }

        Log.Warning(
            "{Status} Jobs that need to embed text will fail in this process until it is fixed.",
            status.Describe());
    }

    private static void ConfigureLogging(string dataDirectory)
    {
        var logDirectory = Path.Combine(dataDirectory, DataDirectory.LogDirectoryName);
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "techiedesk-scheduler-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static IConfiguration BuildConfiguration(string dataDirectory) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = dataDirectory,
                ["AppDb:Provider"] = "Sqlite",
                ["AppDb:ConnectionString"] = DataDirectory.AppDbConnectionString(dataDirectory)
            })
            .AddEnvironmentVariables()
            .Build();

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        services.AddTechieDeskData(configuration);
        services.AddTechieDeskScheduling(configuration);
        RegisterConnectors(services, configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Registers the connector cluster in the helper, so a connector schedule fires with the window
    /// closed (REQ-RAG-019 / REQ-RAG-020 on REQ-FN-042, BRD-139).
    /// </summary>
    /// <param name="services">The helper's service collection.</param>
    /// <param name="configuration">The helper's configuration, pinned to the shared data directory.</param>
    /// <remarks>
    /// <para><b>Why it is registered here at all.</b> "Sync my wiki every weekday at 07:00" is the
    /// reason a connector is worth scheduling, and at 07:00 the window is usually shut. Without these
    /// registrations the helper would find no handler for the <c>Connector</c> kind and write a run
    /// row saying so — an honest failure, and a completely useless one. ADR-009's rule is one
    /// scheduler in two possible hosts, and a job kind that only works in one of them breaks it.</para>
    /// <para><b>What it costs, stated rather than hidden.</b> The helper opens the same vector store
    /// and the same application database as the window, so a connector run started here while the app
    /// is also open contends with it on SQLite — the in-flight guard is per process and cannot see
    /// across the two. The narrower and more likely limit is the credential: the OS keychain scopes
    /// entries to the signed application, so a token saved by the app is generally NOT readable from
    /// this separate executable, and such a connector fails here with "re-enter the token" rather than
    /// silently reading its source anonymously. Public, token-free sources sync here without
    /// qualification.</para>
    /// <para><b>Data Protection is registered against the SHARED key ring</b> the app writes, and with
    /// the same application name, so the machine-bound fallback the app uses when the keychain refuses
    /// it (an unsigned build) resolves in this process too. A helper with its own key ring would have
    /// produced the same "cannot decrypt" failure by a different route.</para>
    /// </remarks>
    private static void RegisterConnectors(IServiceCollection services, IConfiguration configuration)
    {
        var dataDirectory = DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]);
        var keyRing = Path.Combine(dataDirectory, DataDirectory.KeyRingDirectoryName);
        Directory.CreateDirectory(keyRing);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing))
            .SetApplicationName("TechieDesk");

        // The RAG instance the ingested documents land in. Same manager, same saved configuration
        // file, same vector store as the window — a second ingestion path here would have been a
        // second set of embedding settings to keep in step.
        services.AddSingleton<IAppEnvironment>(new AppEnvironment(AppContext.BaseDirectory));
        services.AddSingleton<TechieRagManager>();
        services.AddSingleton<ITechieRag>(provider => provider.GetRequiredService<TechieRagManager>());

        // Order is load-bearing: AddTechieDeskConnectorJobs registers its seams with TryAdd, so the
        // real resolver must already be in the collection or the helper keeps the honest
        // "no connector types are installed" default.
        services.AddTechieDeskConnectors();
        services.AddTechieDeskConnectorJobs();
    }
}
