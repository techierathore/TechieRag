# Story 5.1: Setup TechieRagWeb Foundation

## Story Information
**Story ID:** STORY-5.1
**Epic:** EPIC-005 - TechieRagWeb Sample Application
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 3

## Description
Setup the foundation for TechieRagWeb including proper Program.cs configuration, layout, navigation, and the TechieRagConfigService.

## Acceptance Criteria
- [ ] Program.cs registers TechieRag services
- [ ] MainLayout.razor has proper navigation
- [ ] NavMenu.razor has links to Settings, Ingestion, Chat
- [ ] TechieRagConfigService.cs manages runtime configuration
- [ ] appsettings.json has default TechieRag configuration
- [ ] Solution builds and runs

## Technical Requirements

### Program.cs
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register TechieRag with default config
builder.Services.AddTechieRag(b => b
    .UseOllama()
    .UseSqliteVec());

// Or from configuration
// builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));

builder.Services.AddScoped<TechieRagConfigService>();

var app = builder.Build();
// ... standard middleware
app.Run();
```

### TechieRagConfigService.cs
```csharp
namespace TechieRagWeb.Services;

public class TechieRagConfigService
{
    private readonly IConfiguration configuration;
    private TechieRagConfig? cachedConfig;

    public async Task<TechieRagConfig> LoadConfigAsync();
    public async Task SaveConfigAsync(TechieRagConfig config);
}
```

### appsettings.json
```json
{
  "TechieRag": {
    "Embedding": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "bge-m3"
    },
    "VectorStore": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=techierag.db"
    },
    "Processing": {
      "DefaultChunkSize": 500,
      "DefaultChunkOverlap": 50
    }
  }
}
```

## Definition of Done
- [ ] App starts without errors
- [ ] Navigation works
- [ ] `dotnet build` passes
