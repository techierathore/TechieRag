# Story 1.3: Create Core Model Classes

## Story Information
**Story ID:** STORY-1.3
**Epic:** EPIC-001 - Solution Setup and Core Interfaces
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 3
**Depends On:** STORY-1.2

## User Story
As a library consumer, I want well-structured model classes so that I can work with documents, chunks, and search results in a type-safe manner.

## Description
Create all core model classes used throughout the TechieRag library. These models represent the data structures for documents, chunks, search results, and statistics.

## Acceptance Criteria
- [ ] TextChunk.cs exists in src/TechieRag/Models/
- [ ] Document.cs exists in src/TechieRag/Models/
- [ ] SearchResult.cs exists in src/TechieRag/Models/
- [ ] IngestionStats.cs exists in src/TechieRag/Models/
- [ ] All models have complete XML documentation comments
- [ ] All models follow coding standards (PascalCase, no underscores for fields)
- [ ] Solution builds successfully

## Technical Requirements

### TextChunk.cs
```csharp
namespace TechieRag.Models;

public class TextChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string DocumentId { get; set; }
    public required string Text { get; set; }
    public float[]? Vector { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### Document.cs
```csharp
namespace TechieRag.Models;

public class Document
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public int ChunkCount { get; init; }
    public DateTime IngestedAt { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

### SearchResult.cs
```csharp
namespace TechieRag.Models;

public class SearchResult
{
    public required TextChunk Chunk { get; init; }
    public required float Score { get; init; }
}
```

### IngestionStats.cs
```csharp
namespace TechieRag.Models;

public class IngestionStats
{
    public int TotalDocuments { get; init; }
    public int TotalChunks { get; init; }
    public long VectorStoreSizeBytes { get; init; }
    public DateTime? LastIngestionTime { get; init; }
    public string VectorStoreName { get; init; } = string.Empty;
    public string EmbeddingProviderName { get; init; } = string.Empty;
}
```

## Definition of Done
- [ ] All acceptance criteria met
- [ ] All models have XML documentation
- [ ] `dotnet build` passes
- [ ] Code follows naming conventions (no underscores)
- [ ] Private fields use camelCase (no underscore prefix)

## Notes
- Use `required` keyword for properties that must be set
- Use `init` for properties that should be immutable after construction
- Metadata dictionaries should have reasonable defaults (empty dictionary)
