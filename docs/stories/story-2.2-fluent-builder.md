# Story 2.2: Implement Fluent Builder

## Story Information
**Story ID:** STORY-2.2
**Epic:** EPIC-002 - Configuration System
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 5
**Depends On:** STORY-2.1

## User Story
As a developer, I want a fluent builder API so that I can configure TechieRag with IntelliSense support and method chaining.

## Description
Create TechieRagBuilder with fluent methods for configuring embedding providers, vector stores, and processing options. The builder creates a configured ITechieRag instance.

## Acceptance Criteria
- [ ] TechieRagBuilder.cs exists in src/TechieRag/
- [ ] UseOllama(), UseLmStudio(), UseOnnx(), UseAzureOpenAI() methods exist
- [ ] UseSqliteVec(), UsePgVector(), UseQdrant() methods exist
- [ ] WithChunkSize(), WithLogging(), WithTelemetry() methods exist
- [ ] Build() method returns ITechieRag (placeholder implementation OK for now)
- [ ] GetConfig() method returns TechieRagConfig
- [ ] All methods have XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### TechieRagBuilder.cs
Reference the roadmap for complete implementation. Key methods:
- `UseEmbedding(EmbeddingSource, endpoint?, apiKey?, model?, modelPath?)` - full control
- `UseOllama(endpoint?, model?)` - convenience method
- `UseLmStudio(endpoint?)` - convenience method
- `UseOnnx(modelPath)` - convenience method
- `UseAzureOpenAI(endpoint, apiKey, model?)` - convenience method
- `UseVectorStore(VectorStoreType, connectionString)` - full control
- `UseSqliteVec(databasePath?)` - convenience method
- `UsePgVector(connectionString)` - convenience method
- `UseQdrant(endpoint?)` - convenience method
- `WithChunkSize(size, overlap?)` - processing config
- `WithLogging(ILoggerFactory)` - logging config
- `WithTelemetry(enabled?)` - telemetry config
- `Build()` - creates ITechieRag instance
- `GetConfig()` - returns configuration

NOTE: Build() should throw NotImplementedException for now - the actual TechieRagClient implementation comes later.

## Definition of Done
- [ ] Fluent builder created with all methods
- [ ] `dotnet build` passes
- [ ] Method chaining works correctly
