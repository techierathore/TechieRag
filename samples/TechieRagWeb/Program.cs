using TechieRag;
using TechieRagWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure detailed logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Set specific log levels for different categories
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("TechieRagWeb", LogLevel.Debug);
builder.Logging.AddFilter("Grpc", LogLevel.Debug);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<TechieRagWeb.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
