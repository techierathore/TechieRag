# Story 2.1: Create Configuration Classes

## Story Information
**Story ID:** STORY-2.1
**Epic:** EPIC-002 - Configuration System
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 3

## User Story
As a developer, I want strongly-typed configuration classes so that I can configure TechieRag through code or configuration files.

## Description
Create all configuration classes including the root TechieRagConfig, EmbeddingConfig, VectorStoreConfig, ProcessingConfig, and supporting enums.

## Acceptance Criteria
- [ ] TechieRagConfig.cs exists in src/TechieRag/
- [ ] Contains EmbeddingConfig, VectorStoreConfig, ProcessingConfig nested or separate classes
- [ ] Contains EmbeddingSource enum (Onnx, Ollama, LmStudio, AzureOpenAI, OpenAI)
- [ ] Contains VectorStoreType enum (SqliteVec, PgVector, Qdrant)
- [ ] All classes have XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### TechieRagConfig.cs
```csharp
namespace TechieRag;

public class TechieRagConfig
{
    public EmbeddingConfig Embedding { get; set; } = new();
    public VectorStoreConfig VectorStore { get; set; } = new();
    public ProcessingConfig Processing { get; set; } = new();
    public bool EnableTelemetry { get; set; } = true;
    internal ILoggerFactory? LoggerFactory { get; set; }
}

public class EmbeddingConfig
{
    public EmbeddingSource Source { get; set; } = EmbeddingSource.Ollama;
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "bge-m3";
    public string? ModelPath { get; set; }
}

public class VectorStoreConfig
{
    public VectorStoreType Type { get; set; } = VectorStoreType.SqliteVec;
    public string ConnectionString { get; set; } = "Data Source=techierag.db";
}

public class ProcessingConfig
{
    public int DefaultChunkSize { get; set; } = 500;
    public int DefaultChunkOverlap { get; set; } = 50;
}

public enum EmbeddingSource { Onnx, Ollama, LmStudio, AzureOpenAI, OpenAI }
public enum VectorStoreType { SqliteVec, PgVector, Qdrant }
```

## Definition of Done
- [ ] All configuration classes created with XML documentation
- [ ] `dotnet build` passes
- [ ] Follows naming conventions (no underscores)
