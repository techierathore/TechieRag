# Story 4.2: Implement Embedding Providers

## Story Information
**Story ID:** STORY-4.2
**Epic:** EPIC-004 - Document Processors and Embedding Providers
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 8

## User Story
As a developer, I want multiple embedding provider options so that I can choose between local and cloud-based embedding services.

## Description
Implement embedding providers for Ollama, LM Studio, Azure OpenAI, and optionally ONNX.

## Acceptance Criteria
- [ ] OllamaEmbeddingProvider.cs - Local Ollama embeddings
- [ ] LmStudioEmbeddingProvider.cs - Local LM Studio embeddings
- [ ] AzureOpenAIEmbeddingProvider.cs - Azure OpenAI embeddings
- [ ] OnnxEmbeddingProvider.cs - Local ONNX model embeddings (basic impl)
- [ ] All providers implement IEmbeddingProvider
- [ ] Telemetry events raised after embedding operations
- [ ] Complete XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### NuGet Packages Required
Add to TechieRag.csproj:
- Azure.AI.OpenAI (2.1.0) - for Azure OpenAI
- Microsoft.ML.OnnxRuntime (1.20.1) - for ONNX (optional)

### OllamaEmbeddingProvider
```csharp
namespace TechieRag.Embedding;

public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    public string Name => "Ollama";
    public string ModelName { get; }
    public int Dimensions => 1024; // BGE-M3

    private readonly HttpClient httpClient;
    private readonly string endpoint;

    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    // POST to /api/embeddings with { "model": "...", "prompt": "..." }
    // Returns { "embedding": [...] }
}
```

### LmStudioEmbeddingProvider
```csharp
// Similar to Ollama but uses OpenAI-compatible API
// POST to /v1/embeddings with { "input": "...", "model": "..." }
```

### AzureOpenAIEmbeddingProvider
```csharp
// Use Azure.AI.OpenAI client
// EmbeddingsClient with endpoint and API key
```

### OnnxEmbeddingProvider (Basic)
```csharp
// Use Microsoft.ML.OnnxRuntime
// Load model from path
// Run inference session
// For now, can be a stub that throws NotImplementedException
```

## Definition of Done
- [ ] All 4 providers implemented (ONNX can be stub)
- [ ] HTTP calls work correctly
- [ ] Telemetry events fire
- [ ] `dotnet build` passes
