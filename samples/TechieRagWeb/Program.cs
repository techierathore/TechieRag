using TechieRag;
using TechieRagWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register TechieRagManager as singleton - it manages the ITechieRag lifecycle
// and allows dynamic reconfiguration without app restart
builder.Services.AddSingleton<TechieRagManager>();
builder.Services.AddSingleton<ITechieRag>(sp => sp.GetRequiredService<TechieRagManager>());

// Register TechieRagConfigService for runtime configuration management
builder.Services.AddScoped<TechieRagConfigService>();

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
