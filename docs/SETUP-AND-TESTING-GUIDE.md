# TechieRag Setup, Usage, and Testing Guide

This guide covers everything you need to get TechieRag running and test all its features.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Start](#quick-start)
3. [Setting Up Embedding Providers](#setting-up-embedding-providers)
4. [Using TechieRag Library](#using-techierag-library)
5. [Running TechieRagWeb Sample](#running-techieragweb-sample)
6. [Testing Scenarios](#testing-scenarios)
7. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### Required Software

| Software | Version | Purpose |
|----------|---------|---------|
| .NET SDK | 10.0+ | Build and run the solution |
| Visual Studio 2026 / VS Code | Latest | IDE (optional) |
| Git | Any | Clone repository |

### Optional (for embedding providers)

| Software | Purpose | Download |
|----------|---------|----------|
| Ollama | Local embedding generation | https://ollama.ai |
| LM Studio | Alternative local embeddings | https://lmstudio.ai |
| Docker | For Qdrant vector database | https://docker.com |
| PostgreSQL | For PGVector store | https://postgresql.org |

---

## Quick Start

### 1. Clone and Build

```powershell
# Navigate to project directory
cd C:\3AIGenCode\TechieRag

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Verify build succeeded
# Expected: Build succeeded. 0 Warning(s) 0 Error(s)
```

### 2. Run the Sample Web Application

```powershell
# Run TechieRagWeb
dotnet run --project samples/TechieRagWeb/TechieRagWeb.csproj

# Open browser to: https://localhost:5001 or http://localhost:5000
```

---

## Setting Up Embedding Providers

### Option A: Ollama (Recommended for Local Development)

Ollama is the easiest way to run embeddings locally.

#### Step 1: Install Ollama

```powershell
# Download and install from https://ollama.ai
# Or use winget on Windows:
winget install Ollama.Ollama
```

#### Step 2: Pull the BGE-M3 Embedding Model

```powershell
# Start Ollama service (runs automatically on install)
ollama serve

# In another terminal, pull the embedding model
ollama pull bge-m3

# Verify the model is available
ollama list
# Should show: bge-m3:latest
```

#### Step 3: Verify Ollama is Running

```powershell
# Test the API endpoint
curl http://localhost:11434/api/tags

# Or in PowerShell:
Invoke-RestMethod -Uri "http://localhost:11434/api/tags"
```

#### Step 4: Test Embedding Generation

```powershell
# Test embedding endpoint
curl -X POST http://localhost:11434/api/embeddings -d '{
  "model": "bge-m3",
  "prompt": "Hello world"
}'

# Should return JSON with "embedding" array of 1024 floats
```

### Option B: LM Studio

#### Step 1: Install LM Studio

Download from https://lmstudio.ai and install.

#### Step 2: Load an Embedding Model

1. Open LM Studio
2. Go to "Discover" tab
3. Search for "bge-m3" or "nomic-embed-text"
4. Download the model
5. Go to "Local Server" tab
6. Load the model and start server on port 1234

#### Step 3: Configure TechieRag

```csharp
// In your code or appsettings.json
builder.UseLmStudio("http://localhost:1234");
```

### Option C: Azure OpenAI

#### Step 1: Create Azure OpenAI Resource

1. Go to Azure Portal
2. Create an Azure OpenAI resource
3. Deploy an embedding model (e.g., text-embedding-ada-002)
4. Get your endpoint URL and API key

#### Step 2: Configure TechieRag

```csharp
builder.UseAzureOpenAI(
    endpoint: "https://your-resource.openai.azure.com",
    apiKey: "your-api-key",
    model: "text-embedding-ada-002"
);
```

---

## Using TechieRag Library

### Basic Usage Pattern

```csharp
using TechieRag;
using TechieRag.DependencyInjection;

// Option 1: Fluent Builder
var rag = new TechieRagBuilder()
    .UseOllama("http://localhost:11434", "bge-m3")
    .UseSqliteVec("mydata.db")
    .WithChunkSize(500, 50)
    .Build();

// Initialize the vector store
await rag.InitializeAsync();

// Ingest a document
string docId = await rag.IngestAsync(@"C:\Documents\sample.pdf");
Console.WriteLine($"Ingested document: {docId}");

// Ingest a directory
var docIds = await rag.IngestDirectoryAsync(@"C:\Documents", "*.pdf");
Console.WriteLine($"Ingested {docIds.Count} documents");

// Search
var results = await rag.SearchAsync("What is machine learning?", topK: 5);
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:P1}");
    Console.WriteLine($"Text: {result.Chunk.Text}");
    Console.WriteLine($"Source: {result.Chunk.Metadata["SourceFile"]}");
    Console.WriteLine();
}

// Get stats
var stats = await rag.GetStatsAsync();
Console.WriteLine($"Documents: {stats.TotalDocuments}, Chunks: {stats.TotalChunks}");
```

### ASP.NET Core Integration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register TechieRag services
builder.Services.AddTechieRag(rag => rag
    .UseOllama()
    .UseSqliteVec("techierag.db"));

var app = builder.Build();

// Initialize on startup
using (var scope = app.Services.CreateScope())
{
    var techieRag = scope.ServiceProvider.GetRequiredService<ITechieRag>();
    await techieRag.InitializeAsync();
}

app.Run();
```

### Configuration via appsettings.json

```json
{
  "TechieRag": {
    "Embedding": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "bge-m3"
    },
    "VectorStore": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=techierag.db"
    },
    "Processing": {
      "DefaultChunkSize": 500,
      "DefaultChunkOverlap": 50
    },
    "EnableTelemetry": true
  }
}
```

```csharp
// Program.cs
builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));
```

---

## Running TechieRagWeb Sample

### Step 1: Ensure Ollama is Running

```powershell
# Check Ollama status
ollama list

# If not running, start it
ollama serve
```

### Step 2: Start the Web Application

```powershell
cd C:\3AIGenCode\TechieRag
dotnet run --project samples/TechieRagWeb/TechieRagWeb.csproj
```

### Step 3: Open in Browser

Navigate to: `https://localhost:5001` or `http://localhost:5000`

### Step 4: Configure Settings

1. Click **Settings** in the navigation
2. Verify embedding settings:
   - Source: Ollama
   - Endpoint: http://localhost:11434
   - Model: bge-m3
3. Verify vector store settings:
   - Type: SQLite-vec
   - Connection String: Data Source=techierag.db
4. Click **Save Configuration**

### Step 5: Ingest Documents

1. Click **Ingestion** in the navigation
2. Enter a folder path containing documents (e.g., `C:\Documents\TestDocs`)
3. Set file pattern (e.g., `*.pdf` or `*.*` for all)
4. Click **Ingest Now**
5. Wait for ingestion to complete
6. View statistics and document list

### Step 6: Test Search

1. Click **Chat** in the navigation
2. Enter a search query (e.g., "What is the main topic?")
3. Select Top-K results (5, 10, or 20)
4. Click **Search** or press Enter
5. View results with relevance scores and source attribution

---

## Testing Scenarios

### Test Scenario 1: Basic Ingestion and Search

**Purpose:** Verify end-to-end RAG pipeline works.

```powershell
# Create a test directory with sample files
mkdir C:\TechieRagTest
echo "Machine learning is a subset of artificial intelligence." > C:\TechieRagTest\ml.txt
echo "Natural language processing enables computers to understand human language." > C:\TechieRagTest\nlp.txt
echo "Deep learning uses neural networks with multiple layers." > C:\TechieRagTest\dl.txt
```

**Test Steps:**
1. Start TechieRagWeb
2. Go to Ingestion page
3. Enter path: `C:\TechieRagTest`
4. Pattern: `*.txt`
5. Click Ingest Now
6. Verify: 3 documents ingested
7. Go to Chat page
8. Search: "What is machine learning?"
9. Expected: ml.txt content should rank highest

### Test Scenario 2: PDF Ingestion

**Purpose:** Verify PDF processing works.

**Prerequisites:** Have a PDF file available.

**Test Steps:**
1. Copy a PDF to test directory
2. Ingest with pattern `*.pdf`
3. Verify document appears in list with chunk count > 0
4. Search for content you know is in the PDF
5. Verify results show correct page numbers

### Test Scenario 3: Multiple Document Types

**Purpose:** Verify all processors work.

**Test Files to Create:**
- `test.txt` - Plain text
- `test.md` - Markdown
- `test.json` - JSON data
- `test.html` - HTML page
- `test.cs` - C# source code

**Test Steps:**
1. Ingest directory with `*.*` pattern
2. Verify all 5 documents ingested
3. Check chunk counts are reasonable
4. Search and verify results from different file types

### Test Scenario 4: Vector Store Persistence

**Purpose:** Verify data persists across restarts.

**Test Steps:**
1. Ingest some documents
2. Note the document count and chunk count
3. Stop the application
4. Restart the application
5. Go to Ingestion page
6. Verify stats match previous values
7. Search should still work

### Test Scenario 5: Clear and Re-ingest

**Purpose:** Verify clear functionality works.

**Test Steps:**
1. Verify you have documents ingested
2. Click "Clear All Data"
3. Verify stats show 0 documents, 0 chunks
4. Search should return no results
5. Re-ingest documents
6. Verify everything works again

### Test Scenario 6: Large Document Test

**Purpose:** Verify chunking works on large documents.

**Test Steps:**
1. Find or create a large document (50+ pages PDF or 10000+ words text)
2. Ingest the document
3. Check chunk count (should be > 20 for large docs)
4. Search for specific content
5. Verify results include page/chunk information

---

## Console Test Application

Create a simple console app to test the library directly:

```csharp
// TestConsole/Program.cs
using TechieRag;

Console.WriteLine("TechieRag Console Test");
Console.WriteLine("======================\n");

// Build TechieRag client
var rag = new TechieRagBuilder()
    .UseOllama("http://localhost:11434", "bge-m3")
    .UseSqliteVec("test.db")
    .Build();

// Initialize
Console.WriteLine("Initializing...");
await rag.InitializeAsync();
Console.WriteLine("Initialized!\n");

// Get initial stats
var stats = await rag.GetStatsAsync();
Console.WriteLine($"Current stats: {stats.TotalDocuments} docs, {stats.TotalChunks} chunks\n");

// Ingest text
Console.WriteLine("Ingesting sample text...");
var docId = await rag.IngestTextAsync(
    "TechieRag is a configurable RAG library for .NET. It supports multiple embedding providers including Ollama, LM Studio, and Azure OpenAI. Vector storage options include SQLite-vec, PostgreSQL with pgvector, and Qdrant.",
    "sample-doc",
    new Dictionary<string, object> { ["author"] = "test" }
);
Console.WriteLine($"Ingested document: {docId}\n");

// Search
Console.WriteLine("Searching for 'embedding providers'...\n");
var results = await rag.SearchAsync("embedding providers", topK: 3);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:P1}");
    Console.WriteLine($"Text: {result.Chunk.Text[..Math.Min(200, result.Chunk.Text.Length)]}...");
    Console.WriteLine();
}

// Final stats
stats = await rag.GetStatsAsync();
Console.WriteLine($"Final stats: {stats.TotalDocuments} docs, {stats.TotalChunks} chunks");

Console.WriteLine("\nTest complete! Press any key to exit.");
Console.ReadKey();
```

Run with:
```powershell
dotnet new console -n TestConsole -o tests/TestConsole
cd tests/TestConsole
dotnet add reference ../../src/TechieRag/TechieRag.csproj
# Add the Program.cs content above
dotnet run
```

---

## Troubleshooting

### Issue: "Connection refused" when using Ollama

**Cause:** Ollama service not running.

**Solution:**
```powershell
# Start Ollama
ollama serve

# Verify it's running
curl http://localhost:11434/api/tags
```

### Issue: "Model not found" error

**Cause:** Embedding model not pulled.

**Solution:**
```powershell
# Pull the model
ollama pull bge-m3

# Verify
ollama list
```

### Issue: Empty search results

**Causes:**
1. No documents ingested
2. Query doesn't match content
3. Vector store not initialized

**Solutions:**
1. Check Ingestion page for document count
2. Try broader search terms
3. Restart application and check initialization

### Issue: "No processor for extension" error

**Cause:** Unsupported file type.

**Supported Extensions:**
- PDF: `.pdf`
- Word: `.docx`
- Text: `.txt`
- Markdown: `.md`, `.markdown`
- HTML: `.html`, `.htm`
- JSON: `.json`
- TOML: `.toml`
- Code: `.cs`, `.js`, `.ts`, `.py`, `.java`, `.go`, `.rs`, `.cpp`, `.c`, `.h`, `.jsx`, `.tsx`

### Issue: Slow ingestion

**Causes:**
1. Large documents
2. Many documents
3. Slow embedding provider

**Solutions:**
1. Use local Ollama instead of cloud providers
2. Reduce chunk size for faster processing
3. Ingest in smaller batches

### Issue: SQLite database locked

**Cause:** Multiple processes accessing the database.

**Solution:**
1. Ensure only one application instance is running
2. Close any SQLite browsers/editors
3. Delete the `.db` file and re-ingest

---

## Performance Tips

1. **Use batch ingestion** - `IngestDirectoryAsync` is more efficient than multiple `IngestAsync` calls

2. **Choose appropriate chunk size** - 300-500 characters is a good balance between context and precision

3. **Use local embedding providers** - Ollama and ONNX are faster than cloud APIs for development

4. **Index your documents** - For production, use PGVector or Qdrant with proper indexing

5. **Monitor memory** - Large PDFs can use significant memory during processing

---

## Next Steps

After testing, consider:

1. **Adding unit tests** - The test project infrastructure is ready
2. **Customizing processors** - Extend for specialized document formats
3. **Production deployment** - Use PGVector or Qdrant for scalability
4. **Integration with LLMs** - Combine search results with chat models for complete RAG

---

*Last Updated: 2025-12-29*
