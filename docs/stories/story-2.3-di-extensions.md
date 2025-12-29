# Story 2.3: Create DI Extensions

## Story Information
**Story ID:** STORY-2.3
**Epic:** EPIC-002 - Configuration System
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 3
**Depends On:** STORY-2.2

## User Story
As an ASP.NET Core developer, I want extension methods for IServiceCollection so that I can easily register TechieRag services.

## Description
Create ServiceCollectionExtensions with AddTechieRag methods that support both fluent builder configuration and IConfiguration binding.

## Acceptance Criteria
- [ ] ServiceCollectionExtensions.cs exists in src/TechieRag/DependencyInjection/
- [ ] AddTechieRag(Action<TechieRagBuilder>) method exists
- [ ] AddTechieRag(IConfiguration) method exists
- [ ] Methods register ITechieRag as singleton
- [ ] Methods register TechieRagConfig as singleton
- [ ] All methods have XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### ServiceCollectionExtensions.cs
```csharp
namespace TechieRag.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        Action<TechieRagBuilder> configure)
    {
        var builder = new TechieRagBuilder();
        configure(builder);

        services.AddSingleton(builder.GetConfig());
        services.AddSingleton<ITechieRag>(sp =>
        {
            builder.WithLogging(sp.GetRequiredService<ILoggerFactory>());
            return builder.Build();
        });

        return services;
    }

    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind config from IConfiguration and use builder
    }
}
```

Required NuGet packages:
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Configuration.Binder
- Microsoft.Extensions.Logging.Abstractions

## Definition of Done
- [ ] DI extensions created with both overloads
- [ ] `dotnet build` passes
- [ ] NuGet packages added to .csproj
