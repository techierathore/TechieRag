# TechieRag.Embedded User Guide

**Version:** 1.0.0
**Package:** `TechieRag.Embedded`
**Target Framework:** .NET 10.0+

---

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [How It Works](#how-it-works)
   - [BGE-M3 Model](#bge-m3-model)
   - [Model Download Process](#model-download-process)
   - [Model Storage Location](#model-storage-location)
5. [Configuration](#configuration)
   - [Basic Setup](#basic-setup)
   - [Vector Store Options](#vector-store-options)
   - [Processing Options](#processing-options)
6. [Core Operations](#core-operations)
   - [Initializing TechieRag](#initializing-techierag)
   - [Ingesting Files](#ingesting-files)
   - [Ingesting Text](#ingesting-text)
   - [Searching Documents](#searching-documents)
   - [Managing Documents](#managing-documents)
7. [Model Download Monitoring](#model-download-monitoring)
8. [Offline Usage](#offline-usage)
9. [Advanced Usage](#advanced-usage)
   - [Dependency Injection](#dependency-injection)
   - [Blazor Integration](#blazor-integration)
   - [Background Processing](#background-processing)
10. [API Reference](#api-reference)
11. [Troubleshooting](#troubleshooting)
12. [Performance Considerations](#performance-considerations)
13. [Best Practices](#best-practices)

---

## Introduction

TechieRag.Embedded is a self-contained RAG (Retrieval-Augmented Generation) library that includes an embedded BGE-M3 ONNX model for completely offline operation. No external embedding services required!

### Key Features

- **Zero Configuration**: Works out of the box with no external dependencies
- **Completely Offline**: Once the model is downloaded, no internet required
- **High-Quality Embeddings**: Uses BGE-M3, a state-of-the-art multilingual embedding model
- **Automatic Model Management**: Downloads and caches the model automatically
- **Progress Tracking**: Monitor model download progress in real-time

### Package Comparison

| Feature | TechieRag | TechieRag.Embedded |
|---------|-----------|-------------------|
| Package Size | Small (~2MB) | Small (~2MB) |
| Model Size | N/A | ~2.3GB (downloaded) |
| External Service | Required | Not Required |
| Offline Mode | No | Yes |
| Setup Complexity | Medium | Zero |

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
   dotnet add package TechieRag.Embedded --source github-techierathore
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

### Minimal Example (3 Lines of Setup!)

```csharp
using TechieRag;
using TechieRag.Embedded;

// Create TechieRag with embedded model
var rag = new TechieRagBuilder()
    .UseEmbedded()      // Uses local ONNX BGE-M3 model
    .UseSqliteVec()     // Local SQLite vector store
    .Build();

// Initialize (downloads model on first run, ~2.3GB)
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

### With Text Ingestion

```csharp
using TechieRag;
using TechieRag.Embedded;

var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();

// Ingest text directly (no file needed!)
var documentId = await rag.IngestTextAsync(
    text: @"Machine learning is a subset of artificial intelligence that enables
            systems to learn and improve from experience without being explicitly
            programmed. It focuses on developing algorithms that can access data
            and use it to learn for themselves.",
    documentName: "ml-intro",
    metadata: new Dictionary<string, object>
    {
        { "Topic", "Machine Learning" },
        { "Source", "Tutorial" }
    }
);

// Search
var results = await rag.SearchAsync("What is ML?");
```

---

## How It Works

### BGE-M3 Model

TechieRag.Embedded uses **BGE-M3** (BAAI General Embedding - Multilingual, Multi-Functionality, Multi-Granularity), a state-of-the-art embedding model:

| Property | Value |
|----------|-------|
| Model | BGE-M3 |
| Format | ONNX (optimized for .NET) |
| Dimensions | 1024 |
| Languages | 100+ languages |
| Size | ~2.3GB |
| Performance | ~50-100 embeddings/second (CPU) |

### Model Download Process

On first initialization, TechieRag.Embedded:

1. Checks if model files exist locally
2. If not, downloads from Hugging Face (~2.3GB)
3. Validates file integrity
4. Loads the ONNX model into memory

**Download happens only once** - subsequent runs use the cached model.

### Model Storage Location

Models are stored in a platform-specific cache directory:

| Platform | Location |
|----------|----------|
| Windows | `%LOCALAPPDATA%\TechieRag\Models\bge-m3-onnx` |
| macOS | `~/Library/Application Support/TechieRag/Models/bge-m3-onnx` |
| Linux | `~/.local/share/TechieRag/Models/bge-m3-onnx` |

**Model Files**:
- `model.onnx` - Main model file (~2.2GB)
- `tokenizer.json` - Tokenizer configuration
- `tokenizer_config.json` - Tokenizer settings
- `special_tokens_map.json` - Special token definitions
- `config.json` - Model configuration

---

## Configuration

### Basic Setup

```csharp
using TechieRag;
using TechieRag.Embedded;

var rag = new TechieRagBuilder()
    .UseEmbedded()      // Required for embedded model
    .UseSqliteVec()     // Vector store
    .Build();
```

### Vector Store Options

#### SQLite-vec (Default, Recommended)

```csharp
// Default database
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()  // Uses "techierag.db"
    .Build();

// Custom database path
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec("myapp.db")
    .Build();
```

#### PostgreSQL with pgvector

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UsePgVector("Host=localhost;Database=mydb;Username=user;Password=pass")
    .Build();
```

#### Qdrant

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseQdrant("http://localhost:6334", apiKey: "optional-api-key")
    .Build();
```

### Processing Options

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .WithChunkSize(
        size: 500,      // Characters per chunk
        overlap: 50     // Overlap between chunks
    )
    .WithLogging(loggerFactory)
    .WithTelemetry(true)
    .Build();
```

---

## Core Operations

### Initializing TechieRag

```csharp
// This may take a few minutes on first run (model download)
await rag.InitializeAsync();
```

**First Run**: Downloads ~2.3GB model (may take 5-15 minutes depending on connection)
**Subsequent Runs**: Loads cached model (typically 10-30 seconds)

### Ingesting Files

#### Single File

```csharp
string documentId = await rag.IngestAsync("path/to/document.pdf");
```

#### Directory with Pattern

```csharp
// All supported files
var ids = await rag.IngestDirectoryAsync("./documents", "*.*");

// Only PDFs
var pdfIds = await rag.IngestDirectoryAsync("./documents", "*.pdf");

// Only code files
var codeIds = await rag.IngestDirectoryAsync("./src", "*.cs");
```

### Ingesting Text

Ingest raw text content without creating files:

```csharp
// Basic
string docId = await rag.IngestTextAsync(
    text: "Your content here...",
    documentName: "my-document"
);

// With metadata
string docId = await rag.IngestTextAsync(
    text: articleContent,
    documentName: "article-2024-01-15",
    metadata: new Dictionary<string, object>
    {
        { "Author", "John Doe" },
        { "Category", "Technology" },
        { "Tags", new[] { "AI", "ML", "NLP" } }
    }
);
```

**Perfect For**:
- Database content (articles, blog posts)
- API responses
- User-generated content
- Real-time data ingestion
- Testing and development

### Searching Documents

```csharp
// Basic search
var results = await rag.SearchAsync("machine learning concepts");

// With options
var results = await rag.SearchAsync(
    query: "neural networks",
    topK: 10,                           // Return top 10 results
    documentFilter: "specific-doc-id"   // Optional filter
);

// Process results
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Document: {result.Chunk.DocumentId}");
    Console.WriteLine($"Content: {result.Chunk.Text}");
    Console.WriteLine($"Page: {result.Chunk.PageNumber}");
    Console.WriteLine("---");
}
```

### Managing Documents

```csharp
// List all documents
var documents = await rag.ListDocumentsAsync();

// Get statistics
var stats = await rag.GetStatsAsync();
Console.WriteLine($"Documents: {stats.TotalDocuments}");
Console.WriteLine($"Chunks: {stats.TotalChunks}");
Console.WriteLine($"Size: {stats.VectorStoreSizeBytes / 1024 / 1024} MB");

// Delete a document
await rag.DeleteDocumentAsync("document-id");

// Clear all data
await rag.ClearAsync();
```

---

## Model Download Monitoring

### Using ModelDownloadService

Monitor the model download progress in real-time:

```csharp
using TechieRag.Embedded;

// Subscribe to progress events
ModelDownloadService.Instance.ProgressChanged += (sender, progress) =>
{
    Console.WriteLine($"Status: {progress.Status}");
    Console.WriteLine($"Message: {progress.StatusMessage}");
    Console.WriteLine($"File {progress.CompletedFiles + 1} of {progress.TotalFiles}");
    Console.WriteLine($"Progress: {progress.CurrentFileProgressPercent:F1}%");
    Console.WriteLine($"Downloaded: {progress.CurrentFileBytesDownloaded / 1024 / 1024} MB");
};

// Initialize (triggers download if needed)
await rag.InitializeAsync();
```

### Progress Properties

```csharp
public class ModelDownloadProgress
{
    public ModelDownloadStatus Status { get; }         // Checking, Downloading, Completed, Failed
    public string StatusMessage { get; }               // Human-readable status
    public int TotalFiles { get; }                     // Total files to download
    public int CompletedFiles { get; }                 // Files downloaded so far
    public long CurrentFileTotalBytes { get; }         // Current file size
    public long CurrentFileBytesDownloaded { get; }    // Bytes downloaded
    public double CurrentFileProgressPercent { get; }  // 0-100
}

public enum ModelDownloadStatus
{
    Checking,      // Checking if model exists
    Downloading,   // Downloading model files
    Completed,     // Download complete
    Failed         // Download failed
}
```

### Blazor Integration Example

```razor
@using TechieRag.Embedded

@if (isDownloading)
{
    <div class="download-progress">
        <h3>Downloading BGE-M3 Model</h3>
        <p>@progress?.StatusMessage</p>
        <progress value="@(progress?.CurrentFileProgressPercent ?? 0)" max="100"></progress>
        <p>File @((progress?.CompletedFiles ?? 0) + 1) of @(progress?.TotalFiles ?? 5)</p>
    </div>
}

@code {
    private bool isDownloading;
    private ModelDownloadProgress? progress;

    protected override void OnInitialized()
    {
        ModelDownloadService.Instance.ProgressChanged += OnProgressChanged;
        UpdateStatus();
    }

    private void OnProgressChanged(object? sender, ModelDownloadProgress p)
    {
        progress = p;
        UpdateStatus();
        InvokeAsync(StateHasChanged);
    }

    private void UpdateStatus()
    {
        var status = ModelDownloadService.Instance.Progress.Status;
        isDownloading = status == ModelDownloadStatus.Downloading
                     || status == ModelDownloadStatus.Checking;
    }

    public void Dispose()
    {
        ModelDownloadService.Instance.ProgressChanged -= OnProgressChanged;
    }
}
```

---

## Offline Usage

Once the model is downloaded, TechieRag.Embedded works completely offline:

```csharp
// First run (requires internet)
var rag = new TechieRagBuilder()
    .UseEmbedded()      // Downloads model if not present
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();  // Downloads ~2.3GB

// All subsequent runs (no internet required)
await rag.IngestAsync("document.pdf");
await rag.IngestTextAsync("content...", "doc-name");
var results = await rag.SearchAsync("query");
```

### Pre-downloading the Model

To ensure offline capability before deployment:

```csharp
// Run this once with internet connection
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();  // This downloads the model
Console.WriteLine("Model downloaded. System ready for offline use.");
```

### Deploying with Pre-downloaded Model

1. Download model on a machine with internet
2. Copy the model directory to the target machine
3. The app will use the local model without downloading

Model location: `%LOCALAPPDATA%\TechieRag\Models\bge-m3-onnx`

---

## Advanced Usage

### Dependency Injection

#### ASP.NET Core / Minimal API

```csharp
// Program.cs
using TechieRag;
using TechieRag.Embedded;

var builder = WebApplication.CreateBuilder(args);

// Register TechieRag
builder.Services.AddSingleton<ITechieRag>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    return new TechieRagBuilder()
        .UseEmbedded()
        .UseSqliteVec("rag.db")
        .WithLogging(loggerFactory)
        .Build();
});

var app = builder.Build();

// Initialize on startup
var rag = app.Services.GetRequiredService<ITechieRag>();
await rag.InitializeAsync();

// Use in endpoints
app.MapGet("/search", async (string query, ITechieRag rag) =>
{
    var results = await rag.SearchAsync(query);
    return Results.Ok(results);
});

app.MapPost("/ingest-text", async (IngestRequest req, ITechieRag rag) =>
{
    var id = await rag.IngestTextAsync(req.Text, req.Name, req.Metadata);
    return Results.Ok(new { DocumentId = id });
});

app.Run();

record IngestRequest(string Text, string Name, Dictionary<string, object>? Metadata);
```

### Blazor Integration

```csharp
// Program.cs
builder.Services.AddSingleton<ITechieRag>(sp =>
{
    return new TechieRagBuilder()
        .UseEmbedded()
        .UseSqliteVec("blazor-rag.db")
        .Build();
});

// For Blazor Server with large text input
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});
```

### Background Processing

For long-running ingestion:

```csharp
public class IngestionBackgroundService : BackgroundService
{
    private readonly ITechieRag _rag;
    private readonly ILogger<IngestionBackgroundService> _logger;
    private readonly Channel<IngestionJob> _channel;

    public IngestionBackgroundService(
        ITechieRag rag,
        ILogger<IngestionBackgroundService> logger)
    {
        _rag = rag;
        _logger = logger;
        _channel = Channel.CreateBounded<IngestionJob>(100);
    }

    public async Task QueueIngestion(string text, string name)
    {
        await _channel.Writer.WriteAsync(new IngestionJob(text, name));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _rag.InitializeAsync(stoppingToken);

        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var id = await _rag.IngestTextAsync(job.Text, job.Name);
                _logger.LogInformation("Ingested {Name} with ID {Id}", job.Name, id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest {Name}", job.Name);
            }
        }
    }

    private record IngestionJob(string Text, string Name);
}
```

---

## API Reference

### TechieRagBuilder Extension Method

```csharp
/// <summary>
/// Configure TechieRag to use the embedded BGE-M3 ONNX model.
/// Downloads the model (~2.3GB) on first use.
/// </summary>
public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder);
```

### ModelDownloadService

```csharp
public class ModelDownloadService
{
    /// <summary>
    /// Singleton instance for tracking download progress.
    /// </summary>
    public static ModelDownloadService Instance { get; }

    /// <summary>
    /// Current download progress.
    /// </summary>
    public ModelDownloadProgress Progress { get; }

    /// <summary>
    /// Event fired when download progress changes.
    /// </summary>
    public event EventHandler<ModelDownloadProgress>? ProgressChanged;
}
```

### ITechieRag Interface

See [TechieRag User Guide - API Reference](TechieRag-UserGuide.md#api-reference) for the complete interface documentation.

---

## Troubleshooting

### Common Issues

#### Model Download Fails

```
Error: Failed to download model file
```

**Solutions**:
1. Check internet connection
2. Verify firewall allows HTTPS to huggingface.co
3. Try again (download resumes from where it stopped)
4. Manually download and place files in cache directory

#### Out of Memory During Initialization

```
Error: OutOfMemoryException
```

**Solutions**:
1. Ensure at least 4GB RAM available
2. Close other applications
3. Consider using TechieRag (non-embedded) with Ollama instead

#### Slow Embedding Performance

**Tips**:
1. Use batch operations when possible
2. Larger chunks = fewer embeddings
3. Consider GPU acceleration (requires ONNX GPU runtime)

### Model Cache Issues

To reset the model cache:

```powershell
# Windows
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\TechieRag\Models"

# macOS/Linux
rm -rf ~/.local/share/TechieRag/Models
```

---

## Performance Considerations

### Memory Usage

| Component | Memory |
|-----------|--------|
| ONNX Model | ~2-3GB |
| Tokenizer | ~100MB |
| Per Embedding | ~4KB |

**Total**: Plan for ~4-5GB RAM minimum

### Embedding Speed

| Hardware | Speed |
|----------|-------|
| Modern CPU | 50-100 embeddings/sec |
| With AVX2 | 100-200 embeddings/sec |
| GPU (if configured) | 500+ embeddings/sec |

### Optimization Tips

1. **Batch Processing**: Ingest multiple files with `IngestDirectoryAsync`
2. **Chunk Size**: Larger chunks = fewer embeddings = faster ingestion
3. **Async Operations**: Use `CancellationToken` for long operations
4. **Pre-warm**: Call `InitializeAsync` at app startup

---

## Best Practices

### 1. Initialize Early

```csharp
// Good: Initialize at startup
public class Program
{
    public static async Task Main(string[] args)
    {
        var rag = CreateRag();
        await rag.InitializeAsync();  // Load model early

        // Now ready for fast operations
    }
}
```

### 2. Handle First-Run Experience

```csharp
ModelDownloadService.Instance.ProgressChanged += (s, p) =>
{
    if (p.Status == ModelDownloadStatus.Downloading)
    {
        ShowDownloadUI(p);
    }
};
```

### 3. Use Connection Pooling for Database Vector Stores

```csharp
// For PostgreSQL
var connectionString = "Host=localhost;Database=rag;Pooling=true;Min Pool Size=5;Max Pool Size=20";
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UsePgVector(connectionString)
    .Build();
```

### 4. Implement Graceful Shutdown

```csharp
public class RagService : IAsyncDisposable
{
    private readonly ITechieRag _rag;

    public async ValueTask DisposeAsync()
    {
        // Ensure pending operations complete
        if (_rag is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}
```

### 5. Monitor Resource Usage

```csharp
var stats = await rag.GetStatsAsync();
if (stats.VectorStoreSizeBytes > 1_000_000_000) // 1GB
{
    _logger.LogWarning("Vector store exceeds 1GB. Consider archiving old documents.");
}
```

---

## Support

- **Issues**: [GitHub Issues](https://github.com/techierathore/TechieRag/issues)
- **Source Code**: [GitHub Repository](https://github.com/techierathore/TechieRag)

---

## Requirements Summary

| Requirement | Minimum | Recommended |
|-------------|---------|-------------|
| .NET | 10.0 | 10.0+ |
| RAM | 4GB | 8GB+ |
| Disk Space | 3GB | 5GB+ |
| CPU | x64 | x64 with AVX2 |
| Internet | First run only | First run only |

---

*This guide is for TechieRag.Embedded v1.0.0. For the base package with external embedding providers, see the [TechieRag User Guide](TechieRag-UserGuide.md).*
