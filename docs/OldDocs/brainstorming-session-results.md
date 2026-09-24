# Brainstorming Session Results

**Session Date:** 2025-12-28
**Facilitator:** Business Analyst Mary
**Topic:** TRRAG (Techie Rathor Retrieval Augmented Generator) Library Design

---

## Executive Summary

**Topic:** Design a configurable, reusable RAG library for .NET applications

**Session Goals:**
- Define TRRAG library scope and capabilities
- Determine configuration strategy for embedding models and vector databases
- Assess feasibility of refactoring ChatAppEx as the foundation
- Create actionable architecture for rapid implementation

**Techniques Used:** Progressive Flow (Broad → Narrow → Converge)

**Total Ideas Generated:** 47+

### Key Themes Identified:
- Configuration over code changes (zero-code switching)
- Dual packaging strategy (lightweight + embedded model)
- Multi-source embedding support (ONNX, Ollama, LM Studio, Cloud)
- Provider pattern for vector database abstraction
- Syntax-aware processing for code files
- Multilingual support (English + Hindi via BGE-M3)
- **Fresh solution approach** - New projects from scratch, NOT refactoring ChatAppEx
- **Sample app with UI configuration** - Settings screen to showcase all TechieRag options

### Coding Standards (Added Post-Session):

**A. No Underscores in Naming:**
- Use PascalCase for classes, methods, properties, database tables, and columns
- Use camelCase for private fields, parameters, and local variables
- Example: `connectionString` (private field), `DocumentId` (property), `Chunks` (table)

**B. XML Documentation Required:**
- Every class must have XML comments explaining purpose and code flow
- Every method must have XML comments with param/returns/remarks tags
- Comments should explain the "why" and how it fits into the overall solution

See `docs/trrag-refactoring-roadmap.md` for detailed standards and code examples.

---

## Technique Sessions

### Phase 1: Broad Exploration - Pain Point Discovery

**Description:** Identified core problems that TRRAG will solve

**Ideas Generated:**
1. Eliminate repetitive RAG setup across multiple applications
2. Standardize embedding and retrieval operations
3. Remove dependency on preview/unstable .NET packages
4. Support local embedding models for offline/privacy scenarios
5. Reduce 3-project complexity to single NuGet package
6. Enable configuration-based switching (no code changes)
7. Support bundled ONNX models for zero-setup deployment

**Insights Discovered:**
- ChatAppEx is tightly coupled to Azure OpenAI - needs abstraction
- Qdrant is hardcoded - needs provider pattern
- PDF processing is already well-structured - can be extended
- SemanticSearch and DataIngestor services are the reusable core

**Notable Connections:**
- Configuration-based switching applies to BOTH embedding AND vector DB
- Bundled model approach mirrors how SQLite ships (embedded database concept)

---

### Phase 2: Narrowing - Technical Decisions

**Description:** Locked in specific technical choices for v1

**Decisions Made:**

#### Embedding Model Strategy
| Decision | Choice | Rationale |
|----------|--------|-----------|
| Primary Model | BGE-M3 | Multilingual (100+ languages), high quality, ONNX available |
| Local Sources | ONNX bundled, Ollama, LM Studio | Flexibility + offline capability |
| Cloud Sources | API-based BGE-M3 endpoints | For cloud-native deployments |
| Packaging | Two NuGet packages | Lightweight vs embedded model options |

#### Vector Database Strategy
| Priority | Database | Use Case |
|----------|----------|----------|
| 1 (tied) | SQLite-vec | Embedded apps, desktop, zero-config |
| 1 (tied) | PGVector | Production PostgreSQL environments |
| 2 | Qdrant | Feature-rich, existing ChatAppEx compat |

#### Configuration Strategy
| Style | Support Level |
|-------|---------------|
| Fluent Builder | Full |
| Configuration Object | Full |
| appsettings.json | Full |
| Dependency Injection | Full |

#### File Type Support (v1)
| Category | Extensions | Chunking |
|----------|------------|----------|
| Documents | .pdf, .docx, .txt, .md | Semantic paragraph |
| Data | .json, .toml, .yaml, .xml | Structure-aware |
| Web | .html, .htm | Tag-stripped semantic |
| Code | .cs, .js, .ts, .razor, .css, .py | Syntax-aware (function/class) |

---

### Phase 3: Convergent - Architecture Definition

**Description:** Finalized library structure and API surface

**Final Architecture:**

```
┌─────────────────────────────────────────────────────────────────┐
│                        YOUR APPLICATION                         │
└─────────────────────────────┬───────────────────────────────────┘
                              │ references
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       TechieRag API                             │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ IngestAsync | SearchAsync | DeleteAsync | ListAsync      │   │
│  │ IngestTextAsync | IngestUrlAsync | GetStatsAsync | Clear │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────┬───────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
┌───────────────┐    ┌────────────────┐    ┌───────────────────┐
│ IDocProcessor │    │IEmbeddingProvider│  │   IVectorStore    │
├───────────────┤    ├────────────────┤    ├───────────────────┤
│ PdfProcessor  │    │ OnnxEmbedding  │    │ SqliteVecStore    │
│ DocxProcessor │    │ OllamaEmbedding│    │ PgVectorStore     │
│ TextProcessor │    │ LmStudioEmbed  │    │ QdrantStore       │
│ MarkdownProc  │    │ CloudEmbedding │    └───────────────────┘
│ HtmlProcessor │    └────────────────┘
│ JsonProcessor │
│ CodeProcessor │
└───────────────┘
```

**Namespace:** `TechieRag`

**NuGet Packages:**
- `TechieRag` (~500KB) - Core library, API-based embedding only
- `TechieRag.Embedded` (~500MB+) - Includes bundled BGE-M3 ONNX model

---

### Phase 4: Clarified Requirements - Project Structure & Sample App

**Description:** User clarified critical decisions about project approach and sample application

#### Project Approach Decision
| Option | Decision |
|--------|----------|
| Refactor ChatAppEx in-place | **REJECTED** |
| Create fresh solution from scratch | **SELECTED** |
| Naming convention | Remove ALL "ChatAppEx" references - everything is "TechieRag" |

**Rationale:** Clean slate ensures no legacy coupling, proper naming, and professional package structure.

#### Sample Application Requirements: TechieRagWeb

**Purpose:** Showcase ALL TechieRag capabilities via interactive UI

**Required Screens:**

| Screen | Purpose | Features |
|--------|---------|----------|
| **Settings** | Configure TechieRag | Embedding source selector, Vector DB selector, Connection strings, Model path, Document ingestion path |
| **Ingestion** | Manual document ingestion | Browse/set folder path, Ingest button, Progress indicator, Success/error feedback |
| **Chat** | Test RAG functionality | Chat interface, Citation display, Search visualization |

**Key Behavior Changes from ChatAppEx:**

| ChatAppEx Behavior | TechieRagWeb Behavior |
|--------------------|----------------------|
| Auto-ingests on startup | **Manual ingest** via UI button |
| Hardcoded document path (`wwwroot/Data`) | **Configurable path** via Settings |
| No configuration UI | **Full Settings screen** for all options |
| Tightly coupled to Qdrant | **Dropdown to select** vector DB |
| Azure OpenAI only | **Dropdown to select** embedding source |

**Settings Screen Wireframe:**
```
┌─────────────────────────────────────────────────────────────┐
│  TechieRag Settings                                    [Save]│
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  EMBEDDING CONFIGURATION                                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Source: [Dropdown: ONNX | Ollama | LM Studio | Cloud]│   │
│  │ Endpoint/Path: [____________________________]        │   │
│  │ Model Name: [bge-m3_____]                            │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  VECTOR DATABASE CONFIGURATION                              │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Type: [Dropdown: SQLite-vec | PGVector | Qdrant]    │   │
│  │ Connection String: [_________________________]       │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  DOCUMENT INGESTION                                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Documents Path: [C:\Documents\RAGData____] [Browse] │   │
│  │                                                      │   │
│  │ [Ingest Now]  Status: Ready / Ingesting... / Done   │   │
│  │                                                      │   │
│  │ Last ingested: 2025-12-28 14:30                     │   │
│  │ Documents: 5 | Chunks: 234 | Vector DB Size: 12MB   │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  [Go to Chat →]                                             │
└─────────────────────────────────────────────────────────────┘
```

**Insights from Clarification:**
- Sample app is a **showcase/demo tool**, not just a test harness
- UI configurability demonstrates the "zero-code switching" value proposition
- Manual ingestion allows testing different document sets without restart
- Settings persistence (appsettings.json or local storage) needed

---

## Idea Categorization

### Immediate Opportunities
*Ideas ready to implement now*

1. **Create Fresh TechieRag Solution**
   - Description: New solution from scratch with proper structure (NOT refactoring ChatAppEx)
   - Why immediate: Clean foundation, no legacy baggage
   - Resources needed: 1-2 hours solution setup

2. **Core Interfaces + Configuration System**
   - Description: IVectorStore, IEmbeddingProvider, IDocumentProcessor + TechieRagBuilder
   - Why immediate: Foundation for all other work
   - Resources needed: 3-4 hours

3. **SQLite-vec Provider (Primary)**
   - Description: Implement IVectorStore for SQLite-vec first
   - Why immediate: Zero-dependency, easiest to test, embedded scenarios
   - Resources needed: 4-6 hours implementation

4. **TechieRagWeb Sample Application**
   - Description: Blazor app with Settings screen, Manual Ingestion, and Chat
   - Why immediate: Demonstrates all TechieRag capabilities
   - Resources needed: 1-1.5 days for full UI

5. **Use Stable .NET 10 Only**
   - Description: No preview packages - production-ready from day 1
   - Why immediate: Avoid ChatAppEx's preview dependency issues
   - Resources needed: Careful package selection during setup

### Future Innovations
*Ideas requiring development/research*

1. **ONNX BGE-M3 Integration**
   - Description: Bundle BGE-M3 model for local inference
   - Development needed: ONNX runtime integration, model packaging
   - Timeline estimate: 1-2 weeks

2. **Syntax-Aware Code Chunking**
   - Description: Parse code files by function/class boundaries
   - Development needed: Roslyn for C#, tree-sitter for others
   - Timeline estimate: 1-2 weeks per language

3. **Ollama/LM Studio Providers**
   - Description: Connect to local model servers
   - Development needed: HTTP client implementations
   - Timeline estimate: 3-5 days each

### Moonshots
*Ambitious, transformative concepts*

1. **Auto-Chunking Intelligence**
   - Description: ML-based optimal chunk size detection per document type
   - Transformative potential: Best-in-class retrieval accuracy
   - Challenges: Training data, model complexity

2. **Hybrid Search (Vector + Keyword)**
   - Description: Combine semantic search with BM25 keyword matching
   - Transformative potential: Significantly improved recall
   - Challenges: Scoring fusion algorithms

3. **Incremental Indexing**
   - Description: Only re-embed changed portions of documents
   - Transformative potential: Massive performance gains on updates
   - Challenges: Change detection, chunk boundary management

### Insights & Learnings
*Key realizations from the session*

- **BGE-M3 is ideal**: Multilingual support (Hindi!) + high quality + ONNX available - perfect fit
- **ChatAppEx is 70% there**: Core services (SemanticSearch, DataIngestor) are solid foundation
- **Configuration is everything**: The core value proposition is zero-code switching
- **Provider pattern is proven**: Same pattern used by EF Core, ASP.NET Identity - developers know it
- **LLM separation is correct**: TRRAG does RAG, app does LLM - clean separation of concerns

---

## Action Planning

### Top 4 Priority Ideas (Revised)

#### #1 Priority: Fresh TechieRag Solution + Core Interfaces
- **Rationale:** Clean foundation with proper naming; no ChatAppEx legacy
- **Next steps:**
  1. Create new `TechieRag.sln` solution from scratch
  2. Create `TechieRag` class library project
  3. Create `TechieRag.Embedded` class library project
  4. Create `TechieRagWeb` Blazor Server project
  5. Define IVectorStore, IEmbeddingProvider, IDocumentProcessor interfaces
  6. Build TechieRagBuilder with all configuration styles
- **Resources needed:** 0.5-1 day focused work
- **Timeline:** Day 1

#### #2 Priority: Vector Store Providers (All Three)
- **Rationale:** All three required for v1; SQLite-vec first for easy testing
- **Next steps:**
  1. Implement SqliteVecStore : IVectorStore (primary)
  2. Implement PgVectorStore : IVectorStore
  3. Implement QdrantStore : IVectorStore
  4. Create provider factory with configuration-based selection
- **Resources needed:** sqlite-vec, pgvector, Qdrant client docs
- **Timeline:** Days 2-3

#### #3 Priority: Document Processors + Embedding Providers
- **Rationale:** Enables actual ingestion; use ChatAppEx PDF logic as reference (copy, don't link)
- **Next steps:**
  1. Implement PdfProcessor (based on PdfPig - reference ChatAppEx code)
  2. Implement DocxProcessor, TextProcessor, MarkdownProcessor
  3. Implement HtmlProcessor, JsonProcessor, CodeProcessor
  4. Implement OllamaEmbeddingProvider (primary for local)
  5. Implement AzureOpenAIEmbeddingProvider (for cloud)
  6. Implement OnnxEmbeddingProvider (for bundled)
- **Resources needed:** PdfPig, OpenXml, Ollama API, ONNX Runtime docs
- **Timeline:** Days 2-4 (parallel with #2)

#### #4 Priority: TechieRagWeb Sample Application
- **Rationale:** Showcases all TechieRag capabilities; proves the library works
- **Next steps:**
  1. Create Settings page with all configuration options
  2. Create Ingestion page with path selection and manual trigger
  3. Create Chat page with RAG-powered responses
  4. Wire up configuration persistence (appsettings.json)
  5. Add status/stats display for feedback
- **Resources needed:** Blazor knowledge, UI components
- **Timeline:** Days 4-5

### Revised Timeline Estimate

| Phase | Work | Duration | Days |
|-------|------|----------|------|
| Phase 1 | Solution setup + Core interfaces | 0.5-1 day | Day 1 |
| Phase 2 | Vector Store providers (3) | 1.5 days | Days 2-3 |
| Phase 3 | Document processors + Embedding providers | 2 days | Days 2-4 |
| Phase 4 | TechieRagWeb sample app (Settings + Ingestion + Chat) | 1.5 days | Days 4-5 |
| Phase 5 | Integration testing + Polish | 0.5-1 day | Day 5-6 |
| **Total** | **Complete TechieRag v1** | **5-6 days** | |

**Assessment:** The 5-day estimate is **still achievable but tight** given the additions:
- Settings UI adds ~0.5 day
- Manual ingestion with path selection adds ~0.25 day
- Fresh solution (vs refactor) is actually faster (no cleanup needed)

**Risk factors:**
- ONNX integration may take longer than expected
- Vector DB provider edge cases
- UI polish can expand scope

**Recommendation:** Plan for 5-6 days; 5 days achievable with focused execution and deferring ONNX bundling to v1.1

---

## Reflection & Follow-up

### What Worked Well
- Progressive flow quickly narrowed from broad vision to specific architecture
- Inline Q&A format captured decisions efficiently
- ChatAppEx analysis provided concrete starting point
- YOLO mode enabled rapid iteration

### Areas for Further Exploration
- **ONNX model packaging**: How to bundle 500MB+ model in NuGet efficiently
- **Code chunking strategies**: Roslyn vs tree-sitter vs regex-based
- **Hybrid search implementation**: Vector + keyword fusion scoring
- **Token counting**: How to track embedding tokens across providers

### Recommended Follow-up Techniques
- **Prototyping Sprint**: Build minimal TechieRag in 1-2 days to validate architecture
- **Competitive Analysis**: Review LangChain, LlamaIndex, Semantic Kernel patterns
- **User Story Mapping**: Define specific usage scenarios for API validation

### Questions That Emerged
- Should TechieRag support async streaming for large document ingestion?
- How to handle embedding model version mismatches (re-index required)?
- Should there be a TechieRag.Aspire package for Aspire integration?
- What's the migration path for existing ChatAppEx data in Qdrant?

### Next Session Planning
- **Suggested topics:** Detailed API contract design, Error handling strategy
- **Recommended timeframe:** After initial prototype validates architecture
- **Preparation needed:** Working TechieRag skeleton with one provider each

---

*Session facilitated using the BMAD-METHOD brainstorming framework*
