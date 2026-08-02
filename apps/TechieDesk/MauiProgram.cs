using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using TechieDesk.Services;
using TechieDesk.Services.Appearance;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Files;
using TechieDesk.Services.Install;
using TechieDesk.Services.Localization;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Setup;
using TechieDesk.Services.Speech;
using TechieDesk.Services.Support;
using TechieDesk.Services.Threads;
using TechieDesk.Services.Updates;
using TechieDesk.Services.Web;
using TechieDesk.Services.Workspaces;
using TechieDeskDb;
using TechieRag;
using TrBlazeUI.Components.Toast;
using TrBlazeUI.Primitives.Extensions;

namespace TechieDesk;

/// <summary>
/// Composition root for the TechieDesk desktop head (REQ-FN-035, BRD-128).
/// </summary>
/// <remarks>
/// Replaces the retired <c>Program.cs</c> web host. The service graph is carried over intact — what
/// disappeared is everything that existed only to bridge an HTTP boundary: Kestrel, SignalR circuit
/// configuration, HTTPS redirection, antiforgery, static-file middleware, the cookie authentication
/// scheme and the session endpoints. A Razor component now calls its service as an ordinary method
/// on the same thread.
/// <para>
/// DI lifetimes moved from scoped-per-circuit to scoped-per-app. A MAUI <c>BlazorWebView</c> creates
/// one service scope for the lifetime of the window, so registrations left as <c>AddScoped</c>
/// resolve to a single instance per app run; they are kept scoped rather than rewritten to singleton
/// so the components' disposal semantics stay unchanged.
/// </para>
/// </remarks>
public static class MauiProgram
{
    /// <summary>Builds and configures the MAUI application.</summary>
    /// <returns>The configured <see cref="MauiApp"/>.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // The content root locates the SHIPPED, READ-ONLY bundle contents — appsettings.json and the
        // wwwroot assets. Since REQ-FN-037 nothing writable is resolved against it; see below.
        var contentRoot = AppContext.BaseDirectory;

        var environmentName = Environment.GetEnvironmentVariable("TECHIEDESK_ENVIRONMENT");
        var hostEnvironment = new DesktopHostEnvironment("TechieDesk", contentRoot, environmentName);

        LoadConfiguration(builder, contentRoot, hostEnvironment.EnvironmentName);

        // REQ-FN-034: ONE data directory for every persistent artefact, resolved once, here. The app
        // database, the TechieRag store, the vector database, the saved provider config, the logs and
        // the Data Protection key ring all hang off this. Resolving them independently is what let
        // the migrator and the repositories open different files while the boot log reported success.
        // REQ-FN-037: that directory is now the per-user OS location (~/Library/Application Support/
        // TechieDesk, %LOCALAPPDATA%\TechieDesk). It previously resolved against the content root,
        // which for a MAUI head means inside TechieDesk.app/Contents/MonoBundle — writable from bin/,
        // read-only for any signed install.
        var dataDirectory = DataDirectory.ResolveAndCreate(builder.Configuration[DataDirectory.ConfigKey]);

        // An install carrying the old app-relative data/ directory has it moved across once, losing
        // nothing — app DB, RAG store, vector DB, saved provider config AND the key ring, without
        // which every encrypted API key would become unreadable.
        var relocated = DataDirectory.RelocateLegacyDataDirectory(
            DataDirectory.LegacyDataDirectory(contentRoot), dataDirectory);

        // Serilog writes into the same directory, so it must be configured after it is resolved.
        ConfigureSerilog(builder, dataDirectory);

        // Pin the resolved SQLite connection string so the DbUp migrator below and the Dapper
        // repositories bind to the SAME explicit value instead of each falling back to its own
        // default — the REQ-FN-034 defect, kept closed by construction.
        if (string.IsNullOrWhiteSpace(builder.Configuration["AppDb:ConnectionString"]))
        {
            builder.Configuration["AppDb:ConnectionString"] =
                DataDirectory.AppDbConnectionString(dataDirectory);
        }

        Log.Information("TechieDesk data directory: {DataDirectory}", dataDirectory);
        if (relocated.Count > 0)
        {
            Log.Information(
                "Relocated {Count} legacy artefact(s) from the app-relative data directory (REQ-FN-037): {Artefacts}",
                relocated.Count, string.Join(", ", relocated));
        }

        builder.Services.AddSingleton<IHostEnvironment>(hostEnvironment);
        builder.Services.AddSingleton<IAppEnvironment>(new AppEnvironment(contentRoot));

        // [REQ-FN-051] A seat is one user on one INSTALL. Two halves, both local, neither a licence
        // check — an install with no account is never gated by either (BRD-129).
        //   Single-instance guard  — an exclusive lock on the data directory resolved above. It has
        //                            to run HERE, before RunMigrations, because the failure it
        //                            prevents is two processes migrating and writing one SQLite file.
        //                            LSMultipleInstancesProhibited in Info.plist covers the Finder
        //                            case; it does not cover a second COPY of the bundle, which this
        //                            does. A crashed owner's lock is reclaimed, never inherited.
        //   Install identity       — lazy, computed only when something asks (clause 2 is dark by
        //                            default; see LicensingOptions.SendInstallIdentity).
        var launchLoggerFactory = LoggerFactory.Create(logging => logging.AddSerilog(Log.Logger, dispose: false));
        var singleInstance = SingleInstanceGuard.TryAcquire(
            dataDirectory, logger: launchLoggerFactory.CreateLogger("TechieDesk.SingleInstance"));
        builder.Services.AddTechieDeskInstallIdentity(builder.Configuration, singleInstance);

        // REQ-FN-039 (BRD-132): the OS credential store — Keychain here, the DPAPI-backed Credential
        // Manager on Windows — is where AppManager JWT + refresh tokens and provider API keys now
        // live. Registered BEFORE AddTechieDeskDesktopAuth, whose TryAdd fallback is an in-memory
        // store meant only for hosts that have no platform (the test project). Core states the
        // contract; only this head knows the platform.
        builder.Services.AddSingleton<ISecretStore, OsCredentialStore>();

        // REQ-NFR-004b (encryption at rest): provider API keys in techierag-config.json.
        // REQ-FN-039 supersedes this key ring: with a durable OS credential store present, a saved
        // key is written to Keychain and the file holds only an opaque reference — no ciphertext, so
        // no key ring is needed to read it back. The ring is still persisted because it is what
        // decrypts the enc:v1: values an EXISTING install already has on disk; deleting it would
        // make those unreadable. It is legacy-only, not the protection of record.
        var keyRing = Path.Combine(dataDirectory, DataDirectory.KeyRingDirectoryName);
        Directory.CreateDirectory(keyRing);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing))
            .SetApplicationName("TechieDesk");

        RegisterAppServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // REQ-FN-049 — THE LAUNCH PATH. Everything below this line runs on the platform's launch
        // delegate (UIKit's application:willFinishLaunchingWithOptions: on Mac Catalyst), and the
        // window is not presented until it returns. It therefore contains exactly two things, both
        // synchronous end to end, and NOT ONE blocking wait on a task:
        //
        //   RunMigrations       — DbUp, no async anywhere in it. Must precede the window because
        //                         the first render reads the app database and must not race the
        //                         schema; a failure here is deliberately fatal (see the method).
        //   ApplyStoredLanguage — one SQLite scalar read, via the synchronous ILanguageStore.Load.
        //                         Must precede FIRST PAINT, not merely the window: IStringLocalizer
        //                         reads CultureInfo.CurrentUICulture at each lookup, so a culture
        //                         applied later leaves the launch screen in English.
        //
        // Everything else — the TechieRag instance build, the persistence store, the default
        // workspace, the scheduler — is deferred to the thread pool and streams in behind the open
        // window. That work reads techierag-config.json, talks to embedding/vector providers and can
        // block on I/O for an unbounded time; none of it has any business gating a window.
        // [REQ-FN-051] A refused second instance touches NOTHING. It does not migrate the database
        // the live instance is using, does not read a language out of it and starts no background
        // work — it builds the service graph only so App can resolve SingleInstanceState and show a
        // refusal window, then quit. This early return is the whole reason the guard runs above.
        if (!singleInstance.IsPrimaryInstance)
        {
            return app;
        }

        RunMigrations(builder.Configuration);
        ApplyStoredLanguage(app.Services);
        BeginBackgroundInitialization(app.Services);

        return app;
    }

    /// <summary>
    /// Hands the deferrable half of startup to the thread pool and returns at once (REQ-FN-049).
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <remarks>
    /// <para>
    /// This method replaces two <c>GetAwaiter().GetResult()</c> calls that ran on the launch
    /// delegate. The first of them awaited <c>techierag-config.json</c> inside
    /// <c>TechieRagManager</c>, whose continuation was posted back to the very main-thread
    /// <see cref="SynchronizationContext"/> the wait was occupying — so on any install that had ever
    /// saved provider settings the launch delegate never returned, <c>CreateWindow</c> was never
    /// called, and the app presented no windows at all, forever. With no saved config the file check
    /// short-circuited, the await never happened and startup completed, which is why it was invisible
    /// on a fresh machine and why a 1,622-test suite that always ran against an empty data directory
    /// never touched it.
    /// </para>
    /// <para>
    /// The fix is not to annotate the wait — it is to stop waiting. <c>AppStartup.BeginAsync</c> runs
    /// the work on the thread pool, where there is no synchronization context to deadlock against,
    /// and nothing here awaits it. The window opens immediately; the store, the default workspace and
    /// the scheduler arrive behind it.
    /// </para>
    /// <para>
    /// The scheduler starts AFTER that work rather than beside it because priming a schedule needs
    /// the persistence store, and it starts regardless of the outcome: schedules that do not depend
    /// on retrieval must still run on an install whose RAG store failed to come up.
    /// </para>
    /// </remarks>
    private static void BeginBackgroundInitialization(IServiceProvider services)
    {
        var state = services.GetRequiredService<AppStartupState>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TechieDesk.Startup");

        _ = Task.Run(async () =>
        {
            await AppStartup.InitializeAsync(services, state, logger).ConfigureAwait(false);

            if (state.Phase == AppStartupPhase.Failed)
            {
                ReportStartupFailure(state.FailureMessage);
            }

            StartScheduler(services);
        });
    }

    /// <summary>
    /// Tells the user, in the window that is already open, that background startup failed
    /// (REQ-FN-049).
    /// </summary>
    /// <param name="message">The failure text recorded by <see cref="AppStartupState"/>.</param>
    /// <remarks>
    /// <para>
    /// The old behaviour was to log and swallow, which was defensible only because the alternative
    /// then was an app that would not open. Now that the window is already on screen there is
    /// somewhere to say it, and silence would mean a user staring at an empty document library with
    /// no idea why. Serilog still records the full exception; this is the half a user can see.
    /// </para>
    /// <para>
    /// Dispatched to the UI thread and guarded end to end: this runs from a thread-pool
    /// continuation, the window may not have finished presenting, and a failure to report a failure
    /// must never take the app down.
    /// </para>
    /// </remarks>
    private static void ReportStartupFailure(string? message)
    {
        try
        {
            var current = Application.Current;
            if (current is null)
            {
                return;
            }

            current.Dispatcher.Dispatch(async () =>
            {
                try
                {
                    var page = current.Windows.FirstOrDefault()?.Page;
                    if (page is null)
                    {
                        return;
                    }

                    await page.DisplayAlert(
                        "TechieDesk started with limited functionality",
                        "The document store could not be initialized, so retrieval and workspaces "
                        + "may be unavailable. Everything else works, and the full error is in the "
                        + "log folder (Help ▸ Version & Data Folder)."
                        + Environment.NewLine + Environment.NewLine
                        + (message ?? "No further detail was reported."),
                        "OK");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not present the startup-failure notice");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not dispatch the startup-failure notice");
        }
    }

    /// <summary>
    /// Starts the in-app scheduler loop (REQ-FN-042, ADR-009).
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <remarks>
    /// <para>This is the "run while the window is open" half of BRD-139. The other half — running
    /// with the window closed — is the <c>TechieDeskScheduler</c> helper, which hosts this same
    /// <see cref="ISchedulerService"/> against this same data directory. Neither is a fallback for
    /// the other: whichever process is alive runs the schedules, and the per-schedule in-flight guard
    /// is what stops both hosting a run of the same job at once.</para>
    /// <para>Priming happens inside <c>RunAsync</c>, and priming is what makes a run missed while the
    /// app was closed fire at the next launch. A failure here must never block the window from
    /// opening — an app that will not start because a schedule is malformed is a worse outcome than
    /// an app whose schedules are not running.</para>
    /// </remarks>
    private static void StartScheduler(IServiceProvider services)
    {
        try
        {
            var options = services.GetRequiredService<IOptions<SchedulerOptions>>().Value;
            if (!options.RunWhileAppIsOpen)
            {
                Log.Information("In-app scheduler is disabled by configuration");
                return;
            }

            var scheduler = services.GetRequiredService<ISchedulerService>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await scheduler.RunAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "The in-app scheduler loop stopped");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The scheduler could not be started; the app will still open");
        }
    }

    /// <summary>
    /// Loads configuration from the app bundle. Every source is optional: a desktop install must
    /// start and be usable with no configuration file at all (BRD-129 / REQ-FN-036).
    /// </summary>
    /// <param name="builder">The MAUI application builder.</param>
    /// <param name="contentRoot">The application content root.</param>
    /// <param name="environmentName">The resolved environment name.</param>
    private static void LoadConfiguration(
        MauiAppBuilder builder, string contentRoot, string environmentName)
    {
        builder.Configuration
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    }

    /// <summary>
    /// Wires Serilog console + rolling-file logging (REQ-NFR-009), including the unhandled-exception
    /// safety net.
    /// </summary>
    /// <param name="builder">The MAUI application builder.</param>
    /// <param name="dataDirectory">The resolved per-user data directory (REQ-FN-037).</param>
    /// <remarks>
    /// Logs live under the data directory, not beside the executable. Written into the content root
    /// they landed inside the read-only <c>.app</c> bundle, so a signed install would have produced
    /// no diagnostics at all — the one artefact whose absence hides every other failure.
    /// </remarks>
    private static void ConfigureSerilog(MauiAppBuilder builder, string dataDirectory)
    {
        var logDirectory = Path.Combine(dataDirectory, DataDirectory.LogDirectoryName);
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(Path.Combine(logDirectory, "techiedesk-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // REQ-NFR-009: any exception escaping the app is still captured and flushed before exit.
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            Log.Fatal(eventArgs.ExceptionObject as Exception,
                "TechieDesk terminated with an unhandled exception");
            Log.CloseAndFlush();
        };

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);
    }

    /// <summary>Registers the application service graph.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    private static void RegisterAppServices(IServiceCollection services, IConfiguration configuration)
    {
        // REQ-FN-049: how far the deferred half of startup has got. Registered so any surface can
        // render a loading or a failed state instead of the app silently pretending the document
        // store came up. Written from the thread pool, read from the UI — see AppStartupState.
        services.AddSingleton<AppStartupState>();

        // TechieRagManager owns the ITechieRag lifecycle and allows reconfiguration without restart.
        services.AddSingleton<TechieRagManager>();
        services.AddSingleton<ITechieRag>(provider => provider.GetRequiredService<TechieRagManager>());
        services.AddScoped<TechieRagConfigService>();

        // Qdrant administration (F-QADMIN — explicitly retained by the desktop pivot, REQ-FN-040).
        services.AddSingleton<IDockerContainerService, DockerContainerService>();
        services.AddSingleton<IQdrantAdminService, QdrantAdminService>();

        // App data access (REQ-FN-029/BRD-102): Dapper repositories over SQLite. EF Core is banned.
        services.AddTechieDeskData(configuration);

        // AppManager identity + authorization (REQ-FN-001…007). With AppManager:BaseUrl empty the
        // app runs offline as the built-in Admin, which BRD-129 makes the normal case, not a
        // fallback.
        services.AddTechieDeskDesktopAuth(configuration);

        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddTechieDeskLicensing(configuration);

        // Web ingestion — single page, site crawl, YouTube transcript (REQ-RAG-016/017/018).
        services.AddTechieDeskWebIngestion();

        services.AddScoped<ISetupStateService, SetupStateService>();
        services.AddHttpClient<IOllamaProbe, OllamaProbe>();

        // Update check (REQ-FN-038b / BRD-131). Registered BEFORE AddTechieDeskUpdates so its
        // TryAdd leaves this platform provider in place: the assembly-based fallback would report a
        // version the release pipeline never stamps, and every install would think it was 1.0.0.
        services.AddSingleton<IAppVersionProvider, MauiAppVersionProvider>();
        services.AddTechieDeskUpdates(configuration);

        // Scheduling (REQ-FN-042/028/020, REQ-UI-046). Registered here and hosted identically by the
        // TechieDeskScheduler helper — one scheduler, two possible hosts (ADR-009).
        services.AddTechieDeskScheduling(configuration);

        // Connectors (REQ-RAG-019/BRD-63 repositories, REQ-RAG-020/BRD-64 Confluence). Order is
        // load-bearing and not cosmetic: AddTechieDeskConnectorJobs registers its two seams with
        // TryAdd, so the real resolver has to be in the collection first or the build keeps the
        // honest "no connector types are installed" default. Access tokens go to the OS credential
        // store registered above (REQ-FN-039) and never into the application database.
        services.AddTechieDeskConnectors();
        services.AddTechieDeskConnectorJobs();

        // MCP tool servers (REQ-RAG-023 / BRD-86). AFTER AddTechieDeskData, because the registry is
        // Dapper over the app database, and after the OS credential store above, because an MCP
        // server's bearer token and its stdio environment secrets go there and never onto the row
        // (REQ-FN-039). IMcpServerRegistry resolves to the SQLite registry; the library's
        // process-lifetime InMemoryMcpServerRegistry is never registered in this application.
        services.AddTechieDeskMcp();

        // No-code agent flows (REQ-UI-040 / BRD-92), on the REQ-RAG-042 orchestration framework.
        // AFTER AddTechieDeskData (the flow repository is Dapper over the app database) and AFTER
        // AddTechieDeskMcp (a flow's agents get the same MCP-composed tool set a chat turn gets, so
        // a flow cannot be a wider tool path than the chat surface beside it).
        services.AddTechieDeskFlows();

        // Support issues (REQ-UI-032/033/047, REQ-FN-027). Only the attachment staging area needs a
        // registration; the issue calls themselves ride the AppManager typed client above.
        services.AddTechieDeskSupport();

        // Backup and restore (REQ-FN-046/047, BRD-144/145, ADR-013). Registered AFTER the version
        // provider above, whose value it stamps into every archive manifest. It resolves the data
        // directory itself through DataDirectory and takes no credential seam of any kind — the
        // archive must never be able to reach the OS credential store.
        services.AddTechieDeskBackup();

        // Speech (REQ-UI-035 / REQ-UI-036, BRD-87/88). Registered BEFORE AddTechieDeskSpeech so its
        // TryAdd leaves these platform implementations in place — the Core fallbacks report
        // themselves unavailable and would silently disable both controls on a machine that
        // supports them. Same ordering rule as the credential store and the version provider above.
        services.AddSingleton<IDictationService, CatalystDictationService>();
        services.AddSingleton<IReadAloudService, MauiReadAloudService>();
        services.AddTechieDeskSpeech();

        // Thread export (REQ-FN-010 / BRD-35). Same BEFORE-the-TryAdd ordering rule as speech above:
        // the platform save panel has to be in the collection first or AddTechieDeskFileSave's
        // honest "not available on this platform" fallback would win and no export could ever write.
        // MacCatalystFileSaveService is compiled only into the Catalyst head; the Windows head has no
        // Platforms/Windows sources yet (REQ-FN-035) and deliberately resolves the fallback, which
        // reports a failure rather than claiming a save it did not make.
#if MACCATALYST
        services.AddSingleton<IFileSaveService, MacCatalystFileSaveService>();
#endif
        services.AddTechieDeskFileSave();
        services.AddSingleton<ThreadExporter>();
        services.AddSingleton<ThreadExportService>();

        // REQ-UI-037/038/039 (BRD-89/90/91): white-label branding, theme + accent, and .resx
        // localization. The stores are portable and live in Core; the two coordinators are here
        // because they drive the document (JS interop) and the WebView navigation, neither of which
        // exists outside a head.
        services.AddTechieDeskAppearance();
        services.AddScoped<ThemeCoordinator>();
        services.AddScoped<LanguageCoordinator>();

        services.AddTrBlazeUIPrimitives();
        services.AddScoped<ToastService>();
    }

    /// <summary>
    /// Runs the DbUp migrator in-process before the window opens (REQ-FN-030, ADR-007).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when migration fails. This is deliberately fatal: the app must not open against an
    /// un-migrated database, which is how the REQ-FN-034 defect stayed invisible for a week.
    /// </exception>
    private static void RunMigrations(IConfiguration configuration)
    {
        var provider = configuration["AppDb:Provider"] ?? "Sqlite";
        var connectionString = configuration["AppDb:ConnectionString"];

        Log.Information(
            "Applying TechieDesk database migrations (provider: {Provider}, connection: {Connection})",
            provider, connectionString);

        var exitCode = MigrationRunner.Run(provider, connectionString);
        if (exitCode != 0)
        {
            Log.Fatal("Database migration failed with exit code {ExitCode}; aborting startup", exitCode);
            Log.CloseAndFlush();
            throw new InvalidOperationException(
                $"TechieDesk database migration failed (exit code {exitCode}); the app cannot start.");
        }

        Log.Information("Database migrations applied successfully");
    }

    /// <summary>
    /// Applies the stored UI language to the process before the window opens (REQ-UI-039, BRD-91).
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <remarks>
    /// <para>
    /// Runs BEFORE the window rather than from a component. <c>IStringLocalizer</c> reads
    /// <c>CultureInfo.CurrentUICulture</c> at the moment of each lookup, so a culture applied after
    /// the first render would leave the launch screen in English and switch only what happens to be
    /// re-rendered afterwards. It has to be in place before anything renders.
    /// </para>
    /// <para>
    /// Migrations run first because the language lives in the app database and the table has to
    /// exist. A failure here is logged and swallowed: a UI that opens in English is a degraded
    /// experience, whereas an app that will not open because it could not read a language preference
    /// is a defect.
    /// </para>
    /// <para>
    /// REQ-FN-049: this reads through the SYNCHRONOUS <see cref="ILanguageStore.Load"/>. It used to
    /// be <c>store.LoadAsync().GetAwaiter().GetResult()</c> — a sibling of the wait that deadlocked
    /// the app, and safe only by accident, because SQLite has no async file I/O so the await
    /// happened to complete inline and never posted a continuation back to the blocked launch
    /// thread. That is a property of the current driver, not a guarantee, and it is the wrong thing
    /// for the next person to copy. A genuinely synchronous read has nothing to deadlock on.
    /// </para>
    /// </remarks>
    private static void ApplyStoredLanguage(IServiceProvider services)
    {
        try
        {
            using var scope = services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ILanguageStore>();
            var language = store.Load();
            AppCulture.Apply(language);
            Log.Information("UI language set to {Culture} ({Language})",
                language.Culture, language.EnglishName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply the stored UI language; continuing in English");
        }
    }

}
