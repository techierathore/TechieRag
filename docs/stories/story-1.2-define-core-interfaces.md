# Story 1.2: Define Core Interfaces

## Story Information
**Story ID:** STORY-1.2
**Epic:** EPIC-001 - Solution Setup and Core Interfaces
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 5
**Depends On:** STORY-1.1

## User Story
As a library consumer, I want well-defined interfaces so that I can understand the API contracts and potentially implement custom providers.

## Description
Create all core abstraction interfaces that define the contracts for TechieRag operations. These interfaces enable the pluggable architecture for vector stores, embedding providers, and document processors.

## Acceptance Criteria
- [ ] ITechieRag.cs exists in src/TechieRag/ with complete API
- [ ] IVectorStore.cs exists in src/TechieRag/Abstractions/
- [ ] IEmbeddingProvider.cs exists in src/TechieRag/Abstractions/
- [ ] IDocumentProcessor.cs exists in src/TechieRag/Abstractions/
- [ ] All interfaces have complete XML documentation comments
- [ ] All interfaces follow coding standards (PascalCase, no underscores)
- [ ] Solution builds successfully

## Technical Requirements

### ITechieRag.cs
Main interface with methods:
- `Task<string> IngestAsync(string filePath, CancellationToken ct)`
- `Task<string> IngestTextAsync(string text, string documentName, Dictionary<string, object>? metadata, CancellationToken ct)`
- `Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern, CancellationToken ct)`
- `Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK, string? documentFilter, CancellationToken ct)`
- `Task DeleteDocumentAsync(string documentId, CancellationToken ct)`
- `Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct)`
- `Task<IngestionStats> GetStatsAsync(CancellationToken ct)`
- `Task ClearAsync(CancellationToken ct)`
- `Task InitializeAsync(CancellationToken ct)`

### IVectorStore.cs
- `string Name { get; }`
- `Task InitializeAsync(CancellationToken ct)`
- `Task<string> UpsertAsync(TextChunk chunk, CancellationToken ct)`
- `Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken ct)`
- `Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK, string? documentFilter, CancellationToken ct)`
- `Task DeleteAsync(string chunkId, CancellationToken ct)`
- `Task DeleteByDocumentAsync(string documentId, CancellationToken ct)`
- `Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct)`
- `Task<IngestionStats> GetStatsAsync(CancellationToken ct)`
- `Task ClearAsync(CancellationToken ct)`

### IEmbeddingProvider.cs
- `string Name { get; }`
- `string ModelName { get; }`
- `int Dimensions { get; }`
- `Task<float[]> EmbedAsync(string text, CancellationToken ct)`
- `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct)`
- `event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted`

### IDocumentProcessor.cs
- `IReadOnlyList<string> SupportedExtensions { get; }`
- `Task<IReadOnlyList<TextChunk>> ProcessAsync(Stream content, string fileName, DocumentProcessingOptions? options, CancellationToken ct)`

### Supporting Classes (in Abstractions/)
- `EmbeddingCompletedEventArgs` - Event args for telemetry
- `DocumentProcessingOptions` - Options for chunking (MaxChunkSize, ChunkOverlap, Language, Metadata)

## Definition of Done
- [ ] All acceptance criteria met
- [ ] All interfaces have XML documentation
- [ ] `dotnet build` passes
- [ ] Code follows naming conventions (no underscores)

## Notes
- Reference the roadmap document for exact interface signatures
- Use `required` keyword for required init properties in event args
- All async methods should have optional CancellationToken with default value
