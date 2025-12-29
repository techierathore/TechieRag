# Story 3.2: Implement PGVector Provider

## Story Information
**Story ID:** STORY-3.2
**Epic:** EPIC-003 - Vector Store Providers
**Status:** Ready for Development
**Priority:** P1 - High
**Story Points:** 5

## User Story
As an enterprise developer, I want a PostgreSQL-based vector store so that I can integrate TechieRag with my existing PostgreSQL infrastructure.

## Description
Implement PgVectorStore class that uses PostgreSQL with the pgvector extension for vector similarity search.

## Acceptance Criteria
- [ ] PgVectorStore.cs exists in src/TechieRag/VectorStores/
- [ ] Implements IVectorStore interface completely
- [ ] Creates tables and enables pgvector extension on Initialize
- [ ] All CRUD operations work correctly
- [ ] Complete XML documentation
- [ ] Solution builds successfully

## Technical Requirements

### NuGet Packages Required
Add to TechieRag.csproj:
- Npgsql (9.0.2)
- Pgvector (0.3.0)

### Database Schema
```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS Documents (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    SourcePath TEXT NOT NULL,
    ChunkCount INTEGER DEFAULT 0,
    IngestedAt TIMESTAMPTZ NOT NULL,
    Metadata JSONB
);

CREATE TABLE IF NOT EXISTS Chunks (
    Id TEXT PRIMARY KEY,
    DocumentId TEXT NOT NULL REFERENCES Documents(Id) ON DELETE CASCADE,
    Text TEXT NOT NULL,
    Embedding vector(1024),
    PageNumber INTEGER,
    ChunkIndex INTEGER,
    Metadata JSONB,
    CreatedAt TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId);
CREATE INDEX IF NOT EXISTS IdxChunksEmbedding ON Chunks USING ivfflat (Embedding vector_cosine_ops);
```

### Key Implementation Notes
- Use Npgsql with Pgvector type mapping
- Connection pooling via NpgsqlDataSource
- Use JSONB for metadata
- IVFFlat index for similarity search

## Definition of Done
- [ ] All IVectorStore methods implemented
- [ ] `dotnet build` passes
- [ ] XML documentation complete
