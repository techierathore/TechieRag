# TechieRag — Usage Guide

| | |
|---|---|
| App | TechieRag |
| Kind | library |
| Size | Large |
| Date | 2026-09-24 |

## Test users

The library has no users of its own. Tests and smoke runs use credentials from the environment, never accounts.

| # | User | Password source | Role | Exists |
|---|---|---|---|---|
| 1 | none (library; no sign-in) | — | Developer running the test project | n/a |
| 2 | `TechieRagLiveNetworkTests=1` | environment variable, enables the live web tests | Maintainer | set per machine |
| 3 | `TechieRagTestPostgres` | environment variable holding a PostgreSQL connection string; enables `LivePgVectorStoreTests` | Maintainer | set per machine |
| 4 | staged weights under `~/.cache/techierag-models/bge-reranker-v2-m3` and `bge-m3` | files on disk; enable the live reranker and embedder tests | Maintainer | ask owner |

## Execution guide

Prerequisites: .NET 10 SDK (the harness bridges to the Windows SDK from WSL through `bash .tfcore/utils/tf-build.sh`); Ollama with `bge-m3` pulled for the Ollama examples; nothing else.

```
dotnet restore TechieRag.slnx
dotnet build TechieRag.slnx --configuration Release
dotnet test tests/TechieRag.Tests/TechieRag.Tests.csproj --configuration Release
bash .tfcore/utils/tf-build.sh test tests/TechieRag.Tests/TechieRag.Tests.csproj
dotnet pack src/TechieRag/TechieRag.csproj -o ./local-feed -p:Version=1.0.8-local.1
```

There is no URL to open: the library is exercised through its tests and through a consuming app (Sevak, MyDiary, or the planned `samples/TechieRag.Probe`).

## How to test, screen by screen

### Ingestion
- **Sign in as:** user 1 (no sign-in)
- **Steps:** 1) Build a `TechieRagBuilder().UseOllama("http://localhost:11434", "bge-m3").UseSqliteVec("t.db")` instance and call `InitializeAsync()` 2) call `IngestTextAsync` twice with two short texts 3) call `IngestAsync` with a PDF and `IngestDirectoryAsync("./docs", "*.md")` 4) call `ListDocumentsAsync` and `GetStatsAsync`
- **Expected:** four or more documents listed with chunk counts above zero; a PDF yields page-numbered chunks; the statistics match the list.
- **Covers:** REQ-RAG-001

### Embedding providers and vector stores
- **Sign in as:** user 1
- **Steps:** 1) switch the builder to `UsePgVector(conn)` with `TechieRagTestPostgres` set, then to `UseQdrant()` against a local Qdrant 2) repeat the ingestion steps 3) run `dotnet test --filter VectorStores`
- **Expected:** each store creates its tables or collections on initialise; upsert, filtered search, delete and stats behave identically; the hermetic store tests pass and the live PostgreSQL tests run when the variable is set.
- **Covers:** REQ-RAG-002, REQ-RAG-003, REQ-RAG-044

### Semantic search
- **Sign in as:** user 1
- **Steps:** 1) call `SearchAsync("What is TechieRag?", topK: 2)` after the ingestion steps 2) call it again with a `documentFilter` naming one document 3) pass `new SearchOptions { Rerank = true }` with a reranker configured
- **Expected:** two results in descending score between 0 and 1 with chunk text and metadata; the filtered call returns only that document's chunks; the reranked call reorders the candidates.
- **Covers:** REQ-RAG-004, REQ-RAG-097, REQ-RAG-098

### Configuration and DI
- **Sign in as:** user 1
- **Steps:** 1) put a `TechieRag` section in an `appsettings.json` with embedding, vector store and LLM values 2) call `services.AddTechieRag(configuration.GetSection("TechieRag"))` and resolve `ITechieRag` 3) change only the vector store type in the file and resolve again
- **Expected:** the instance uses the configured providers; the second resolution uses the new store with no code change. Known gap: `VectorStore.ApiKey` and the `Prompt` section are not mapped yet (REQ-FN-066).
- **Covers:** REQ-FN-001, REQ-FN-066

### Embedded package
- **Sign in as:** user 1
- **Steps:** 1) reference `TechieRag.Embedded` and build with `.UseEmbedded().UseSqliteVec()` 2) subscribe to `ModelDownloadService.Instance.ProgressChanged` 3) call `InitializeAsync()` once online, then again with the network off 4) call `EmbedAsync("hello")`
- **Expected:** the first initialise downloads about 2.3 GB with progress events; the second makes no network call; a 1024-dimension vector returns. On a phone this default is replaced by a 384-dimension model in phase 2 (REQ-RAG-054).
- **Covers:** REQ-RAG-005, REQ-RAG-053, REQ-RAG-054, REQ-RAG-055

### LLM providers, RAG generation and structured output
- **Sign in as:** user 1, with one provider key in the environment
- **Steps:** 1) add `.UseOpenAICompatibleLlm(endpoint, key, "gpt-4o")` or `.UseOllamaLlm()` 2) call `AskAsync("What does TechieRag do?")` 3) iterate `AskStreamAsync` and `AskStreamWithSourcesAsync` 4) call `CompleteAsync<MyDto>("Return JSON with fields a and b")` 5) call `UseLlmForModel("claude-sonnet-4-5")` and check the resolved provider name
- **Expected:** an answer with sources and usage; text arrives in pieces, sources arrive as events; the DTO is populated; the router picks Anthropic from the model name alone.
- **Covers:** REQ-RAG-006, REQ-RAG-007, REQ-RAG-008, REQ-RAG-099, REQ-RAG-103

### Agent loop, MCP tools and flows
- **Sign in as:** user 1, with a provider that supports tool calling
- **Steps:** 1) register a `get_weather` delegate on `ToolRegistry` and run `AgentLoopRunner` on "What is the weather in Pune?" 2) register an MCP server over stdio and repeat with one of its tools 3) run a five-node flow from the orchestration tests with a `MaxSteps` of 3
- **Expected:** the tool executes and the final answer uses its result; the MCP tool appears and executes through `McpToolHandler`; the flow stops at three steps with a coded `FlowMessage`.
- **Covers:** REQ-RAG-009, REQ-RAG-042, REQ-RAG-050, REQ-RAG-051, REQ-RAG-084, REQ-RAG-086, REQ-RAG-087

### Workspaces, memory and token tracking
- **Sign in as:** user 1
- **Steps:** 1) add `.WithPersistence(StoreProvider.Sqlite, "Data Source=ws.db").WithConversationMemory().WithUsageTracking(u => u.MaxCostUsd = 0.10m)` 2) create two workspaces with different documents through `WorkspaceManager` 3) chat twice in a thread, restart, reload the thread 4) read `GetTokenTracker().GetBudgetStatus()`
- **Expected:** retrieval in one workspace never returns the other's chunks; the thread reloads with both turns; usage and cost are recorded and the alert fires when the budget nears its ceiling.
- **Covers:** REQ-RAG-010, REQ-RAG-011, REQ-RAG-089, REQ-RAG-092

### Resilience and fallback
- **Sign in as:** user 1
- **Steps:** 1) point the primary provider at an endpoint that returns 429 with `Retry-After: 2` 2) configure `.WithFallbackLlm(f => f.Source = LlmSource.Ollama)` 3) call `AskAsync` 4) run `dotnet test --filter RetryHandler`
- **Expected:** the call waits the stated delay, retries, then fails over to the fallback and answers; the retry tests pass including the `Retry-After` parsing cases.
- **Covers:** REQ-RAG-012, REQ-RAG-013, REQ-NFR-002

### Data connectors and web ingestion
- **Sign in as:** user 2 (`TechieRagLiveNetworkTests=1`) for the live cases
- **Steps:** 1) run `ConnectorRunner` with a `RepositoryConnector` against a public GitHub repository, branch `main`, glob `*.md` 2) run an `EmailConnector` against an `.mbox` file 3) ingest one URL and crawl one site with depth 1 4) attempt a fetch to `http://127.0.0.1/` through the guarded handler
- **Expected:** per-item results and sync state for the repository run; messages ingested from the mbox with replies trimmed; the page and the crawled pages become documents; the loopback fetch is refused by the SSRF guard.
- **Covers:** REQ-RAG-074 to REQ-RAG-083

### Packaging and publishing
- **Sign in as:** user 1 (maintainer)
- **Steps:** 1) `dotnet pack` the three packages with `-p:Version=1.0.8-local.1` into `./local-feed` 2) `unzip -l` each `.nupkg` 3) dispatch `publish-nuget.yml` with `dry_run` true against `main`
- **Expected:** `TechieRag`, `TechieRag.Embedded` and (once REQ-FN-065 lands) `TechieRag.Telemetry` pack with README, `buildTransitive` targets and symbols; the dry run derives a version and stops before pushing.
- **Covers:** REQ-FN-003, REQ-FN-004, REQ-FN-005, REQ-FN-065, REQ-FN-067

## Automated tests

```
bash .tfcore/utils/tf-build.sh test tests/TechieRag.Tests/TechieRag.Tests.csproj
TechieRagLiveNetworkTests=1 dotnet test tests/TechieRag.Tests --filter "Category=LiveNetwork"
TechieRagTestPostgres="Host=localhost;Database=techierag;Username=postgres;Password=..." dotnet test tests/TechieRag.Tests --filter LivePgVector
```
About 634 xUnit test methods across processors, providers, stores, agent loop, orchestration, MCP, connectors, web, persistence, reranking, telemetry and packaging; 41 live tests skip with a reason when their environment is absent. PASS on 2026-09-24 through the build ladder.

## Known limitations

- Every vector store is constructed with 1024 dimensions; Cohere, OpenAI and Gemini embedders need REQ-RAG-106 before pgvector or Qdrant can hold their vectors.
- SQLite search is a managed full scan; sqlite-vec is never loaded (REQ-RAG-056 makes the managed path fast and documented).
- `AddTechieRag(IConfiguration)` drops `VectorStore.ApiKey`, embedding `Dimensions`, `ApiFormat`, `ApiPath`, `RequestDelayMs` and the `Prompt` section (REQ-FN-066).
- `TechieRag.Telemetry` is not published by any workflow yet (REQ-FN-065).
- YouTube transcript ingestion is blocked: the timed-text endpoint returns empty bodies (REQ-RAG-076, TR-RAG-015 in `docs/Sevak-TechieRag-Feedback.md`).
- The email connector has only been exercised against mbox files, not a live IMAP server (REQ-RAG-081).
- `PgVectorStore` has never run against a real PostgreSQL (REQ-RAG-044).
- Model files live under the `TechieRag.Embedded.dll` folder, which is read-only on phones; phase 2 moves them to the per-user application data root (REQ-RAG-053).
- No in-process language model, no typed streaming, no subscription sign-in yet: phase 2 (REQ-RAG-057 to REQ-RAG-070).

## Platform notes

Today the packages are plain `net10.0` / `net8.0` and are proven on Windows and macOS (console hosts and the Sevak desktop app; the macOS native ONNX load was fixed in `OnnxNativeLibraryResolver`). Android and iOS load the assemblies but nothing yet makes the native ONNX library arrive or the model folder writable; the phase-2 platform support matrix (REQ-FN-054) will record, per package and platform, supported, tested on a named device, or not supported. Until it exists, treat Android and iOS as untested.

## How to call it

Install from nuget.org (no token, no `nuget.config` edit):

```
dotnet add package TechieRag
dotnet add package TechieRag.Embedded
dotnet add package TechieRag.Telemetry   # once REQ-FN-065 publishes it
```

Register through the builder or DI:

```csharp
using TechieRag;
using TechieRag.Embedded;

var rag = new TechieRagBuilder()
    .UseEmbedded()                       // offline embeddings, downloaded once
    .UseSqliteVec("app.db")              // zero-config local vector store
    .UseOllamaLlm(model: "llama3.2")     // optional: any of six providers, or UseLlmForModel("gpt-4o")
    .WithConversationMemory()
    .WithUsageTracking(u => u.MaxCostUsd = 1.00m)
    .WithLogging(loggerFactory)
    .Build();

services.AddTechieRag(configuration.GetSection("TechieRag"));   // or the appsettings path
```

One call per public service:

| Service | Call |
|---|---|
| Ingestion | `await rag.InitializeAsync(); await rag.IngestAsync("file.pdf"); await rag.IngestDirectoryAsync("./docs", "*.md"); await rag.IngestTextAsync(text, "name", metadata);` |
| Search | `var hits = await rag.SearchAsync("query", topK: 5, documentFilter: null);` |
| RAG answer | `var r = await rag.AskAsync("question"); await foreach (var piece in rag.AskStreamAsync("question")) …` |
| Multi-turn RAG | `await rag.ChatWithRagAsync("message", history);` |
| Streaming with sources | `await foreach (var ev in rag.AskStreamWithSourcesAsync("question")) …` |
| Structured output | `var dto = await rag.GetLlmProvider()!.CompleteAsync<MyDto>("Return JSON …");` |
| Tools and agent loop | `new ToolRegistry().Register("get_weather", schema, args => …); await new AgentLoopRunner(provider, registry).RunAsync(prompt);` |
| MCP tool servers | `builder.WithTools(t => t.AddMcpServer(new McpServerConfig { Command = "npx", Args = … }));` |
| Flows | `var flow = FlowSerializer.Deserialize(json); await new FlowRunner(runtime).RunAsync(flow, input);` |
| Workspaces and threads | `builder.WithPersistence(StoreProvider.Sqlite, "Data Source=ws.db"); var ws = rag.GetWorkspaceManager()!;` |
| Reranking | `builder.WithReranker(RerankSource.LocalOnnx); await rag.SearchAsync("q", new SearchOptions { Rerank = true });` |
| Connectors | `await ConnectorRunner.RunAsync(new RepositoryConnector(options), rag, runOptions);` |
| Web ingestion | `await rag.IngestUrlAsync("https://…"); await rag.CrawlAsync("https://…", new WebCrawlOptions { MaxDepth = 1 });` |
| Token tracking | `var status = rag.GetTokenTracker().GetBudgetStatus();` |
| Telemetry | `services.AddTechieRagTelemetry(o => { o.EnableTracing = true; o.Endpoint = "http://localhost:4318"; });` |

Exact signatures are in `docs/TechieRag-AI-Reference.md` (also installed into a consumer's repository as `.techierag/TechieRag-AI-Reference.md` on build); the maintainer's map of each service is `docs/TechieRag-DevGuide.md`.
