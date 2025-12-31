# TechieRag User Guide

**Version:** 1.0.0
**Package:** `TechieRag`
**Target Framework:** .NET 10.0+

---

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [Configuration](#configuration)
   - [Embedding Providers](#embedding-providers)
   - [Vector Stores](#vector-stores)
   - [Processing Options](#processing-options)
5. [Core Operations](#core-operations)
   - [Initializing TechieRag](#initializing-techierag)
   - [Ingesting Files](#ingesting-files)
   - [Ingesting Text](#ingesting-text)
   - [Searching Documents](#searching-documents)
   - [Managing Documents](#managing-documents)
6. [Supported Document Types](#supported-document-types)
7. [Advanced Usage](#advanced-usage)
   - [Custom Embedding Provider](#custom-embedding-provider)
   - [Dependency Injection](#dependency-injection)
   - [Logging and Telemetry](#logging-and-telemetry)
8. [API Reference](#api-reference)
9. [Troubleshooting](#troubleshooting)
10. [Best Practices](#best-practices)

---

## Introduction

TechieRag is a flexible, configurable RAG (Retrieval-Augmented Generation) library for .NET. It enables you to build powerful document search and retrieval systems with minimal code.

### Key Features

- **Multiple Embedding Providers**: Connect to Ollama, LM Studio, OpenAI, Azure OpenAI, or any HTTP-compatible embedding API
- **Multiple Vector Stores**: Store vectors locally with SQLite-vec, or scale with PostgreSQL/pgvector or Qdrant
- **Rich Document Support**: Process PDF, DOCX, HTML, Markdown, JSON, TOML, and 70+ code file types
- **Fluent Builder API**: Configure your RAG system with intuitive method chaining
- **Production Ready**: Built-in logging, telemetry, and error handling

### When to Use TechieRag (vs TechieRag.Embedded)

Use **TechieRag** when you:
- Already have an embedding service running (Ollama, LM Studio, OpenAI, etc.)
- Want to use cloud-based embedding APIs
- Need flexibility in choosing embedding models
- Want a smaller package size

Use **TechieRag.Embedded** when you:
- Need completely offline operation
- Don't want to manage external embedding services
- Prefer a zero-configuration setup

---

## Installation

### From GitHub Packages

1. **Create a Personal Access Token (PAT)** with `read:packages` scope:
   - Go to GitHub → Settings → Developer settings → Personal access tokens
   - Generate new token with `read:packages` permission

2. **Add the GitHub Packages source**:

   ```bash
   dotnet nuget add source "https://nuget.pkg.github.com/techierathore/index.json" \
     --name "github-techierathore" \
     --username YOUR_GITHUB_USERNAME \
     --password YOUR_GITHUB_PAT \
     --store-password-in-clear-text
   ```

3. **Install the package**:

   ```bash
   dotnet add package TechieRag --source github-techierathore
   ```

### Using nuget.config

Add to your solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-techierathore" value="https://nuget.pkg.github.com/techierathore/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-techierathore>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="YOUR_GITHUB_PAT" />
    </github-techierathore>
  </packageSourceCredentials>
</configuration>
```

---

## Quick Start

### Minimal Example with Ollama

```csharp
using TechieRag;

// Create and configure TechieRag
var rag = new TechieRagBuilder()
    .UseOllama("http://localhost:11434", "bge-m3")
    .UseSqliteVec("myapp.db")
    .Build();

// Initialize
await rag.InitializeAsync();

// Ingest a document
await rag.IngestAsync("path/to/document.pdf");

// Search
var results = await rag.SearchAsync("What is machine learning?");
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Content: {result.Chunk.Text}");
}
```

---

## Configuration

### Embedding Providers

#### Ollama (Recommended for Local)

```csharp
var rag = new TechieRagBuilder()
    .UseOllama(
        endpoint: "http://localhost:11434",  // Ollama server URL
        model: "bge-m3"                       // Embedding model name
    )
    .UseSqliteVec()
    .Build();
```

**Requirements**: Ollama running locally with the model pulled (`ollama pull bge-m3`)

#### LM Studio

```csharp
var rag = new TechieRagBuilder()
    .UseLmStudio("http://localhost:1234")
    .UseSqliteVec()
    .Build();
```

**Requirements**: LM Studio running with an embedding model loaded

#### OpenAI

```csharp
var rag = new TechieRagBuilder()
    .UseOpenAI(
        apiKey: "sk-your-api-key",
        model: "text-embedding-3-small",      // or "text-embedding-3-large"
        endpoint: "https://api.openai.com"    // Optional, defaults to OpenAI
    )
    .UseSqliteVec()
    .Build();
```

**Requirements**: Valid OpenAI API key

#### Azure OpenAI

```csharp
var rag = new TechieRagBuilder()
    .UseAzureOpenAI(
        endpoint: "https://your-resource.openai.azure.com",
        apiKey: "your-azure-api-key",
        model: "text-embedding-3-small"
    )
    .UseSqliteVec()
    .Build();
```

**Requirements**: Azure OpenAI resource with deployed embedding model

#### Generic HTTP API

For any OpenAI-compatible or custom HTTP embedding API:

```csharp
var rag = new TechieRagBuilder()
    .UseHttp(
        endpoint: "http://your-api.com",
        apiFormat: HttpApiFormat.OpenAI,      // or HttpApiFormat.Ollama, HttpApiFormat.Simple
        model: "your-model-name",
        dimensions: 1024,
        apiPath: "/v1/embeddings",            // Optional
        apiKey: "your-api-key",               // Optional
        requestDelayMs: 100                   // Rate limiting
    )
    .UseSqliteVec()
    .Build();
```

#### ONNX Model

```csharp
var rag = new TechieRagBuilder()
    .UseOnnx("path/to/model.onnx")
    .UseSqliteVec()
    .Build();
```

**Requirements**: ONNX model file compatible with sentence embeddings

### Vector Stores

#### SQLite-vec (Local, Zero-Config)

```csharp
// Default database file
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()  // Uses "techierag.db"
    .Build();

// Custom database path
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec("myapp.db")
    .Build();
```

**Best for**: Development, small-medium datasets, single-machine deployments

#### PostgreSQL with pgvector

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UsePgVector("Host=localhost;Database=mydb;Username=user;Password=pass")
    .Build();
```

**Requirements**: PostgreSQL with pgvector extension installed

**Best for**: Production environments, larger datasets, multi-user access

#### Qdrant

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseQdrant(
        endpoint: "http://localhost:6334",
        apiKey: "your-qdrant-api-key"         // Optional
    )
    .Build();
```

**Requirements**: Qdrant server running

**Best for**: High-performance vector search, cloud deployments, large-scale RAG

### Processing Options

#### Chunk Size Configuration

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .WithChunkSize(
        size: 500,      // Characters per chunk
        overlap: 50     // Overlap between chunks for context continuity
    )
    .Build();
```

**Guidelines**:
- Smaller chunks (300-500): Better for precise retrieval, Q&A systems
- Larger chunks (800-1500): Better for context-heavy applications
- Overlap: 10-20% of chunk size recommended

---

## Core Operations

### Initializing TechieRag

Always call `InitializeAsync()` before using the RAG system:

```csharp
await rag.InitializeAsync();
```

This:
- Creates database tables/collections if needed
- Validates the embedding provider connection
- Prepares the system for ingestion and search

### Ingesting Files

#### Single File

```csharp
string documentId = await rag.IngestAsync("path/to/document.pdf");
Console.WriteLine($"Ingested document with ID: {documentId}");
```

#### Directory with Pattern

```csharp
// All files
IReadOnlyList<string> documentIds = await rag.IngestDirectoryAsync(
    directoryPath: "./documents",
    searchPattern: "*.*"
);

// Only PDFs
var pdfIds = await rag.IngestDirectoryAsync("./documents", "*.pdf");

// Only Markdown files
var mdIds = await rag.IngestDirectoryAsync("./documents", "*.md");
```

### Ingesting Text

Ingest raw text content directly without saving to files:

```csharp
// Basic usage
string documentId = await rag.IngestTextAsync(
    text: "Your article content here...",
    documentName: "my-article"
);

// With metadata
string documentId = await rag.IngestTextAsync(
    text: articleContent,
    documentName: "article-123",
    metadata: new Dictionary<string, object>
    {
        { "Source", "PostgreSQL" },
        { "ArticleId", 123 },
        { "Author", "John Doe" },
        { "Category", "Technology" },
        { "PublishedDate", DateTime.UtcNow }
    }
);
```

**Use Cases**:
- Ingesting content from databases
- Processing API responses (news feeds, blog posts)
- User-generated content
- Real-time content ingestion

### Searching Documents

#### Basic Search

```csharp
var results = await rag.SearchAsync("What is machine learning?");

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Document: {result.Chunk.DocumentId}");
    Console.WriteLine($"Content: {result.Chunk.Text}");
    Console.WriteLine("---");
}
```

#### Search with Options

```csharp
var results = await rag.SearchAsync(
    query: "machine learning algorithms",
    topK: 10,                              // Return top 10 results
    documentFilter: "specific-doc-id"      // Filter to specific document
);
```

### Managing Documents

#### List All Documents

```csharp
var documents = await rag.ListDocumentsAsync();

foreach (var doc in documents)
{
    Console.WriteLine($"ID: {doc.Id}");
    Console.WriteLine($"Name: {doc.Name}");
    Console.WriteLine($"Chunks: {doc.ChunkCount}");
    Console.WriteLine($"Ingested: {doc.IngestedAt}");
}
```

#### Get Statistics

```csharp
var stats = await rag.GetStatsAsync();

Console.WriteLine($"Total Documents: {stats.TotalDocuments}");
Console.WriteLine($"Total Chunks: {stats.TotalChunks}");
Console.WriteLine($"Storage Size: {stats.VectorStoreSizeBytes / 1024 / 1024} MB");
Console.WriteLine($"Vector Store: {stats.VectorStoreName}");
Console.WriteLine($"Embedding Provider: {stats.EmbeddingProviderName}");
```

#### Delete a Document

```csharp
await rag.DeleteDocumentAsync("document-id");
```

#### Clear All Data

```csharp
await rag.ClearAsync();
```

---

## Supported Document Types

| Type | Extensions | Description |
|------|------------|-------------|
| PDF | `.pdf` | Extracts text from PDF documents |
| Word | `.docx` | Microsoft Word documents |
| HTML | `.html`, `.htm` | Web pages, strips tags |
| Markdown | `.md`, `.markdown` | Preserves structure |
| JSON | `.json` | Parses and extracts text |
| TOML | `.toml` | Configuration files |
| Plain Text | `.txt` | Direct text processing |
| Code | 70+ extensions | See below |

### Supported Code File Types

`.cs`, `.py`, `.js`, `.ts`, `.jsx`, `.tsx`, `.java`, `.go`, `.rs`, `.rb`, `.php`, `.swift`, `.kt`, `.scala`, `.c`, `.cpp`, `.h`, `.hpp`, `.m`, `.mm`, `.sql`, `.sh`, `.bash`, `.ps1`, `.yaml`, `.yml`, `.xml`, `.css`, `.scss`, `.less`, `.vue`, `.svelte`, `.dart`, `.lua`, `.r`, `.jl`, `.hs`, `.ex`, `.exs`, `.clj`, `.fs`, `.ml`, `.pl`, `.pm`, `.tcl`, `.awk`, `.sed`, `.vim`, `.el`, `.lisp`, `.scm`, `.rkt`, `.erl`, `.hrl`, and more...

---

## Advanced Usage

### Custom Embedding Provider

Implement `IEmbeddingProvider` for custom embedding sources:

```csharp
public class MyCustomEmbeddingProvider : IEmbeddingProvider
{
    public string Name => "MyCustomProvider";
    public string ModelName => "custom-model";
    public int Dimensions => 1024;

    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Your embedding logic
        return await GetEmbeddingFromMyService(text);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        // Batch embedding logic
        var embeddings = new List<float[]>();
        foreach (var text in texts)
        {
            embeddings.Add(await EmbedAsync(text, ct));
        }
        return embeddings;
    }
}

// Usage
var rag = new TechieRagBuilder()
    .UseCustomEmbeddingProvider(() => new MyCustomEmbeddingProvider())
    .UseSqliteVec()
    .Build();
```

### Dependency Injection

#### ASP.NET Core / Blazor

```csharp
// Program.cs
builder.Services.AddSingleton<ITechieRag>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    return new TechieRagBuilder()
        .UseOllama("http://localhost:11434", "bge-m3")
        .UseSqliteVec("app.db")
        .WithLogging(loggerFactory)
        .Build();
});

// Initialize on startup
var app = builder.Build();
var rag = app.Services.GetRequiredService<ITechieRag>();
await rag.InitializeAsync();
```

#### Using in Controllers/Services

```csharp
public class SearchController : ControllerBase
{
    private readonly ITechieRag _rag;

    public SearchController(ITechieRag rag)
    {
        _rag = rag;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string query)
    {
        var results = await _rag.SearchAsync(query);
        return Ok(results);
    }
}
```

### Logging and Telemetry

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .WithLogging(loggerFactory)
    .WithTelemetry(true)
    .Build();
```

---

## API Reference

### ITechieRag Interface

```csharp
public interface ITechieRag
{
    /// <summary>
    /// Initialize the RAG system. Must be called before any operations.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingest a single file into the vector store.
    /// </summary>
    /// <param name="filePath">Path to the file to ingest</param>
    /// <returns>The document ID</returns>
    Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingest raw text content directly into the vector store.
    /// </summary>
    /// <param name="text">The text content to ingest</param>
    /// <param name="documentName">A unique name for this document</param>
    /// <param name="metadata">Optional metadata to attach</param>
    /// <returns>The document ID</returns>
    Task<string> IngestTextAsync(
        string text,
        string documentName,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingest all matching files in a directory.
    /// </summary>
    /// <param name="directoryPath">Directory to scan</param>
    /// <param name="searchPattern">File pattern (e.g., "*.pdf", "*.*")</param>
    /// <returns>List of ingested document IDs</returns>
    Task<IReadOnlyList<string>> IngestDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.*",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for relevant document chunks.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <param name="topK">Number of results to return</param>
    /// <param name="documentFilter">Optional document ID to filter results</param>
    /// <returns>Ranked search results</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a document and all its chunks.
    /// </summary>
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all ingested documents.
    /// </summary>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get ingestion statistics.
    /// </summary>
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all data from the vector store.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
```

### Data Models

#### SearchResult

```csharp
public class SearchResult
{
    public TextChunk Chunk { get; init; }    // The matched chunk
    public float Score { get; init; }         // Similarity score (0-1)
}
```

#### TextChunk

```csharp
public class TextChunk
{
    public string Id { get; set; }
    public string DocumentId { get; set; }
    public string Text { get; set; }
    public float[]? Vector { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

#### Document

```csharp
public class Document
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string SourcePath { get; init; }
    public int ChunkCount { get; init; }
    public DateTime IngestedAt { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
}
```

#### IngestionStats

```csharp
public class IngestionStats
{
    public int TotalDocuments { get; init; }
    public int TotalChunks { get; init; }
    public long VectorStoreSizeBytes { get; init; }
    public DateTime? LastIngestionTime { get; init; }
    public string VectorStoreName { get; init; }
    public string EmbeddingProviderName { get; init; }
}
```

---

## Troubleshooting

### Common Issues

#### "Connection refused" to Ollama

```
Error: Unable to connect to http://localhost:11434
```

**Solution**: Ensure Ollama is running:
```bash
ollama serve
```

#### "Model not found" in Ollama

```
Error: Model 'bge-m3' not found
```

**Solution**: Pull the model first:
```bash
ollama pull bge-m3
```

#### "pgvector extension not found"

```
Error: could not load library "$libdir/vector"
```

**Solution**: Install pgvector in PostgreSQL:
```sql
CREATE EXTENSION vector;
```

#### "API key invalid" with OpenAI

**Solution**: Verify your API key is correct and has embedding permissions.

### Performance Tips

1. **Use batch operations**: `IngestDirectoryAsync` is more efficient than multiple `IngestAsync` calls
2. **Optimize chunk size**: Larger chunks = fewer embeddings = faster ingestion
3. **Use SQLite-vec for development**: Zero config, fast for small datasets
4. **Use Qdrant for production**: Better performance at scale

---

## Best Practices

### 1. Initialize Once

```csharp
// Good: Initialize once at startup
await rag.InitializeAsync();

// Bad: Initialize before each operation
await rag.InitializeAsync(); // Don't do this repeatedly
await rag.SearchAsync(query);
```

### 2. Use Meaningful Document Names

```csharp
// Good
await rag.IngestTextAsync(content, "user-manual-v2.1");
await rag.IngestTextAsync(content, "article-2024-01-15-ai-trends");

// Bad
await rag.IngestTextAsync(content, "doc1");
await rag.IngestTextAsync(content, "text");
```

### 3. Add Metadata for Filtering

```csharp
await rag.IngestTextAsync(
    text: content,
    documentName: "quarterly-report-q4-2024",
    metadata: new Dictionary<string, object>
    {
        { "Department", "Finance" },
        { "Year", 2024 },
        { "Quarter", 4 },
        { "Confidential", true }
    }
);
```

### 4. Handle Errors Gracefully

```csharp
try
{
    var results = await rag.SearchAsync(query);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not initialized"))
{
    await rag.InitializeAsync();
    var results = await rag.SearchAsync(query);
}
```

### 5. Use Cancellation Tokens

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    var results = await rag.SearchAsync(query, cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Search timed out");
}
```

---

## Support

- **Issues**: [GitHub Issues](https://github.com/techierathore/TechieRag/issues)
- **Source Code**: [GitHub Repository](https://github.com/techierathore/TechieRag)

---

*This guide is for TechieRag v1.0.0. For the embedded version with offline capabilities, see the [TechieRag.Embedded User Guide](TechieRag.Embedded-UserGuide.md).*
