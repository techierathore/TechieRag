using Serilog;
using Serilog.Events;
using TechieRag;
using TechieRagWeb.Services;
using TrBlazeUI.Primitives.Extensions;
using TrBlazeUI.Components.Toast;

// Serilog — structured logging to the console AND a daily rolling file under logs/.
// Everything (this app, the TechieRag library, and providers) flows through this, so
// LLM Save/Test/Reconfigure activity is always visible on-screen and persisted to disk.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/techieragweb-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

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

// TrBlazeUI Services
builder.Services.AddTrBlazeUIPrimitives();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

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

app.MapStaticAssets();
app.MapRazorComponents<TechieRagWeb.Components.App>()
    .AddInteractiveServerRenderMode();

try
{
    Log.Information("Starting TechieRagWeb sample host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TechieRagWeb host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
