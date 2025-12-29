# Story 3.1: Implement SQLite-vec Provider

## Story Information
**Story ID:** STORY-3.1
**Epic:** EPIC-003 - Vector Store Providers
**Status:** Ready for Development
**Priority:** P0 - Critical
**Story Points:** 8

## User Story
As a developer, I want an embedded SQLite-based vector store so that I can run TechieRag without external database dependencies.

## Description
Implement SqliteVecStore class that uses SQLite with the sqlite-vec extension for vector similarity search. This is the primary/default vector store.

## Acceptance Criteria
- [ ] SqliteVecStore.cs exists in src/TechieRag/VectorStores/
- [ ] Implements IVectorStore interface completely
- [ ] Creates Documents, Chunks, ChunksVec tables on Initialize
- [ ] UpsertAsync stores chunks with vectors
- [ ] SearchAsync performs vector similarity search
- [ ] All CRUD operations work correctly
- [ ] Complete XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### NuGet Packages Required
Add to TechieRag.csproj:
- Microsoft.Data.Sqlite (9.0.0)
- Dapper (2.1.35)
- SQLitePCLRaw.bundle_e_sqlite3 (2.1.10)

### Database Schema
```sql
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
    Embedding float[1024]
);

CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId);
```

### Key Implementation Notes
- Use 1024 dimensions (BGE-M3 default)
- Vector serialization: float[] to byte[] using Buffer.BlockCopy
- Distance to score conversion: score = 1 - distance
- Load sqlite-vec extension via SQLitePCLRaw

### SqliteVecStore Class Structure
```csharp
namespace TechieRag.VectorStores;

public class SqliteVecStore : IVectorStore
{
    public string Name => "SQLite-vec";

    private readonly string connectionString;
    private readonly int dimensions;
    private bool initialized;

    public SqliteVecStore(string connectionString, int dimensions = 1024) { }

    public async Task InitializeAsync(CancellationToken ct) { }
    public async Task<string> UpsertAsync(TextChunk chunk, CancellationToken ct) { }
    public async Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken ct) { }
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK, string? documentFilter, CancellationToken ct) { }
    public async Task DeleteAsync(string chunkId, CancellationToken ct) { }
    public async Task DeleteByDocumentAsync(string documentId, CancellationToken ct) { }
    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct) { }
    public async Task<IngestionStats> GetStatsAsync(CancellationToken ct) { }
    public async Task ClearAsync(CancellationToken ct) { }

    private static byte[] SerializeVector(float[] vector) { }
    private static float[] DeserializeVector(byte[] bytes) { }
}
```

## Definition of Done
- [ ] All IVectorStore methods implemented
- [ ] Unit tests pass
- [ ] `dotnet build` passes
- [ ] XML documentation complete
