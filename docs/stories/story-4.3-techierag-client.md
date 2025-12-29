# Story 4.3: Implement TechieRagClient

## Story Information
**Story ID:** STORY-4.3
**Epic:** EPIC-004 - Document Processors and Embedding Providers
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 8
**Depends On:** STORY-4.1, STORY-4.2

## User Story
As a developer, I want a complete TechieRag client implementation so that I can ingest documents and perform semantic search.

## Description
Implement the main TechieRagClient class that orchestrates document processing, embedding, and vector storage. Also update TechieRagBuilder.Build() to create actual instances.

## Acceptance Criteria
- [ ] TechieRagClient.cs exists in src/TechieRag/
- [ ] Implements ITechieRag interface completely
- [ ] Coordinates processors, embedding provider, and vector store
- [ ] TechieRagBuilder.Build() creates working TechieRagClient
- [ ] All public methods work correctly
- [ ] Complete XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### TechieRagClient.cs
```csharp
namespace TechieRag;

public class TechieRagClient : ITechieRag
{
    private readonly IVectorStore vectorStore;
    private readonly IEmbeddingProvider embeddingProvider;
    private readonly IEnumerable<IDocumentProcessor> processors;
    private readonly TechieRagConfig config;
    private readonly ILogger<TechieRagClient> logger;

    public TechieRagClient(
        IVectorStore vectorStore,
        IEmbeddingProvider embeddingProvider,
        IEnumerable<IDocumentProcessor> processors,
        TechieRagConfig config,
        ILogger<TechieRagClient> logger)
    {
        // Store dependencies
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await vectorStore.InitializeAsync(ct);
    }

    public async Task<string> IngestAsync(string filePath, CancellationToken ct)
    {
        // 1. Get file extension
        // 2. Find matching processor
        // 3. Process file to get chunks
        // 4. Generate document ID
        // 5. Set DocumentId on chunks
        // 6. Embed all chunks
        // 7. Store in vector store
        // 8. Return document ID
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int topK, string? documentFilter, CancellationToken ct)
    {
        // 1. Embed query
        // 2. Search vector store
        // 3. Return results
    }

    // Implement remaining ITechieRag methods
}
```

### Update TechieRagBuilder
Remove the NotImplementedException from Build() and create actual instances:
```csharp
public ITechieRag Build()
{
    var vectorStore = CreateVectorStore();
    var embeddingProvider = CreateEmbeddingProvider();
    var processors = CreateProcessors();
    var logger = config.LoggerFactory?.CreateLogger<TechieRagClient>()
        ?? NullLogger<TechieRagClient>.Instance;

    return new TechieRagClient(vectorStore, embeddingProvider, processors, config, logger);
}
```

## Definition of Done
- [ ] TechieRagClient fully implemented
- [ ] TechieRagBuilder.Build() works
- [ ] Integration tested with SQLite-vec + Ollama
- [ ] `dotnet build` passes
