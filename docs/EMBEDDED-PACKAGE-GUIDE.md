# TechieRag.Embedded - Zero-Config Offline RAG

**TechieRag.Embedded** has the embedding model **built into the DLL**. No downloads, no configuration, just works!

---

## Quick Start (3 lines of code!)

```csharp
using TechieRag;
using TechieRag.Embedded;

// That's it! Model is embedded in the DLL!
var rag = new TechieRagBuilder()
    .UseEmbedded()      // Uses bundled all-MiniLM-L6-v2 model
    .UseSqliteVec()
    .Build();

await rag.InitializeAsync();
await rag.IngestAsync("document.pdf");
var results = await rag.SearchAsync("your query");
```

---

## What's Bundled?

| Component | Details |
|-----------|---------|
| **Model** | all-MiniLM-L6-v2 |
| **Dimensions** | 384 |
| **DLL Size** | ~86 MB |
| **NuGet Size** | ~79 MB (compressed) |
| **Speed** | ~500 tokens/sec (CPU) |
| **Quality** | Good for general use |

---

## Usage Options

### Option 1: Just Use It (Recommended)

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()  // Model is inside the DLL!
    .UseSqliteVec()
    .Build();
```

### Option 2: Direct Provider

```csharp
// Create provider directly - model extracted from DLL automatically
var provider = EmbeddedEmbeddingProvider.CreateDefault();

var embedding = await provider.EmbedAsync("Hello world");
Console.WriteLine($"Dimensions: {embedding.Length}"); // 384
```

### Option 3: Custom Model Path (Advanced)

```csharp
// Use your own model instead of the bundled one
var rag = new TechieRagBuilder()
    .UseEmbeddedModel("./models/bge-m3", dimensions: 1024)
    .UseSqliteVec()
    .Build();
```

---

## Complete Example

```csharp
using TechieRag;
using TechieRag.Embedded;

// Fully offline RAG - works without internet!
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec("my-knowledge-base.db")
    .WithChunkSize(300, 50)
    .Build();

await rag.InitializeAsync();

// Ingest documents
await rag.IngestDirectoryAsync("./documents", "*.pdf");

// Search - completely offline!
var results = await rag.SearchAsync("What is machine learning?", topK: 5);

foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:P0}");
    Console.WriteLine($"Text: {result.Chunk.Text}");
}
```

---

## For Package Maintainers

If you're building the package yourself:

### Step 1: Download Model (One Time)

```powershell
cd src/TechieRag.Embedded
.\download-models.ps1
```

This downloads all-MiniLM-L6-v2 (~90MB) to the Models folder.

### Step 2: Build & Pack

```powershell
dotnet pack src/TechieRag.Embedded/TechieRag.Embedded.csproj -c Release -o nupkg
```

The model gets embedded into the DLL automatically.

---

## Why TechieRag.Embedded?

| Feature | TechieRag | TechieRag.Embedded |
|---------|-----------|-------------------|
| Requires Ollama | Yes | **No** |
| Requires Internet | For embeddings | **No** |
| Configuration | API keys, URLs | **None** |
| Package size | Small | ~79 MB |
| First-run setup | Install services | **Just works** |

Perfect for:
- Air-gapped environments
- Edge deployments
- Privacy-sensitive applications
- Quick prototyping
- CI/CD pipelines

---

*Model: [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) by Sentence Transformers (Apache 2.0 License)*
