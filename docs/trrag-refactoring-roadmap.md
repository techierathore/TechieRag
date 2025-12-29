# TechieRag Development Roadmap

## Building TechieRag Library From Scratch

This document provides a step-by-step guide to build the TechieRag library as a **fresh solution** - NOT refactoring ChatAppEx. ChatAppEx serves only as a **reference** for proven patterns.

---

## Coding Standards

All code in the TechieRag solution MUST follow these standards:

### A. Naming Conventions - No Underscores

**DO NOT use underscores (`_`) in any names.** Use PascalCase or camelCase instead.

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `SqliteVecStore`, `TextChunk` |
| Interfaces | PascalCase with I prefix | `IVectorStore`, `IEmbeddingProvider` |
| Methods | PascalCase | `SearchAsync`, `IngestDocument` |
| Properties | PascalCase | `DocumentId`, `ChunkIndex` |
| Public fields | PascalCase | `MaxChunkSize` |
| Private fields | camelCase | `connectionString`, `httpClient` |
| Parameters | camelCase | `queryVector`, `topK` |
| Local variables | camelCase | `results`, `pageText` |
| Constants | PascalCase | `DefaultChunkSize` |
| Database tables | PascalCase | `Documents`, `Chunks`, `ChunksVec` |
| Database columns | PascalCase | `DocumentId`, `ChunkIndex`, `CreatedAt` |
| Stored procedures | PascalCase | `GetDocumentById`, `InsertChunk` |

**Examples:**
```csharp
// ❌ WRONG - uses underscores
private readonly string _connectionString;
private readonly int _dimensions;
public string source_file { get; set; }
CREATE TABLE chunks_vec ...

// ✅ CORRECT - no underscores
private readonly string connectionString;
private readonly int dimensions;
public string SourceFile { get; set; }
CREATE TABLE ChunksVec ...
```

### B. XML Documentation Comments

**Every class and method MUST have XML documentation comments** explaining:
1. **Purpose** - What the class/method does in context of the whole solution
2. **Code flow** - How it fits into the overall architecture (for complex components)
3. **Complex logic** - Explanation of any non-obvious algorithms or decisions

**Class documentation template:**
```csharp
/// <summary>
/// [Brief description of what this class does]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [Explain role in the TechieRag solution]</para>
/// <para><b>Code Flow:</b> [How this class is used - who creates it, who calls it]</para>
/// <para><b>Dependencies:</b> [Key dependencies and why they're needed]</para>
/// </remarks>
public class ExampleClass { }
```

**Method documentation template:**
```csharp
/// <summary>
/// [Brief description of what this method does]
/// </summary>
/// <param name="paramName">[Description of parameter]</param>
/// <returns>[Description of return value]</returns>
/// <remarks>
/// <para><b>Flow:</b> [Step-by-step explanation if complex]</para>
/// <para><b>Note:</b> [Any important considerations]</para>
/// </remarks>
/// <exception cref="ExceptionType">[When this exception is thrown]</exception>
public async Task<Result> ExampleMethod(string paramName) { }
```

---

## Key Decision: Fresh Solution Approach

| Approach | Decision |
|----------|----------|
| Refactor ChatAppEx in-place | **REJECTED** |
| Create fresh solution from scratch | **SELECTED** |
| ChatAppEx role | Reference implementation only (copy patterns, not code) |
| Naming | ALL projects named "TechieRag*" - no ChatAppEx references |

**Rationale:** Clean slate ensures proper architecture, no legacy coupling, and professional package structure.

---

## Target Solution Structure

```
TechieRag/
├── TechieRag.sln
│
├── src/
│   ├── TechieRag/                              # Core library (NuGet: TechieRag)
│   │   ├── TechieRag.csproj
│   │   ├── ITechieRag.cs                       # Main interface
│   │   ├── TechieRagClient.cs                  # Main implementation
│   │   ├── TechieRagBuilder.cs                 # Fluent configuration
│   │   ├── TechieRagConfig.cs                  # Configuration object
│   │   ├── DependencyInjection/
│   │   │   └── ServiceCollectionExtensions.cs
│   │   ├── Abstractions/
│   │   │   ├── IVectorStore.cs
│   │   │   ├── IEmbeddingProvider.cs
│   │   │   └── IDocumentProcessor.cs
│   │   ├── Models/
│   │   │   ├── TextChunk.cs
│   │   │   ├── Document.cs
│   │   │   ├── SearchResult.cs
│   │   │   └── IngestionStats.cs
│   │   ├── VectorStores/
│   │   │   ├── SqliteVecStore.cs
│   │   │   ├── PgVectorStore.cs
│   │   │   └── QdrantStore.cs
│   │   ├── Embedding/
│   │   │   ├── OllamaEmbeddingProvider.cs
│   │   │   ├── LmStudioEmbeddingProvider.cs
│   │   │   ├── AzureOpenAIEmbeddingProvider.cs
│   │   │   └── OnnxEmbeddingProvider.cs
│   │   ├── Processors/
│   │   │   ├── PdfProcessor.cs
│   │   │   ├── DocxProcessor.cs
│   │   │   ├── TextProcessor.cs
│   │   │   ├── MarkdownProcessor.cs
│   │   │   ├── HtmlProcessor.cs
│   │   │   ├── JsonProcessor.cs
│   │   │   ├── TomlProcessor.cs
│   │   │   └── CodeProcessor.cs
│   │   └── Telemetry/
│   │       ├── TechieRagMetrics.cs
│   │       └── TechieRagActivitySource.cs
│   │
│   └── TechieRag.Embedded/                     # With bundled model (NuGet: TechieRag.Embedded)
│       ├── TechieRag.Embedded.csproj
│       ├── BundledBgeM3Provider.cs
│       └── Models/
│           └── bge-m3-onnx/                    # Bundled ONNX model files
│
├── samples/
│   └── TechieRagWeb/                           # Sample Blazor app showcasing TechieRag
│       ├── TechieRagWeb.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Components/
│       │   ├── App.razor
│       │   ├── Routes.razor
│       │   ├── Layout/
│       │   │   └── MainLayout.razor
│       │   └── Pages/
│       │       ├── Settings.razor              # Configuration UI
│       │       ├── Ingestion.razor             # Manual ingestion UI
│       │       └── Chat.razor                  # RAG chat UI
│       └── Services/
│           └── TechieRagConfigService.cs       # Runtime config management
│
└── tests/
    ├── TechieRag.Tests/
    └── TechieRag.IntegrationTests/
```

---

## Development Phases

### Phase 1: Solution Setup + Core Interfaces (Day 1)

#### Step 1.1: Create Fresh Solution

```powershell
# Create solution directory (NOT inside ChatAppEx)
mkdir C:\2AIdeation\TechieRag
cd C:\2AIdeation\TechieRag

# Create solution
dotnet new sln -n TechieRag

# Create core library
mkdir src
dotnet new classlib -n TechieRag -o src/TechieRag -f net10.0
dotnet sln add src/TechieRag/TechieRag.csproj

# Create embedded variant
dotnet new classlib -n TechieRag.Embedded -o src/TechieRag.Embedded -f net10.0
dotnet sln add src/TechieRag.Embedded/TechieRag.Embedded.csproj

# Create sample web app
mkdir samples
dotnet new blazor -n TechieRagWeb -o samples/TechieRagWeb -f net10.0 --interactivity Server
dotnet sln add samples/TechieRagWeb/TechieRagWeb.csproj

# Create test projects
mkdir tests
dotnet new xunit -n TechieRag.Tests -o tests/TechieRag.Tests -f net10.0
dotnet sln add tests/TechieRag.Tests/TechieRag.Tests.csproj

# Add project references
dotnet add samples/TechieRagWeb/TechieRagWeb.csproj reference src/TechieRag/TechieRag.csproj
dotnet add src/TechieRag.Embedded/TechieRag.Embedded.csproj reference src/TechieRag/TechieRag.csproj
dotnet add tests/TechieRag.Tests/TechieRag.Tests.csproj reference src/TechieRag/TechieRag.csproj
```

#### Step 1.2: Define Core Interfaces

**File: `src/TechieRag/ITechieRag.cs`**
```csharp
namespace TechieRag;

/// <summary>
/// Main interface for TechieRag RAG (Retrieval-Augmented Generation) operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines the contract for all RAG operations including document ingestion,
/// semantic search, and document management. This is the primary interface consumers interact with.</para>
/// <para><b>Code Flow:</b> Implemented by TechieRagClient. Created via TechieRagBuilder or DI container.
/// Applications call these methods to ingest documents and perform semantic searches.</para>
/// <para><b>Design:</b> All operations are async and support cancellation for responsive applications.</para>
/// </remarks>
public interface ITechieRag
{
    /// <summary>
    /// Ingests a single file into the vector store.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to ingest.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The generated document ID for the ingested file.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> File is read, processed by appropriate IDocumentProcessor,
    /// chunked, embedded via IEmbeddingProvider, and stored in IVectorStore.</para>
    /// </remarks>
    Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests raw text content as a document.
    /// </summary>
    /// <param name="text">The text content to ingest.</param>
    /// <param name="documentName">A friendly name for the document.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The generated document ID.</returns>
    Task<string> IngestTextAsync(string text, string documentName, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests all matching files from a directory.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing documents.</param>
    /// <param name="searchPattern">File pattern to match (e.g., "*.pdf", "*.*").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of document IDs for all successfully ingested files.</returns>
    Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*", CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs semantic search across all ingested documents.
    /// </summary>
    /// <param name="query">The natural language query to search for.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked list of search results with relevance scores.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Query is embedded, vector similarity search is performed,
    /// and results are ranked by relevance score (higher is better).</para>
    /// </remarks>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document and all its chunks from the vector store.
    /// </summary>
    /// <param name="documentId">The ID of the document to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all documents currently in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of all ingested documents with metadata.</returns>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves statistics about the current vector store state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including document count, chunk count, and storage size.</returns>
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all documents and chunks from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// <para><b>Warning:</b> This operation is irreversible and deletes all data.</para>
    /// </remarks>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the vector store and validates configuration.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// <para><b>Flow:</b> Creates database tables/collections if needed,
    /// validates embedding provider connectivity, and prepares for operations.</para>
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
```

**File: `src/TechieRag/Abstractions/IVectorStore.cs`**
```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for vector database storage operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for storing and retrieving vector embeddings
/// across different vector database implementations (SQLite-vec, PGVector, Qdrant).</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder based on configuration. Called by
/// TechieRagClient during ingestion (UpsertAsync) and search (SearchAsync) operations.</para>
/// <para><b>Implementations:</b> SqliteVecStore, PgVectorStore, QdrantStore</para>
/// </remarks>
public interface IVectorStore
{
    /// <summary>
    /// Gets the display name of this vector store implementation.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the vector store, creating tables/collections if needed.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a single text chunk with its vector embedding.
    /// </summary>
    /// <param name="chunk">The chunk containing text, vector, and metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the upserted chunk.</returns>
    Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates multiple chunks in a batch operation for efficiency.
    /// </summary>
    /// <param name="chunks">Collection of chunks to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of IDs for all upserted chunks.</returns>
    Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs vector similarity search to find chunks most similar to the query vector.
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked search results ordered by similarity score.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific chunk by its ID.
    /// </summary>
    /// <param name="chunkId">The ID of the chunk to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks belonging to a specific document.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be deleted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all documents in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of documents with their metadata.</returns>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves statistics about the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including counts and storage size.</returns>
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all data from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
```

**File: `src/TechieRag/Abstractions/IEmbeddingProvider.cs`**
```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for text embedding generation services.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for converting text into vector embeddings
/// across different embedding providers (ONNX, Ollama, LM Studio, Azure OpenAI).</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder based on configuration. Called by
/// TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>Implementations:</b> OnnxEmbeddingProvider, OllamaEmbeddingProvider,
/// LmStudioEmbeddingProvider, AzureOpenAIEmbeddingProvider</para>
/// </remarks>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Gets the display name of this embedding provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the name of the embedding model being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets the dimensionality of the embedding vectors produced.
    /// </summary>
    /// <remarks>BGE-M3 produces 1024-dimensional vectors.</remarks>
    int Dimensions { get; }

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch operation.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Batch operations are more efficient for multiple texts
    /// as they reduce API call overhead.</para>
    /// </remarks>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised after each embedding operation completes, for telemetry purposes.
    /// </summary>
    event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
}

/// <summary>
/// Event arguments for embedding completion telemetry.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides metrics about embedding operations for logging,
/// monitoring, and token usage tracking.</para>
/// </remarks>
public class EmbeddingCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the approximate number of tokens processed.
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Gets the number of text inputs embedded.
    /// </summary>
    public required int TextCount { get; init; }

    /// <summary>
    /// Gets the duration of the embedding operation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the name of the embedding model used.
    /// </summary>
    public required string ModelName { get; init; }

    /// <summary>
    /// Gets the name of the embedding provider used.
    /// </summary>
    public required string ProviderName { get; init; }
}
```

**File: `src/TechieRag/Abstractions/IDocumentProcessor.cs`**
```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for document parsing and chunking operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for extracting text from various document
/// formats and splitting them into chunks suitable for embedding.</para>
/// <para><b>Code Flow:</b> TechieRagClient selects the appropriate processor based on file extension,
/// then calls ProcessAsync to extract and chunk the document content.</para>
/// <para><b>Implementations:</b> PdfProcessor, DocxProcessor, TextProcessor, MarkdownProcessor,
/// HtmlProcessor, JsonProcessor, TomlProcessor, CodeProcessor</para>
/// </remarks>
public interface IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports (e.g., ".pdf", ".docx").
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Processes a document stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The document content stream.</param>
    /// <param name="fileName">The original file name (used for metadata and extension detection).</param>
    /// <param name="options">Optional processing configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the document.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> 1) Parse document format, 2) Extract text content,
    /// 3) Split into semantic chunks, 4) Return with page/position metadata.</para>
    /// </remarks>
    Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for document processing operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Allows customization of chunking behavior per document or globally.</para>
/// </remarks>
public class DocumentProcessingOptions
{
    /// <summary>
    /// Gets or sets the maximum size of each text chunk in characters.
    /// </summary>
    /// <remarks>Default is 500 characters, balancing context and retrieval precision.</remarks>
    public int MaxChunkSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the number of overlapping characters between consecutive chunks.
    /// </summary>
    /// <remarks>Overlap helps maintain context across chunk boundaries.</remarks>
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>
    /// Gets or sets the language hint for language-specific processing.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets additional metadata to attach to all chunks from this document.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
```

#### Step 1.3: Define Core Models

**File: `src/TechieRag/Models/TextChunk.cs`**
```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents a chunk of text extracted from a document with its vector embedding.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Core data structure for storing document content. Each document is split
/// into multiple chunks for more precise retrieval.</para>
/// <para><b>Code Flow:</b> Created by IDocumentProcessor during ingestion. Vector is populated
/// by IEmbeddingProvider. Stored and retrieved via IVectorStore.</para>
/// </remarks>
public class TextChunk
{
    /// <summary>
    /// Gets or sets the unique identifier for this chunk.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the ID of the parent document this chunk belongs to.
    /// </summary>
    public required string DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the text content of this chunk.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Gets or sets the vector embedding of the text (populated during ingestion).
    /// </summary>
    public float[]? Vector { get; set; }

    /// <summary>
    /// Gets or sets the page number in the source document (if applicable).
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the sequential index of this chunk within the document.
    /// </summary>
    public int? ChunkIndex { get; set; }

    /// <summary>
    /// Gets or sets additional metadata associated with this chunk.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this chunk was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**File: `src/TechieRag/Models/Document.cs`**
```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents an ingested document in the vector store.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Tracks document-level metadata and provides a reference for
/// managing all chunks belonging to a document.</para>
/// </remarks>
public class Document
{
    /// <summary>
    /// Gets the unique identifier for this document.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of the document.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the original file path or source URL of the document.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets the number of chunks this document was split into.
    /// </summary>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Gets the timestamp when this document was ingested.
    /// </summary>
    public DateTime IngestedAt { get; init; }

    /// <summary>
    /// Gets additional metadata associated with the document.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

**File: `src/TechieRag/Models/SearchResult.cs`**
```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents a single search result from a semantic search operation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pairs a matched chunk with its relevance score for ranking results.</para>
/// </remarks>
public class SearchResult
{
    /// <summary>
    /// Gets the matched text chunk.
    /// </summary>
    public required TextChunk Chunk { get; init; }

    /// <summary>
    /// Gets the similarity score (0-1, higher is more relevant).
    /// </summary>
    public required float Score { get; init; }
}
```

**File: `src/TechieRag/Models/IngestionStats.cs`**
```csharp
namespace TechieRag.Models;

/// <summary>
/// Statistics about the current state of the vector store.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides metrics for monitoring and displaying
/// ingestion status in the UI.</para>
/// </remarks>
public class IngestionStats
{
    /// <summary>
    /// Gets the total number of documents in the vector store.
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Gets the total number of chunks across all documents.
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Gets the approximate storage size in bytes.
    /// </summary>
    public long VectorStoreSizeBytes { get; init; }

    /// <summary>
    /// Gets the timestamp of the most recent ingestion operation.
    /// </summary>
    public DateTime? LastIngestionTime { get; init; }

    /// <summary>
    /// Gets the name of the vector store implementation in use.
    /// </summary>
    public string VectorStoreName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the embedding provider in use.
    /// </summary>
    public string EmbeddingProviderName { get; init; } = string.Empty;
}
```

---

### Phase 2: Configuration System (Day 1)

#### Step 2.1: Configuration Classes

**File: `src/TechieRag/TechieRagConfig.cs`**
```csharp
namespace TechieRag;

/// <summary>
/// Root configuration object for TechieRag library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Centralizes all configuration options for embedding, vector storage,
/// and document processing. Can be populated via fluent builder, object initializer, or appsettings.json.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder or bound from IConfiguration.
/// Passed to TechieRagClient and used to instantiate providers.</para>
/// </remarks>
public class TechieRagConfig
{
    /// <summary>
    /// Gets or sets the embedding provider configuration.
    /// </summary>
    public EmbeddingConfig Embedding { get; set; } = new();

    /// <summary>
    /// Gets or sets the vector store configuration.
    /// </summary>
    public VectorStoreConfig VectorStore { get; set; } = new();

    /// <summary>
    /// Gets or sets the document processing configuration.
    /// </summary>
    public ProcessingConfig Processing { get; set; } = new();

    /// <summary>
    /// Gets or sets whether telemetry (logging, metrics) is enabled.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Internal logger factory set by the builder or DI container.
    /// </summary>
    internal ILoggerFactory? LoggerFactory { get; set; }
}

/// <summary>
/// Configuration for embedding provider selection and settings.
/// </summary>
public class EmbeddingConfig
{
    /// <summary>
    /// Gets or sets the embedding source type.
    /// </summary>
    public EmbeddingSource Source { get; set; } = EmbeddingSource.Ollama;

    /// <summary>
    /// Gets or sets the API endpoint URL (for Ollama, LM Studio, Azure OpenAI).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key (for Azure OpenAI, OpenAI).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the embedding model name.
    /// </summary>
    public string Model { get; set; } = "bge-m3";

    /// <summary>
    /// Gets or sets the local model file path (for ONNX).
    /// </summary>
    public string? ModelPath { get; set; }
}

/// <summary>
/// Configuration for vector store selection and connection.
/// </summary>
public class VectorStoreConfig
{
    /// <summary>
    /// Gets or sets the vector store type.
    /// </summary>
    public VectorStoreType Type { get; set; } = VectorStoreType.SqliteVec;

    /// <summary>
    /// Gets or sets the connection string or endpoint URL for the vector store.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=techierag.db";
}

/// <summary>
/// Configuration for document processing and chunking.
/// </summary>
public class ProcessingConfig
{
    /// <summary>
    /// Gets or sets the default chunk size in characters.
    /// </summary>
    public int DefaultChunkSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the default overlap between chunks in characters.
    /// </summary>
    public int DefaultChunkOverlap { get; set; } = 50;
}

/// <summary>
/// Supported embedding provider sources.
/// </summary>
public enum EmbeddingSource
{
    /// <summary>Local ONNX model inference.</summary>
    Onnx,
    /// <summary>Ollama local model server.</summary>
    Ollama,
    /// <summary>LM Studio local model server.</summary>
    LmStudio,
    /// <summary>Azure OpenAI cloud service.</summary>
    AzureOpenAI,
    /// <summary>OpenAI cloud service.</summary>
    OpenAI
}

/// <summary>
/// Supported vector database types.
/// </summary>
public enum VectorStoreType
{
    /// <summary>SQLite with sqlite-vec extension (embedded).</summary>
    SqliteVec,
    /// <summary>PostgreSQL with pgvector extension.</summary>
    PgVector,
    /// <summary>Qdrant vector database.</summary>
    Qdrant
}
```

#### Step 2.2: Fluent Builder

**File: `src/TechieRag/TechieRagBuilder.cs`**
```csharp
namespace TechieRag;

/// <summary>
/// Fluent builder for configuring and creating TechieRag instances.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a fluent API for configuring all aspects of TechieRag
/// including embedding providers, vector stores, and processing options.</para>
/// <para><b>Usage:</b> Chain configuration methods and call Build() to create an ITechieRag instance.</para>
/// <para><b>Example:</b></para>
/// <code>
/// var rag = new TechieRagBuilder()
///     .UseOllama()
///     .UseSqliteVec()
///     .Build();
/// </code>
/// </remarks>
public class TechieRagBuilder
{
    private readonly TechieRagConfig config = new();

    /// <summary>
    /// Configures the embedding provider with full control over all settings.
    /// </summary>
    /// <param name="source">The embedding source type.</param>
    /// <param name="endpoint">API endpoint URL (optional).</param>
    /// <param name="apiKey">API key for authentication (optional).</param>
    /// <param name="model">Model name (defaults to "bge-m3").</param>
    /// <param name="modelPath">Local model path for ONNX (optional).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseEmbedding(EmbeddingSource source, string? endpoint = null, string? apiKey = null, string? model = null, string? modelPath = null)
    {
        config.Embedding = new EmbeddingConfig
        {
            Source = source,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model ?? "bge-m3",
            ModelPath = modelPath
        };
        return this;
    }

    /// <summary>
    /// Configures Ollama as the embedding provider.
    /// </summary>
    /// <param name="endpoint">Ollama API endpoint (default: http://localhost:11434).</param>
    /// <param name="model">Model name (default: bge-m3).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseOllama(string endpoint = "http://localhost:11434", string model = "bge-m3")
        => UseEmbedding(EmbeddingSource.Ollama, endpoint, model: model);

    /// <summary>
    /// Configures LM Studio as the embedding provider.
    /// </summary>
    /// <param name="endpoint">LM Studio API endpoint (default: http://localhost:1234).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseLmStudio(string endpoint = "http://localhost:1234")
        => UseEmbedding(EmbeddingSource.LmStudio, endpoint);

    /// <summary>
    /// Configures local ONNX model as the embedding provider.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model directory.</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseOnnx(string modelPath)
        => UseEmbedding(EmbeddingSource.Onnx, modelPath: modelPath);

    /// <summary>
    /// Configures Azure OpenAI as the embedding provider.
    /// </summary>
    /// <param name="endpoint">Azure OpenAI endpoint URL.</param>
    /// <param name="apiKey">API key for authentication.</param>
    /// <param name="model">Deployment/model name (default: text-embedding-3-small).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseAzureOpenAI(string endpoint, string apiKey, string model = "text-embedding-3-small")
        => UseEmbedding(EmbeddingSource.AzureOpenAI, endpoint, apiKey, model);

    /// <summary>
    /// Configures the vector store with full control over settings.
    /// </summary>
    /// <param name="type">The vector store type.</param>
    /// <param name="connectionString">Connection string or endpoint URL.</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseVectorStore(VectorStoreType type, string connectionString)
    {
        config.VectorStore = new VectorStoreConfig
        {
            Type = type,
            ConnectionString = connectionString
        };
        return this;
    }

    /// <summary>
    /// Configures SQLite-vec as the vector store (embedded, zero-config).
    /// </summary>
    /// <param name="databasePath">Path to SQLite database file (default: techierag.db).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseSqliteVec(string databasePath = "techierag.db")
        => UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={databasePath}");

    /// <summary>
    /// Configures PostgreSQL with pgvector as the vector store.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UsePgVector(string connectionString)
        => UseVectorStore(VectorStoreType.PgVector, connectionString);

    /// <summary>
    /// Configures Qdrant as the vector store.
    /// </summary>
    /// <param name="endpoint">Qdrant API endpoint (default: http://localhost:6334).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder UseQdrant(string endpoint = "http://localhost:6334")
        => UseVectorStore(VectorStoreType.Qdrant, endpoint);

    /// <summary>
    /// Configures document chunking parameters.
    /// </summary>
    /// <param name="size">Maximum chunk size in characters.</param>
    /// <param name="overlap">Overlap between chunks in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder WithChunkSize(int size, int overlap = 50)
    {
        config.Processing.DefaultChunkSize = size;
        config.Processing.DefaultChunkOverlap = overlap;
        return this;
    }

    /// <summary>
    /// Configures the logger factory for diagnostic logging.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        config.LoggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Enables or disables telemetry collection.
    /// </summary>
    /// <param name="enabled">True to enable telemetry (default).</param>
    /// <returns>This builder for method chaining.</returns>
    public TechieRagBuilder WithTelemetry(bool enabled = true)
    {
        config.EnableTelemetry = enabled;
        return this;
    }

    /// <summary>
    /// Builds and returns a configured ITechieRag instance.
    /// </summary>
    /// <returns>A fully configured TechieRag client ready for use.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Creates vector store, embedding provider, and document processors
    /// based on configuration, then assembles them into a TechieRagClient.</para>
    /// </remarks>
    public ITechieRag Build()
    {
        var vectorStore = CreateVectorStore();
        var embeddingProvider = CreateEmbeddingProvider();
        var processors = CreateProcessors();
        var logger = config.LoggerFactory?.CreateLogger<TechieRagClient>()
            ?? NullLogger<TechieRagClient>.Instance;

        return new TechieRagClient(vectorStore, embeddingProvider, processors, config, logger);
    }

    /// <summary>
    /// Returns the current configuration object for inspection or serialization.
    /// </summary>
    /// <returns>The configuration object.</returns>
    public TechieRagConfig GetConfig() => config;

    /// <summary>
    /// Creates the appropriate vector store based on configuration.
    /// </summary>
    private IVectorStore CreateVectorStore() => config.VectorStore.Type switch
    {
        VectorStoreType.SqliteVec => new SqliteVecStore(config.VectorStore.ConnectionString),
        VectorStoreType.PgVector => new PgVectorStore(config.VectorStore.ConnectionString),
        VectorStoreType.Qdrant => new QdrantStore(config.VectorStore.ConnectionString),
        _ => throw new NotSupportedException($"Vector store {config.VectorStore.Type} not supported")
    };

    /// <summary>
    /// Creates the appropriate embedding provider based on configuration.
    /// </summary>
    private IEmbeddingProvider CreateEmbeddingProvider() => config.Embedding.Source switch
    {
        EmbeddingSource.Ollama => new OllamaEmbeddingProvider(config.Embedding.Endpoint!, config.Embedding.Model),
        EmbeddingSource.LmStudio => new LmStudioEmbeddingProvider(config.Embedding.Endpoint!),
        EmbeddingSource.Onnx => new OnnxEmbeddingProvider(config.Embedding.ModelPath!),
        EmbeddingSource.AzureOpenAI => new AzureOpenAIEmbeddingProvider(config.Embedding.Endpoint!, config.Embedding.ApiKey!, config.Embedding.Model),
        _ => throw new NotSupportedException($"Embedding source {config.Embedding.Source} not supported")
    };

    /// <summary>
    /// Creates all document processor instances.
    /// </summary>
    private IEnumerable<IDocumentProcessor> CreateProcessors() =>
    [
        new PdfProcessor(),
        new DocxProcessor(),
        new TextProcessor(),
        new MarkdownProcessor(),
        new HtmlProcessor(),
        new JsonProcessor(),
        new TomlProcessor(),
        new CodeProcessor()
    ];
}
```

#### Step 2.3: Dependency Injection Extensions

**File: `src/TechieRag/DependencyInjection/ServiceCollectionExtensions.cs`**
```csharp
namespace TechieRag.DependencyInjection;

/// <summary>
/// Extension methods for registering TechieRag services with dependency injection.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides convenient methods to add TechieRag to an ASP.NET Core
/// or generic .NET host application's service collection.</para>
/// <para><b>Usage:</b> Call AddTechieRag in Program.cs or Startup.cs to register ITechieRag.</para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TechieRag services using a fluent builder configuration.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Action to configure the TechieRag builder.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para><b>Example:</b></para>
    /// <code>
    /// services.AddTechieRag(builder => builder
    ///     .UseOllama()
    ///     .UseSqliteVec());
    /// </code>
    /// </remarks>
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

    /// <summary>
    /// Adds TechieRag services using configuration from IConfiguration (e.g., appsettings.json).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration section containing TechieRag settings.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration section is missing.</exception>
    /// <remarks>
    /// <para><b>Example appsettings.json:</b></para>
    /// <code>
    /// {
    ///   "TechieRag": {
    ///     "Embedding": { "Source": "Ollama", "Endpoint": "http://localhost:11434" },
    ///     "VectorStore": { "Type": "SqliteVec", "ConnectionString": "Data Source=rag.db" }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddTechieRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var config = configuration.Get<TechieRagConfig>()
            ?? throw new InvalidOperationException("TechieRag configuration section not found");

        services.AddSingleton(config);

        return services.AddTechieRag(builder =>
        {
            builder.UseEmbedding(
                config.Embedding.Source,
                config.Embedding.Endpoint,
                config.Embedding.ApiKey,
                config.Embedding.Model,
                config.Embedding.ModelPath);
            builder.UseVectorStore(config.VectorStore.Type, config.VectorStore.ConnectionString);
            builder.WithChunkSize(config.Processing.DefaultChunkSize, config.Processing.DefaultChunkOverlap);
            builder.WithTelemetry(config.EnableTelemetry);
        });
    }
}
```

---

### Phase 3: Vector Store Providers (Days 2-3)

#### Step 3.1: SQLite-vec Provider (Primary - implement first)

Reference ChatAppEx patterns but write fresh implementation.

```csharp
// src/TechieRag/VectorStores/SqliteVecStore.cs
namespace TechieRag.VectorStores;

/// <summary>
/// SQLite-vec vector store implementation for embedded database scenarios.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a zero-configuration embedded vector database using SQLite
/// with the sqlite-vec extension for vector similarity search.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when VectorStoreType.SqliteVec is configured.
/// InitializeAsync creates tables on first use. UpsertAsync/SearchAsync handle vector operations.</para>
/// <para><b>Dependencies:</b> Requires sqlite-vec extension to be available.</para>
/// </remarks>
public class SqliteVecStore : IVectorStore
{
    /// <inheritdoc/>
    public string Name => "SQLite-vec";

    private readonly string connectionString;
    private readonly int dimensions;
    private bool initialized;

    /// <summary>
    /// Creates a new SQLite-vec vector store instance.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <param name="dimensions">Vector dimensions (default: 1024 for BGE-M3).</param>
    public SqliteVecStore(string connectionString, int dimensions = 1024)
    {
        this.connectionString = connectionString;
        this.dimensions = dimensions;
    }

    /// <summary>
    /// Initializes the database schema, creating tables if they don't exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// <para><b>Tables Created:</b></para>
    /// <list type="bullet">
    /// <item>Documents - stores document metadata</item>
    /// <item>Chunks - stores text chunks with references to documents</item>
    /// <item>ChunksVec - virtual table for vector similarity search</item>
    /// </list>
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Load sqlite-vec extension
        connection.LoadExtension("vec0");

        // Create tables (using PascalCase for all identifiers)
        await connection.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                ChunkCount INTEGER DEFAULT 0,
                IngestedAt TEXT NOT NULL,
                Metadata TEXT
            );

            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                DocumentId TEXT NOT NULL,
                Text TEXT NOT NULL,
                PageNumber INTEGER,
                ChunkIndex INTEGER,
                Metadata TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS ChunksVec USING vec0(
                Id TEXT PRIMARY KEY,
                Embedding float[{dimensions}]
            );

            CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId);
        ");

        initialized = true;
    }

    /// <summary>
    /// Performs vector similarity search against stored embeddings.
    /// </summary>
    /// <param name="queryVector">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to filter results.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked list of matching chunks with similarity scores.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b> Uses sqlite-vec's MATCH operator for approximate nearest neighbor search.
    /// Results are sorted by distance (lower is better) and converted to similarity scores (higher is better).</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        connection.LoadExtension("vec0");

        var sql = documentFilter is null
            ? @"SELECT c.*, v.distance
                FROM ChunksVec v
                JOIN Chunks c ON c.Id = v.Id
                WHERE v.Embedding MATCH @embedding
                ORDER BY v.distance
                LIMIT @topK"
            : @"SELECT c.*, v.distance
                FROM ChunksVec v
                JOIN Chunks c ON c.Id = v.Id
                WHERE v.Embedding MATCH @embedding AND c.DocumentId = @documentFilter
                ORDER BY v.distance
                LIMIT @topK";

        var results = await connection.QueryAsync<ChunkRow>(sql, new
        {
            embedding = SerializeVector(queryVector),
            topK,
            documentFilter
        });

        return results.Select(r => new SearchResult
        {
            Chunk = r.ToTextChunk(),
            Score = 1 - r.Distance
        }).ToList();
    }

    // ... implement other IVectorStore methods

    /// <summary>
    /// Serializes a float array to bytes for sqlite-vec storage.
    /// </summary>
    /// <param name="vector">The vector to serialize.</param>
    /// <returns>Byte array representation of the vector.</returns>
    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
```

#### Step 3.2: PGVector Provider

```csharp
// src/TechieRag/VectorStores/PgVectorStore.cs
// Similar pattern to SqliteVecStore, using Npgsql and pgvector extension
```

#### Step 3.3: Qdrant Provider

```csharp
// src/TechieRag/VectorStores/QdrantStore.cs
// Use Qdrant.Client NuGet package
```

---

### Phase 4: Document Processors + Embedding Providers (Days 2-4)

#### Step 4.1: PDF Processor (Reference ChatAppEx pattern)

```csharp
// src/TechieRag/Processors/PdfProcessor.cs
namespace TechieRag.Processors;

/// <summary>
/// Document processor for PDF files using PdfPig library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts text content from PDF files and splits into chunks
/// suitable for embedding and semantic search.</para>
/// <para><b>Code Flow:</b> Called by TechieRagClient when ingesting .pdf files.
/// Uses PdfPig for text extraction and custom chunking for splitting.</para>
/// <para><b>Dependencies:</b> PdfPig NuGet package for PDF parsing.</para>
/// </remarks>
public class PdfProcessor : IDocumentProcessor
{
    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions => [".pdf"];

    /// <summary>
    /// Processes a PDF document and returns text chunks.
    /// </summary>
    /// <param name="content">The PDF file stream.</param>
    /// <param name="fileName">Original file name for metadata.</param>
    /// <param name="options">Processing options (chunk size, overlap).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks with page numbers and metadata.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item>Open PDF using PdfPig</item>
    /// <item>For each page, extract text using word and block detection</item>
    /// <item>Split page text into chunks based on configured size/overlap</item>
    /// <item>Attach page number and source file metadata to each chunk</item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();

        // Use PdfPig (same as ChatAppEx reference)
        using var pdf = PdfDocument.Open(content);

        foreach (var page in pdf.GetPages())
        {
            var pageText = ExtractPageText(page);
            var pageChunks = ChunkText(pageText, options.MaxChunkSize, options.ChunkOverlap);

            foreach (var (text, index) in pageChunks.Select((t, i) => (t, i)))
            {
                chunks.Add(new TextChunk
                {
                    Text = text,
                    PageNumber = page.Number,
                    ChunkIndex = index,
                    DocumentId = string.Empty, // Set by TechieRagClient
                    Metadata = new Dictionary<string, object>
                    {
                        ["SourceFile"] = fileName,
                        ["Page"] = page.Number
                    }
                });
            }
        }

        return await Task.FromResult(chunks);
    }

    /// <summary>
    /// Extracts text from a PDF page using word and block detection.
    /// </summary>
    /// <param name="page">The PDF page to extract text from.</param>
    /// <returns>Extracted text with paragraphs separated by newlines.</returns>
    private static string ExtractPageText(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        return string.Join("\n\n", textBlocks.Select(t => t.Text.ReplaceLineEndings(" ")));
    }

    /// <summary>
    /// Splits text into chunks of specified size with overlap.
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="maxSize">Maximum chunk size in characters.</param>
    /// <param name="overlap">Number of overlapping characters between chunks.</param>
    /// <returns>Enumerable of text chunks.</returns>
    private static IEnumerable<string> ChunkText(string text, int maxSize, int overlap)
    {
        // Implement chunking (can reference Semantic Kernel TextChunker)
        // ...
    }
}
```

#### Step 4.2: Other Processors

- DocxProcessor (DocumentFormat.OpenXml)
- TextProcessor (simple line-based chunking)
- MarkdownProcessor (preserve structure)
- HtmlProcessor (strip tags, semantic chunking)
- JsonProcessor (structure-aware)
- TomlProcessor (structure-aware)
- CodeProcessor (syntax-aware, by function/class)

#### Step 4.3: Embedding Providers

```csharp
// src/TechieRag/Embedding/OllamaEmbeddingProvider.cs
namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider implementation for Ollama local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Generates text embeddings using models running on a local Ollama server,
/// enabling offline operation and privacy-sensitive scenarios.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when EmbeddingSource.Ollama is configured.
/// Called by TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>Dependencies:</b> Requires Ollama to be running locally with an embedding model pulled.</para>
/// </remarks>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc/>
    public string Name => "Ollama";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public int Dimensions => 1024; // BGE-M3 default

    private readonly HttpClient httpClient;
    private readonly string endpoint;

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a new Ollama embedding provider instance.
    /// </summary>
    /// <param name="endpoint">Ollama API endpoint (e.g., http://localhost:11434).</param>
    /// <param name="model">Model name to use for embeddings (default: bge-m3).</param>
    public OllamaEmbeddingProvider(string endpoint, string model = "bge-m3")
    {
        this.endpoint = endpoint.TrimEnd('/');
        ModelName = model;
        httpClient = new HttpClient { BaseAddress = new Uri(this.endpoint) };
    }

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Sends POST request to Ollama's /api/embeddings endpoint,
    /// receives vector response, and raises telemetry event.</para>
    /// </remarks>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        var request = new { model = ModelName, prompt = text };
        var response = await httpClient.PostAsJsonAsync("/api/embeddings", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken);

        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = text.Split(' ').Length, // Approximate token count
            TextCount = 1,
            Duration = DateTime.UtcNow - startTime,
            ModelName = ModelName,
            ProviderName = Name
        });

        return result!.Embedding;
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts sequentially.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <remarks>
    /// <para><b>Note:</b> Ollama processes embeddings one at a time. For better performance
    /// with many texts, consider using a batch-capable provider.</para>
    /// </remarks>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await EmbedAsync(text, cancellationToken));
        }
        return results;
    }

    /// <summary>
    /// Response model for Ollama embedding API.
    /// </summary>
    private record OllamaEmbeddingResponse(float[] Embedding);
}
```

---

### Phase 5: TechieRagWeb Sample Application (Days 4-5)

This is the **showcase application** demonstrating all TechieRag capabilities.

#### Step 5.1: Project Structure

```
samples/TechieRagWeb/
├── TechieRagWeb.csproj
├── Program.cs
├── appsettings.json
├── Components/
│   ├── App.razor
│   ├── Routes.razor
│   ├── _Imports.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   ├── NavMenu.razor
│   │   └── NavMenu.razor.css
│   └── Pages/
│       ├── Home.razor                  # Landing page with navigation
│       ├── Settings.razor              # Full configuration UI
│       ├── Ingestion.razor             # Manual ingestion with path selection
│       └── Chat.razor                  # RAG-powered chat
└── Services/
    └── TechieRagConfigService.cs       # Runtime config management
```

#### Step 5.2: Settings Page

**File: `samples/TechieRagWeb/Components/Pages/Settings.razor`**
```razor
@page "/settings"
@inject TechieRagConfigService ConfigService
@inject ILogger<Settings> Logger

<PageTitle>TechieRag Settings</PageTitle>

<div class="container mx-auto p-6 max-w-4xl">
    <h1 class="text-2xl font-bold mb-6">TechieRag Configuration</h1>

    <div class="space-y-6">
        <!-- Embedding Configuration -->
        <div class="bg-white rounded-lg shadow p-6">
            <h2 class="text-lg font-semibold mb-4">Embedding Configuration</h2>

            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label class="block text-sm font-medium mb-1">Source</label>
                    <select @bind="config.Embedding.Source" class="w-full border rounded p-2">
                        <option value="@EmbeddingSource.Ollama">Ollama (Local)</option>
                        <option value="@EmbeddingSource.LmStudio">LM Studio (Local)</option>
                        <option value="@EmbeddingSource.Onnx">ONNX (Bundled)</option>
                        <option value="@EmbeddingSource.AzureOpenAI">Azure OpenAI (Cloud)</option>
                    </select>
                </div>

                <div>
                    <label class="block text-sm font-medium mb-1">Model</label>
                    <input type="text" @bind="config.Embedding.Model"
                           class="w-full border rounded p-2" placeholder="bge-m3" />
                </div>

                <div class="col-span-2">
                    <label class="block text-sm font-medium mb-1">
                        @(config.Embedding.Source == EmbeddingSource.Onnx ? "Model Path" : "Endpoint")
                    </label>
                    <input type="text" @bind="endpointOrPath"
                           class="w-full border rounded p-2"
                           placeholder="@GetPlaceholder()" />
                </div>

                @if (config.Embedding.Source == EmbeddingSource.AzureOpenAI)
                {
                    <div class="col-span-2">
                        <label class="block text-sm font-medium mb-1">API Key</label>
                        <input type="password" @bind="config.Embedding.ApiKey"
                               class="w-full border rounded p-2" />
                    </div>
                }
            </div>
        </div>

        <!-- Vector Database Configuration -->
        <div class="bg-white rounded-lg shadow p-6">
            <h2 class="text-lg font-semibold mb-4">Vector Database Configuration</h2>

            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label class="block text-sm font-medium mb-1">Type</label>
                    <select @bind="config.VectorStore.Type" class="w-full border rounded p-2">
                        <option value="@VectorStoreType.SqliteVec">SQLite-vec (Embedded)</option>
                        <option value="@VectorStoreType.PgVector">PGVector (PostgreSQL)</option>
                        <option value="@VectorStoreType.Qdrant">Qdrant (Docker)</option>
                    </select>
                </div>

                <div>
                    <label class="block text-sm font-medium mb-1">Connection String</label>
                    <input type="text" @bind="config.VectorStore.ConnectionString"
                           class="w-full border rounded p-2"
                           placeholder="@GetConnectionStringPlaceholder()" />
                </div>
            </div>
        </div>

        <!-- Processing Configuration -->
        <div class="bg-white rounded-lg shadow p-6">
            <h2 class="text-lg font-semibold mb-4">Processing Configuration</h2>

            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label class="block text-sm font-medium mb-1">Chunk Size</label>
                    <input type="number" @bind="config.Processing.DefaultChunkSize"
                           class="w-full border rounded p-2" min="100" max="2000" />
                </div>

                <div>
                    <label class="block text-sm font-medium mb-1">Chunk Overlap</label>
                    <input type="number" @bind="config.Processing.DefaultChunkOverlap"
                           class="w-full border rounded p-2" min="0" max="500" />
                </div>
            </div>
        </div>

        <!-- Save Button -->
        <div class="flex justify-between items-center">
            <button @onclick="SaveConfigAsync"
                    class="bg-blue-600 text-white px-6 py-2 rounded hover:bg-blue-700">
                Save Configuration
            </button>

            @if (saveMessage is not null)
            {
                <span class="@(saveSuccess ? "text-green-600" : "text-red-600")">@saveMessage</span>
            }
        </div>
    </div>
</div>

@code {
    private TechieRagConfig config = new();
    private string? endpointOrPath;
    private string? saveMessage;
    private bool saveSuccess;

    protected override async Task OnInitializedAsync()
    {
        config = await ConfigService.LoadConfigAsync();
        endpointOrPath = config.Embedding.Source == EmbeddingSource.Onnx
            ? config.Embedding.ModelPath
            : config.Embedding.Endpoint;
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            if (config.Embedding.Source == EmbeddingSource.Onnx)
                config.Embedding.ModelPath = endpointOrPath;
            else
                config.Embedding.Endpoint = endpointOrPath;

            await ConfigService.SaveConfigAsync(config);
            saveMessage = "Configuration saved successfully!";
            saveSuccess = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save configuration");
            saveMessage = $"Error: {ex.Message}";
            saveSuccess = false;
        }
    }

    private string GetPlaceholder() => config.Embedding.Source switch
    {
        EmbeddingSource.Ollama => "http://localhost:11434",
        EmbeddingSource.LmStudio => "http://localhost:1234",
        EmbeddingSource.Onnx => "C:\\Models\\bge-m3-onnx",
        EmbeddingSource.AzureOpenAI => "https://your-resource.openai.azure.com",
        _ => ""
    };

    private string GetConnectionStringPlaceholder() => config.VectorStore.Type switch
    {
        VectorStoreType.SqliteVec => "Data Source=techierag.db",
        VectorStoreType.PgVector => "Host=localhost;Database=techierag;Username=postgres;Password=...",
        VectorStoreType.Qdrant => "http://localhost:6334",
        _ => ""
    };
}
```

#### Step 5.3: Ingestion Page

**File: `samples/TechieRagWeb/Components/Pages/Ingestion.razor`**
```razor
@page "/ingestion"
@inject ITechieRag Rag
@inject ILogger<Ingestion> Logger

<PageTitle>Document Ingestion</PageTitle>

<div class="container mx-auto p-6 max-w-4xl">
    <h1 class="text-2xl font-bold mb-6">Document Ingestion</h1>

    <!-- Ingestion Controls -->
    <div class="bg-white rounded-lg shadow p-6 mb-6">
        <h2 class="text-lg font-semibold mb-4">Ingest Documents</h2>

        <div class="space-y-4">
            <div>
                <label class="block text-sm font-medium mb-1">Documents Folder Path</label>
                <div class="flex gap-2">
                    <input type="text" @bind="documentsPath"
                           class="flex-1 border rounded p-2"
                           placeholder="C:\Documents\RAGData" />
                    <button @onclick="BrowseFolder"
                            class="bg-gray-200 px-4 py-2 rounded hover:bg-gray-300">
                        Browse...
                    </button>
                </div>
            </div>

            <div>
                <label class="block text-sm font-medium mb-1">File Pattern</label>
                <input type="text" @bind="filePattern"
                       class="w-full border rounded p-2"
                       placeholder="*.pdf,*.docx,*.txt" />
                <p class="text-xs text-gray-500 mt-1">
                    Supported: PDF, DOCX, TXT, MD, HTML, JSON, TOML, CS, JS, TS, PY
                </p>
            </div>

            <div class="flex gap-4">
                <button @onclick="IngestDocumentsAsync"
                        disabled="@isIngesting"
                        class="bg-green-600 text-white px-6 py-2 rounded hover:bg-green-700 disabled:bg-gray-400">
                    @(isIngesting ? "Ingesting..." : "Ingest Now")
                </button>

                <button @onclick="ClearVectorStoreAsync"
                        disabled="@isIngesting"
                        class="bg-red-600 text-white px-4 py-2 rounded hover:bg-red-700 disabled:bg-gray-400">
                    Clear All Data
                </button>
            </div>
        </div>

        <!-- Progress -->
        @if (isIngesting)
        {
            <div class="mt-4">
                <div class="w-full bg-gray-200 rounded-full h-2">
                    <div class="bg-blue-600 h-2 rounded-full transition-all"
                         style="width: @(progress)%"></div>
                </div>
                <p class="text-sm text-gray-600 mt-1">@progressMessage</p>
            </div>
        }

        <!-- Status Message -->
        @if (statusMessage is not null)
        {
            <div class="mt-4 p-3 rounded @(statusSuccess ? "bg-green-100 text-green-700" : "bg-red-100 text-red-700")">
                @statusMessage
            </div>
        }
    </div>

    <!-- Current Stats -->
    <div class="bg-white rounded-lg shadow p-6">
        <h2 class="text-lg font-semibold mb-4">Vector Store Statistics</h2>

        @if (stats is not null)
        {
            <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
                <div class="text-center p-4 bg-gray-50 rounded">
                    <div class="text-2xl font-bold text-blue-600">@stats.TotalDocuments</div>
                    <div class="text-sm text-gray-600">Documents</div>
                </div>
                <div class="text-center p-4 bg-gray-50 rounded">
                    <div class="text-2xl font-bold text-green-600">@stats.TotalChunks</div>
                    <div class="text-sm text-gray-600">Chunks</div>
                </div>
                <div class="text-center p-4 bg-gray-50 rounded">
                    <div class="text-2xl font-bold text-purple-600">@FormatSize(stats.VectorStoreSizeBytes)</div>
                    <div class="text-sm text-gray-600">Storage Size</div>
                </div>
                <div class="text-center p-4 bg-gray-50 rounded">
                    <div class="text-sm font-medium text-gray-700">@stats.VectorStoreName</div>
                    <div class="text-xs text-gray-500">@stats.EmbeddingProviderName</div>
                </div>
            </div>

            @if (stats.LastIngestionTime.HasValue)
            {
                <p class="text-sm text-gray-500 mt-4">
                    Last ingestion: @stats.LastIngestionTime.Value.ToString("g")
                </p>
            }
        }

        <button @onclick="RefreshStatsAsync" class="mt-4 text-blue-600 hover:underline text-sm">
            Refresh Statistics
        </button>
    </div>

    <!-- Document List -->
    <div class="bg-white rounded-lg shadow p-6 mt-6">
        <h2 class="text-lg font-semibold mb-4">Ingested Documents</h2>

        @if (documents.Any())
        {
            <table class="w-full">
                <thead>
                    <tr class="border-b">
                        <th class="text-left p-2">Name</th>
                        <th class="text-left p-2">Chunks</th>
                        <th class="text-left p-2">Ingested</th>
                        <th class="text-left p-2">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var doc in documents)
                    {
                        <tr class="border-b hover:bg-gray-50">
                            <td class="p-2">@doc.Name</td>
                            <td class="p-2">@doc.ChunkCount</td>
                            <td class="p-2">@doc.IngestedAt.ToString("g")</td>
                            <td class="p-2">
                                <button @onclick="() => DeleteDocumentAsync(doc.Id)"
                                        class="text-red-600 hover:underline text-sm">
                                    Delete
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
        else
        {
            <p class="text-gray-500">No documents ingested yet.</p>
        }
    </div>

    <!-- Navigation -->
    <div class="mt-6">
        <a href="/chat" class="bg-blue-600 text-white px-6 py-2 rounded hover:bg-blue-700 inline-block">
            Go to Chat →
        </a>
    </div>
</div>

@code {
    private string documentsPath = "";
    private string filePattern = "*.*";
    private bool isIngesting;
    private int progress;
    private string? progressMessage;
    private string? statusMessage;
    private bool statusSuccess;
    private IngestionStats? stats;
    private List<Document> documents = new();

    protected override async Task OnInitializedAsync()
    {
        await RefreshStatsAsync();
        await RefreshDocumentsAsync();
    }

    private async Task IngestDocumentsAsync()
    {
        if (string.IsNullOrWhiteSpace(documentsPath))
        {
            statusMessage = "Please enter a documents folder path.";
            statusSuccess = false;
            return;
        }

        if (!Directory.Exists(documentsPath))
        {
            statusMessage = "The specified folder does not exist.";
            statusSuccess = false;
            return;
        }

        isIngesting = true;
        progress = 0;
        statusMessage = null;

        try
        {
            progressMessage = "Scanning folder...";
            StateHasChanged();

            var documentIds = await Rag.IngestDirectoryAsync(documentsPath, filePattern);

            progress = 100;
            progressMessage = "Complete!";
            statusMessage = $"Successfully ingested {documentIds.Count} documents.";
            statusSuccess = true;

            await RefreshStatsAsync();
            await RefreshDocumentsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ingestion failed");
            statusMessage = $"Error: {ex.Message}";
            statusSuccess = false;
        }
        finally
        {
            isIngesting = false;
        }
    }

    private async Task ClearVectorStoreAsync()
    {
        try
        {
            await Rag.ClearAsync();
            statusMessage = "All data cleared successfully.";
            statusSuccess = true;
            await RefreshStatsAsync();
            await RefreshDocumentsAsync();
        }
        catch (Exception ex)
        {
            statusMessage = $"Error: {ex.Message}";
            statusSuccess = false;
        }
    }

    private async Task DeleteDocumentAsync(string documentId)
    {
        await Rag.DeleteDocumentAsync(documentId);
        await RefreshStatsAsync();
        await RefreshDocumentsAsync();
    }

    private async Task RefreshStatsAsync()
    {
        stats = await Rag.GetStatsAsync();
    }

    private async Task RefreshDocumentsAsync()
    {
        documents = (await Rag.ListDocumentsAsync()).ToList();
    }

    private void BrowseFolder()
    {
        // In a real app, would use a folder browser dialog
        // For Blazor Server, this would need JS interop or a file picker component
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
```

#### Step 5.4: Chat Page

Similar to ChatAppEx Chat.razor but using `ITechieRag` interface.

---

## NuGet Package Configuration

### TechieRag.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PackageId>TechieRag</PackageId>
    <Version>1.0.0</Version>
    <Authors>Techie Rathor</Authors>
    <Company>Techie Rathor</Company>
    <Description>Configurable RAG library for .NET - Embedding, Vector Storage, and Retrieval</Description>
    <PackageTags>rag;ai;embedding;vector;search;llm;retrieval;augmented;generation</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/techierathor/TechieRag</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core .NET -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />

    <!-- Document Processing -->
    <PackageReference Include="PdfPig" Version="0.1.13-alpha" />
    <PackageReference Include="DocumentFormat.OpenXml" Version="3.0.0" />
    <PackageReference Include="Tomlyn" Version="0.17.0" />

    <!-- Vector Stores -->
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
    <PackageReference Include="Npgsql" Version="9.0.0" />
    <PackageReference Include="Qdrant.Client" Version="1.9.0" />
    <PackageReference Include="Dapper" Version="2.1.0" />

    <!-- Telemetry -->
    <PackageReference Include="OpenTelemetry.Api" Version="1.14.0" />
  </ItemGroup>
</Project>
```

---

## Summary: Revised Effort Estimation

| Phase | Work | Duration | Days |
|-------|------|----------|------|
| Phase 1 | Fresh solution + Core interfaces + Config | 1 day | Day 1 |
| Phase 2 | Vector Store providers (SQLite, PG, Qdrant) | 1.5 days | Days 2-3 |
| Phase 3 | Document processors (7 types) | 1 day | Days 2-3 |
| Phase 4 | Embedding providers (Ollama, Azure, ONNX) | 1 day | Days 3-4 |
| Phase 5 | TechieRagWeb (Settings + Ingestion + Chat) | 1.5 days | Days 4-5 |
| Phase 6 | Integration testing + Polish | 0.5-1 day | Days 5-6 |
| **Total** | **Complete TechieRag v1** | **5-6 days** | |

### Risk Factors
- ONNX integration complexity
- Vector store provider edge cases
- UI polish can expand scope

### Recommendation
**5-6 days is achievable** with focused execution. Consider deferring ONNX bundled provider to v1.1 if time is tight.

---

## Next Steps

1. Create `C:\2AIdeation\TechieRag` directory
2. Run solution creation commands from Phase 1
3. Implement core interfaces
4. Build SQLite-vec provider first (easiest to test)
5. Create TechieRagWeb with Settings + Ingestion pages
6. Add Chat page for end-to-end testing

---

*This roadmap provides a complete path to a production-ready TechieRag library built from scratch.*
