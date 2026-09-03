# TechieRag

[![Build and Publish NuGet](https://github.com/techierathore/TechieRag/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/techierathore/TechieRag/actions/workflows/publish-nuget.yml)
[![NuGet TechieRag](https://img.shields.io/nuget/v/TechieRag?label=TechieRag)](https://www.nuget.org/packages/TechieRag)
[![NuGet TechieRag.Embedded](https://img.shields.io/nuget/v/TechieRag.Embedded?label=TechieRag.Embedded)](https://www.nuget.org/packages/TechieRag.Embedded)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A flexible, configurable RAG (Retrieval-Augmented Generation) library for .NET. Build powerful document search and retrieval systems with minimal code.

## Features

- **Multiple Embedding Providers**: Ollama, LM Studio, OpenAI, Azure OpenAI, ONNX, or any HTTP-compatible API
- **Multiple Vector Stores**: SQLite-vec (local), PostgreSQL/pgvector, Qdrant
- **Built-in Document Processors**: PDF, DOCX, HTML, Markdown, JSON, TOML, and code files
- **Fluent Builder API**: Easy configuration with method chaining
- **Offline Capable**: Use `TechieRag.Embedded` for completely offline operation with BGE-M3 model

## Packages

| Package | Description | User Guide |
|---------|-------------|------------|
| `TechieRag` | Core library with all embedding providers and vector stores | [User Guide](docs/TechieRag-UserGuide.md) |
| `TechieRag.Embedded` | Self-contained package with embedded BGE-M3 ONNX model for offline use | [User Guide](docs/TechieRag.Embedded-UserGuide.md) |

## Installation

Both packages are published on [nuget.org](https://www.nuget.org/packages/TechieRag). No account, no token,
no `nuget.config` edit — the default NuGet feed every .NET SDK already has is all you need.

```bash
# Core package — bring your own embedding service (Ollama, OpenAI, Azure OpenAI, LM Studio, ...)
dotnet add package TechieRag

# OR the self-contained package — embedded BGE-M3 model, works fully offline
dotnet add package TechieRag.Embedded
```

Or in your `.csproj`:

```xml
<PackageReference Include="TechieRag" Version="1.*" />
```

`TechieRag` targets **.NET 8 and .NET 10**; `TechieRag.Embedded` targets **.NET 10**.

### Your first search in five minutes

The fastest zero-cost path uses [Ollama](https://ollama.com) for embeddings (a one-time ~1.2 GB model pull)
and the built-in SQLite vector store. Nothing else to install or configure.

```bash
# 1. An embedding model, served locally
ollama pull bge-m3

# 2. A new console app with TechieRag
dotnet new console -n MyRagApp && cd MyRagApp
dotnet add package TechieRag
```

Replace `Program.cs` with:

```csharp
using TechieRag;

var rag = new TechieRagBuilder()
    .UseOllama("http://localhost:11434", "bge-m3")   // embeddings from Ollama
    .UseSqliteVec("myapp.db")                         // local SQLite vector store
    .Build();

await rag.InitializeAsync();

await rag.IngestTextAsync(
    "TechieRag is a RAG library for .NET with pluggable embedding providers and vector stores.",
    documentName: "about-techierag");
await rag.IngestTextAsync(
    "Ollama runs open-weight language and embedding models on your own machine.",
    documentName: "about-ollama");

var results = await rag.SearchAsync("What is TechieRag?", topK: 2);
foreach (var result in results)
    Console.WriteLine($"{result.Score:F3}  {result.Chunk.Text}");
```

```bash
# 3. Run it
dotnet run
```

You should see two scored results, the `about-techierag` chunk first. From here, `IngestAsync("file.pdf")`
and `IngestDirectoryAsync("./docs", "*.md")` bring in real documents — see the [Quick Start](#quick-start).

### GitHub Packages — internal development builds only

> **Public consumers never need this section.** It exists for maintainers working on TechieRag itself
> who want pre-release builds from the private feed. Every merge and every GitHub Release publishes there;
> a release reaches nuget.org only when a maintainer runs the public workflow against its tag.

<details>
<summary>Consuming the private GitHub Packages feed</summary>

1. Create a GitHub Personal Access Token (classic) with the `read:packages` scope.
2. Register the source (or put the same three values in a `nuget.config` next to your solution):
   ```bash
   dotnet nuget add source "https://nuget.pkg.github.com/techierathore/index.json" \
     --name "github-techierathore" \
     --username YOUR_GITHUB_USERNAME \
     --password YOUR_GITHUB_PAT \
     --store-password-in-clear-text
   ```
3. Pin the source when installing, so the private feed is a deliberate choice rather than a fallback:
   ```bash
   dotnet add package TechieRag --source github-techierathore --prerelease
   ```

Never commit a `nuget.config` that carries the token. The publishing pipeline for both feeds is described in
[NUGET-PUBLISHING.md](NUGET-PUBLISHING.md).

</details>

## Quick Start

### Using TechieRag.Embedded (Offline, No Setup Required)

```csharp
using TechieRag;
using TechieRag.Embedded;

// Create TechieRag with embedded BGE-M3 model and SQLite storage
var rag = new TechieRagBuilder()
    .UseEmbedded()      // Uses local ONNX BGE-M3 model
    .UseSqliteVec()     // Local SQLite vector store
    .Build();

// Initialize (downloads model on first run, ~2.3GB, cached locally)
await rag.InitializeAsync();

// Ingest documents from files
await rag.IngestAsync("path/to/document.pdf");
await rag.IngestDirectoryAsync("./docs", "*.md");

// Ingest raw text directly (great for database content, API responses, etc.)
await rag.IngestTextAsync(
    text: "Your article or story content here...",
    documentName: "my-article",
    metadata: new Dictionary<string, object> { { "Source", "database" } }
);

// Search
var results = await rag.SearchAsync("What is machine learning?", topK: 5);
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3} - {result.Chunk.Text}");
}
```

### Using TechieRag with Ollama

```csharp
using TechieRag;

var rag = new TechieRagBuilder()
    .UseOllama("http://localhost:11434", "bge-m3")
    .UseSqliteVec("myapp.db")
    .Build();

await rag.InitializeAsync();
```

### Using TechieRag with OpenAI

```csharp
using TechieRag;

var rag = new TechieRagBuilder()
    .UseOpenAI("your-api-key", "text-embedding-3-small")
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();
```

### Using TechieRag with Qdrant

```csharp
using TechieRag;

var rag = new TechieRagBuilder()
    .UseOllama()
    .UseQdrant("http://localhost:6334", apiKey: "your-qdrant-api-key")
    .Build();

await rag.InitializeAsync();
```

## Configuration Options

### Embedding Providers

| Provider | Method | Requirements |
|----------|--------|--------------|
| Embedded (BGE-M3) | `.UseEmbedded()` | TechieRag.Embedded package |
| Ollama | `.UseOllama(endpoint, model)` | Ollama running locally |
| LM Studio | `.UseLmStudio(endpoint)` | LM Studio running locally |
| OpenAI | `.UseOpenAI(apiKey, model, endpoint)` | OpenAI API key |
| Azure OpenAI | `.UseAzureOpenAI(endpoint, apiKey, model)` | Azure OpenAI resource |
| ONNX | `.UseOnnx(modelPath)` | ONNX model file |
| HTTP (Generic) | `.UseHttp(endpoint, format, model)` | Any HTTP embedding API |

### Vector Stores

| Store | Method | Connection String Example |
|-------|--------|--------------------------|
| SQLite-vec | `.UseSqliteVec(dbPath)` | `techierag.db` |
| PostgreSQL/pgvector | `.UsePgVector(connectionString)` | `Host=localhost;Database=mydb;...` |
| Qdrant | `.UseQdrant(endpoint, apiKey)` | `http://localhost:6334` |

### Processing Options

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .WithChunkSize(500, overlap: 50)  // Configure chunking
    .WithLogging(loggerFactory)        // Add logging
    .WithTelemetry(true)               // Enable telemetry
    .Build();
```

## Supported Document Types

| Type | Extensions | Processor |
|------|------------|-----------|
| PDF | `.pdf` | PdfProcessor |
| Word | `.docx` | DocxProcessor |
| HTML | `.html`, `.htm` | HtmlProcessor |
| Markdown | `.md`, `.markdown` | MarkdownProcessor |
| JSON | `.json` | JsonProcessor |
| TOML | `.toml` | TomlProcessor |
| Code | `.cs`, `.py`, `.js`, `.ts`, etc. | CodeProcessor |
| Plain Text | `.txt` | TextProcessor |
| Other Text | `*` | GenericTextProcessor (fallback) |

## API Reference

### Core Interface: `ITechieRag`

```csharp
public interface ITechieRag
{
    // Initialize the RAG system
    Task InitializeAsync(CancellationToken ct = default);

    // Ingest a single file
    Task<string> IngestAsync(string filePath, CancellationToken ct = default);

    // Ingest raw text
    Task<string> IngestTextAsync(string text, string documentName,
        Dictionary<string, object>? metadata = null, CancellationToken ct = default);

    // Ingest all matching files in a directory
    Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath,
        string searchPattern = "*.*", CancellationToken ct = default);

    // Search for relevant documents
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5,
        string? documentFilter = null, CancellationToken ct = default);

    // Delete a document and its chunks
    Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);

    // List all ingested documents
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct = default);

    // Get ingestion statistics
    Task<IngestionStats> GetStatsAsync(CancellationToken ct = default);

    // Clear all data
    Task ClearAsync(CancellationToken ct = default);
}
```

### Search Results

```csharp
var results = await rag.SearchAsync("your query", topK: 10);

foreach (var result in results)
{
    Console.WriteLine($"Document: {result.Chunk.DocumentId}");
    Console.WriteLine($"Score: {result.Score}");
    Console.WriteLine($"Content: {result.Chunk.Text}");
    Console.WriteLine($"Page: {result.Chunk.PageNumber}  Chunk: {result.Chunk.ChunkIndex}");
    foreach (var (key, value) in result.Chunk.Metadata)
        Console.WriteLine($"  {key} = {value}");
}
```

## Advanced Usage

### Custom Embedding Provider

```csharp
public class MyCustomEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 1024;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        // Your implementation
    }

    public Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct)
    {
        // Your implementation
    }
}

var rag = new TechieRagBuilder()
    .UseCustomEmbeddingProvider(() => new MyCustomEmbeddingProvider())
    .UseSqliteVec()
    .Build();
```

### Dependency Injection

```csharp
// In Program.cs or Startup.cs
services.AddSingleton<ITechieRag>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    return new TechieRagBuilder()
        .UseEmbedded()
        .UseSqliteVec("app.db")
        .WithLogging(loggerFactory)
        .Build();
});
```

### Text Ingestion (Raw Text)

Perfect for ingesting content from databases, APIs, or any text source without saving to files first:

```csharp
// Ingest text content directly
var documentId = await rag.IngestTextAsync(
    text: articleContent,           // Your raw text content
    documentName: "article-123",    // Unique name for this document
    metadata: new Dictionary<string, object>
    {
        { "Source", "PostgreSQL" },
        { "ArticleId", 123 },
        { "Category", "Technology" }
    }
);

Console.WriteLine($"Ingested with ID: {documentId}");
```

Use cases:
- Embedding articles fetched from a database
- Processing API responses (news feeds, blog posts)
- Ingesting user-generated content
- Testing embeddings with sample text

## Sample Application

The repository includes a Blazor Server application (**TechieDesk**, formerly `TechieRagWeb`) demonstrating:
- **File Ingestion UI** - Upload and process documents from local directories
- **Text Ingestion UI** - Paste and ingest raw text content directly
- Search interface
- Configuration management
- Qdrant database administration

Run it with:
```bash
cd apps/TechieDesk
dotnet run
```

## Documentation

For comprehensive guides on using TechieRag, see:

- **[TechieRag User Guide](docs/TechieRag-UserGuide.md)** - Complete guide for the core package
- **[TechieRag.Embedded User Guide](docs/TechieRag.Embedded-UserGuide.md)** - Guide for the self-contained embedded package

## Requirements

- `TechieRag`: .NET 8.0 or .NET 10.0; `TechieRag.Embedded`: .NET 10.0
- For `TechieRag.Embedded`: ~2.3GB disk space for the BGE-M3 model (downloaded on first use)

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Acknowledgments

- [BGE-M3](https://huggingface.co/BAAI/bge-m3) - Multilingual embedding model
- [SQLite-vec](https://github.com/asg017/sqlite-vec) - Vector search for SQLite
- [Qdrant](https://qdrant.tech/) - Vector database
- [pgvector](https://github.com/pgvector/pgvector) - Vector extension for PostgreSQL
