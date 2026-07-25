using Serilog;
using Serilog.Events;
using TechieRag;
using TechieDesk.Services;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Setup;
using TechieDesk.Services.Workspaces;
using TechieDeskDb;
using TrBlazeUI.Primitives.Extensions;
using TrBlazeUI.Components.Toast;

// Serilog — structured logging to the console AND a daily rolling file under logs/.
// Everything (this app, the TechieRag library, and providers) flows through this, so
// LLM Save/Test/Reconfigure activity is always visible on-screen and persisted to disk.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/techiedesk-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Global safety net (REQ-NFR-009): any exception that escapes the request/host pipeline
// is still captured and flushed to the console and rolling file before the process dies.
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    Log.Fatal(eventArgs.ExceptionObject as Exception,
        "TechieDesk terminated with an unhandled exception");
    Log.CloseAndFlush();
};

var builder = WebApplication.CreateBuilder(args);

// Route all ASP.NET Core / Microsoft.Extensions.Logging output through Serilog.
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SignalR for larger message sizes (needed for text ingestion with large content)
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB max message size
});

// Register TechieRagManager as singleton - it manages the ITechieRag lifecycle
// and allows dynamic reconfiguration without app restart
builder.Services.AddSingleton<TechieRagManager>();
builder.Services.AddSingleton<ITechieRag>(sp => sp.GetRequiredService<TechieRagManager>());

// Register TechieRagConfigService for runtime configuration management
builder.Services.AddScoped<TechieRagConfigService>();

// Register Qdrant management services
builder.Services.AddSingleton<IDockerContainerService, DockerContainerService>();
builder.Services.AddSingleton<IQdrantAdminService, QdrantAdminService>();

// App data access (REQ-FN-029/BRD-102): Dapper repositories over SQLite/PostgreSQL,
// selected by the AppDb configuration section. Schema is owned by the TechieDeskDb migrator.
builder.Services.AddTechieDeskData(builder.Configuration);

// AppManager auth stack (REQ-FN-001…007): typed AppManager client, RSA password encryption,
// per-circuit server-side tokens, roles/capabilities, and the server-side authorization guard.
// With AppManager:BaseUrl empty the app runs in offline single-user mode as built-in Admin (BRD-54).
builder.Services.AddTechieDeskAuth(builder.Configuration);

// Wave 3 (REQ-UI-014/015, REQ-FN-008/009): the workspace facade over the library
// WorkspaceManager + the app-DB assignment store. Scoped, matching the auth guard / user
// context it depends on.
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Licensing (REQ-FN-013/014/015): license validation + status (POST /LicenseSvc/validate),
// feature gating over FeatureSvc, and the AppManager-outage grace window backed by the Wave 0
// LicenseCacheRepository. Options bind from the AppManager section (AppManager:LicenseGraceHours).
// Live LicenseSvc/FeatureSvc round-trips are pending AppManager UAT; offline mode resolves the
// local Free tier so gating + status are demonstrable without a license server.
builder.Services.AddTechieDeskLicensing(builder.Configuration);

// Wave 5 (REQ-UI-022/023, REQ-FN-016): first-run wizard support — the completion-state store
// (persisted in InstanceSetting) and the graceful local-Ollama probe used to offer discovered
// models. The probe uses a typed HttpClient with a short timeout and never throws on absence.
builder.Services.AddScoped<ISetupStateService, SetupStateService>();
builder.Services.AddHttpClient<IOllamaProbe, OllamaProbe>();

// TrBlazeUI Services
builder.Services.AddTrBlazeUIPrimitives();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

// App-owned schema migration (REQ-FN-030/REQ-FN-031): run the DbUp migrator at startup
// using the same AppDb provider/connection the repositories use. A non-zero result means
// the schema is broken or unreachable, so we throw before configuring the pipeline — the
// app must NOT serve requests against an un-migrated database. Outcomes flow through Serilog.
var appDbProvider = app.Configuration["AppDb:Provider"] ?? "Sqlite";
var appDbConnectionString = app.Configuration["AppDb:ConnectionString"];
Log.Information("Applying TechieDesk database migrations (provider: {Provider})", appDbProvider);
var migrationExitCode = MigrationRunner.Run(appDbProvider, appDbConnectionString);
if (migrationExitCode != 0)
{
    Log.Fatal("Database migration failed with exit code {ExitCode}; aborting startup", migrationExitCode);
    Log.CloseAndFlush();
    throw new InvalidOperationException(
        $"TechieDesk database migration failed (exit code {migrationExitCode}); the app cannot start.");
}

Log.Information("Database migrations applied successfully");

// Wave 3 (REQ-RAG-007/008/028, REQ-FN-009): initialize the TechieRag persistence store so the
// library self-creates its Tr* tables (threads/messages/workspaces), then bootstrap a default
// workspace on first run. This runs AFTER the app-DB migration above. A provider outage must
// NOT block startup — the SQLite store init is local and independent of the LLM/embedding
// providers — so any failure here is logged and the app still serves.
try
{
    using var startupScope = app.Services.CreateScope();
    var ragManager = startupScope.ServiceProvider.GetRequiredService<TechieRagManager>();
    await ragManager.InitializeAsync();
    Log.Information("TechieRag persistence store initialized (threads/workspaces)");

    var workspaceService = startupScope.ServiceProvider.GetRequiredService<IWorkspaceService>();
    var created = await workspaceService.EnsureDefaultWorkspaceAsync(
        TechieDesk.Services.Auth.TechieDeskUser.BuiltInAdmin.UserId.ToString());
    if (created)
    {
        Log.Information("Default workspace bootstrapped on first run (REQ-FN-009)");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "TechieRag persistence init / default-workspace bootstrap failed; the app will still start");
}

// Concise one-line-per-request Serilog logging (replaces the noisy default).
app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseTechieDeskAuth();

app.MapStaticAssets();
app.MapRazorComponents<TechieDesk.Components.App>()
    .AddInteractiveServerRenderMode();

try
{
    Log.Information("Starting TechieDesk host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TechieDesk host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
