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
| 5 | `TechieRagLiveLocalModel` (optional model id) and a local model downloaded under the model root | environment variable plus files on disk; enable the live `TechieRag.Local` tests once a runtime is chosen for the platform | Maintainer | set per machine |

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
- **Steps:** 1) reference `TechieRag.Embedded` and build with `.UseEmbedded().UseSqliteVec()` 2) subscribe to `ModelDownloadService.Instance.DownloadSizeKnown` and `ProgressChanged` 3) call `InitializeAsync()` once online, then again with the network off 4) call `EmbedAsync("hello")` 5) on Android or iOS, call `.UseEmbedded(EmbeddedModel.BgeM3)`
- **Expected:** `DownloadSizeKnown` fires first with the total ("2.3 GB" desktop, "91 MB" phone) before any byte; `Decline = true` there stops it with `ModelDownloadDeclinedException`; progress events follow; files land in `<LocalApplicationData>/TechieRag/models/<model>` unless `UseModelRoot(path)` or `TECHIERAG_MODEL_ROOT` moved them; the second initialise makes no network call; a 1024-dimension vector on a desktop, 384 (all-MiniLM-L6-v2) on Android and iOS; step 5 throws `NotSupportedException` naming 2.3 GB.
- **Covers:** REQ-RAG-005, REQ-RAG-053, REQ-RAG-054, REQ-RAG-055

### Local model (TechieRag.Local): terms and download
- **Sign in as:** user 1
- **Steps:** 1) reference `TechieRag.Local`; build with `.UseEmbedded().UseSqliteVec("app.db").UseLocalLlm()` 2) call `AskAsync("What does TechieRag do?")` without accepting terms 3) rebuild with `.UseLocalLlm(o => o.ConfirmTermsAsync = (terms, ct) => ShowTermsDialogAsync(terms))` and subscribe to `ModelDownloadService.Instance.DownloadSizeKnown` 4) ask again, stop the app mid-download, restart, ask again 5) after `LocalLlm.Register()`, call `LlmProviderFactory.CreateForModel("local/qwen2.5-0.5b-instruct", null)`
- **Expected:** 2) no network request; `LocalModelTermsNotAcceptedException.Terms` carries the licence and `TermsUrl`; 3)–4) the dialog gets the terms and size before the first byte, the download resumes from its `.part` file, each file is SHA-256 checked into `<ModelRoot>/<model>-<format>`; 5) a `LocalLlmProvider` with no endpoint or key.
- **Covers:** REQ-RAG-057, REQ-RAG-061, REQ-RAG-062, REQ-RAG-064, REQ-RAG-066

### Local model (TechieRag.Local): generation and limits
- **Sign in as:** user 1, with the model downloaded
- **Steps:** 1) iterate `GetLlmProvider()!.ChatStreamEventsAsync(...)`, then `CompleteAsync<MyDto>(...)` 2) send a prompt longer than the context 3) on a device with little free memory, call `LoadAsync()`
- **Expected:** 1) text deltas, then one `Completed` with usage from the model's own tokenizer; the DTO parses; 2) `LocalPromptTooLongException` naming both lengths, nothing generated; 3) `LocalModelMemoryException` naming the shortfall, the app keeps running. The engine is ONNX Runtime GenAI on every platform (`DECISIONS.md` 2026-09-25); the conformance suite runs these against the real engine with both models when a model is present, and against a scripted runtime otherwise.
- **Covers:** REQ-RAG-058, REQ-RAG-059, REQ-RAG-060, REQ-RAG-063, REQ-RAG-065

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

### Typed streaming and the streaming agent loop
- **Sign in as:** user 1, with LM Studio (or any of the six providers) serving a tool-capable model
- **Steps:** 1) with one `get_weather` tool in `LlmCompletionOptions.Tools`, iterate `provider.ChatStreamEventsAsync([ChatMessage.User("Weather in Pune?")], options)` 2) iterate `provider.ChatStreamAsync(...)` on the same prompt 3) register `get_weather` on a `ToolRegistry` and iterate `new AgentLoopRunner(provider, registry).RunStreamAsync(messages)`
- **Expected:** 1) `TextDelta` events (if the model writes any), then one `ToolCall` with name `get_weather` and complete JSON arguments, then one `Completed` with usage, last; 2) text only, as before; 3) text streams as it arrives, `ToolCallRequested` then `ToolExecuted` carrying the registry's result, the answer's text, then `Completed` with usage summed over the run.
- **Covers:** REQ-RAG-067, REQ-RAG-068

### Subscription catalog
- **Sign in as:** user 1
- **Steps:** 1) list `LlmConnectorCatalog.All.Where(c => c.Source == LlmSource.Subscription)` and read each row's `Subscription.Terms`, `AppliesTo` and `CheckedOn` 2) call `LlmProviderFactory.CreateSubscription(ModelRouter.Require("claude-subscription/claude-sonnet-4-5"), callback)`
- **Expected:** 1) six rows (ChatGPT, Claude, Gemini, Grok, Groq, Meta), each with its terms and the date 2026-09-24; only `chatgpt-subscription` is permitted and names `UseChatGptSubscriptionLlm`; Anthropic's reads "Not permitted"; 2) `SubscriptionSignInException` with code `SubscriptionNotPermitted` carrying Anthropic's terms. The research behind each row is in `DECISIONS.md`.
- **Covers:** REQ-RAG-070, REQ-FN-062

### Subscription sign-in (ChatGPT)
- **Sign in as:** user 1, with a ChatGPT account whose device-code sign-in is on (ChatGPT → Settings → Security; Business or Enterprise need their admin's consent)
- **Steps:** 1) build with `.UseChatGptSubscriptionLlm((prompt, ct) => { OpenBrowser(prompt.VerificationUri); ShowCode(prompt.UserCode); return Task.CompletedTask; })`; the host opens the browser, never the library 2) call `AskAsync("What does TechieRag do?")`, enter the code and approve 3) restart with `new ChatGptSubscriptionOptions { SessionStore = mySecureStore }` and ask again
- **Expected:** 1)–2) the callback receives `https://auth.openai.com/codex/device` and a one-time code; once approved the answer arrives, billed to the ChatGPT plan; 3) with the session saved, nothing is asked. Choose a model with `ChatGptSubscriptionOptions.Model` or route `chatgpt-subscription/<model>`.
- **Covers:** REQ-RAG-069

### Agents package (Microsoft Agent Framework)
- **Sign in as:** user 1, with LM Studio running a tool-capable model (for example `qwen3-8b`) at `http://localhost:1234`
- **Steps:** 1) ingest a document with a known fact 2) build `new TechieRagAgentBuilder(rag).UseLmStudio("http://localhost:1234", "qwen3-8b").Build()` and `AskAsync` about that fact 3) repeat with `.UseConfiguredLlm()`, `.WithToolHandler(registry)` and `.WithTrace(progress)` 4) iterate `AskStreamAsync` 5) register `RegisterKnowledgeBase(new TechieRagRetrievalSource(rag), null, new RetrievalTurnState())` on a `ToolRegistry` for `AgentLoopRunner` and ask again
- **Expected:** 2) `[S1]`-style refs, `Sources` holds the passage, `Searches` shows a `strong` search; 3) the same via the configured provider; the registry tool runs and the trace shows `ToolCallRequested`, `ToolExecuted`, `FinalAnswer`; 4) `Sources` after the search, then tokens, then `Completed`; 5) the same JSON (refs, scores, `status`) the MAF agent saw.
- **Covers:** REQ-RAG-015, REQ-RAG-016, REQ-RAG-017

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
- **Expected:** per-item results and sync state for the repository run; messages ingested from the mbox with replies trimmed; the page and the crawled pages become documents; the loopback fetch is refused by the SSRF guard. A run stopped by a limit carries a code (see "Connector stop codes" under How to call it).
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
bash .tfcore/utils/tf-build.sh test tests/TechieRag.Agents.Tests/TechieRag.Agents.Tests.csproj
TechieRagLiveLmStudioModel=qwen3-8b dotnet test tests/TechieRag.Agents.Tests --filter "Category=LiveLmStudio"   # optional TechieRagLiveLmStudioEndpoint
bash .tfcore/utils/tf-build.sh test tests/TechieRag.Local.Tests/TechieRag.Local.Tests.csproj
TechieRagLiveLocalModel=qwen2.5-0.5b-instruct dotnet test tests/TechieRag.Local.Tests --filter LiveLocalLlmTests
TechieRagLiveHuggingFace=1 dotnet test tests/TechieRag.Local.Tests --filter LiveHuggingFaceTests   # downloads about 900 MB
```
`TechieRag.Local.Tests` runs the runtime-neutral conformance suite (`LocalLlmConformanceTests`) against a scripted runtime, plus the provider, template, stop-sequence, memory, registration and catalog tests, and the download tests against a loopback HTTP server. Its three live tests (`LiveLocalLlmTests`, collection `LiveLocalLlm`, one at a time) skip with a printed reason until the platform has a runtime and the model is downloaded; they never download.
`TechieRag.Agents.Tests` (28 methods on 2026-09-24) runs the Agent Framework builder and the four seam adapters against a scripted `IChatClient` and a real `TechieRagClient`; its two LM Studio tests skip with a reason unless `TechieRagLiveLmStudioModel` names a loaded tool-capable model.
About 634 xUnit test methods across processors, providers, stores, agent loop, orchestration, MCP, connectors, web, persistence, reranking, telemetry and packaging; 41 live tests skip with a reason when their environment is absent. PASS on 2026-09-24 through the build ladder.

## Known limitations

- Every vector store is constructed with 1024 dimensions; Cohere, OpenAI and Gemini embedders need REQ-RAG-106 before pgvector or Qdrant can hold their vectors.
- SQLite search is an exact managed scan by design (no sqlite-vec, no approximate index): about 0.24 s at 50,000 chunks of 1024 dimensions on a desktop (Platform notes). Past a few hundred thousand chunks use pgvector or Qdrant.
- `AddTechieRag(IConfiguration)` drops `VectorStore.ApiKey`, embedding `Dimensions`, `ApiFormat`, `ApiPath`, `RequestDelayMs` and the `Prompt` section (REQ-FN-066).
- `TechieRag.Telemetry` is not published by any workflow yet (REQ-FN-065).
- Web page and site-crawl ingestion are the web routes; transcript ingestion was removed by owner decision on 2026-09-24 (REQ-RAG-076 N/A, code deleted under BRD-164).
- The email connector has only been exercised against mbox files, not a live IMAP server (REQ-RAG-081).
- The probe's Mac Catalyst and iOS heads ran on 2026-09-25 on the owner's Mac (Mac Catalyst and the iPhone 17 Pro simulator); a physical iPhone and an Android phone have not run it yet.
- `TechieRag.Local` has no engine on an Intel Mac (GenAI ships none) and throws `PlatformNotSupportedException`. Subscription sign-in: REQ-RAG-069 and REQ-RAG-070.
- `TechieRag.Agents`: a session's citation refs live in memory against the `AgentSession` object, so a serialized and restored session restarts at S1; a traced agent (`WithTrace`) should run one turn at a time; MAF approval requests (`PendingApprovals`) are surfaced but TechieRag does not resume them for you.
- Ollama's request mapping sends no `tool_calls` on assistant messages and Gemini's sends tool results as a `tool` role rather than `functionResponse`; multi-turn tool use on those two providers relies on the server tolerating that (unchanged by REQ-RAG-067).

## Platform notes

### Platform support matrix (REQ-FN-054, BRD-88)

Each cell reads **supported** (built for it, no recorded run there yet), **tested** (the probe app ran there: device and date), or **not supported**. The same table is in the phase-1 BRD §9; change both together, and only from a recorded probe run (the runbook below, step "Record").

| Package | Windows | macOS / Mac Catalyst | Android | iOS |
|---|---|---|---|---|
| `TechieRag` | tested (Windows 11 laptop, Mi NoteBook Pro, probe Windows head, 2026-09-24) | supported | supported ¹ | supported |
| `TechieRag.Embedded` | tested (Windows 11 laptop, Mi NoteBook Pro, probe Windows head, bge-m3, 2026-09-24) | supported | supported ¹ | supported |
| `TechieRag.Agents` | not supported ² | not supported ² | not supported ² | not supported ² |
| `TechieRag.Local` | tested (Windows 11 laptop, Mi NoteBook Pro, probe Windows head, Phi-3 mini, 2026-09-25) ³ | tested (owner's Mac, Apple M4 Max 36 GB, macOS 27, probe Mac Catalyst head, Phi-3 mini, 2026-09-25) | supported ³ | supported ³ |

1. Android emulator run on 2026-09-24 (Pixel 5 profile, Android 12, x86_64): all-MiniLM-L6-v2 picked by default, files in the app's data folder, top result correct. An emulator is not a phone, so the cell waits for the owner's phone run.
2. Being built (REQ-RAG-045).
3. ONNX Runtime GenAI 0.16.0 on all four platforms (`DECISIONS.md` 2026-09-25); `TechieRag.Local.targets` adds the Mac Catalyst library GenAI's package leaves out (REQ-FN-058). Windows: real-engine conformance passed with both models (native `dotnet test`) and the probe generated (2026-09-25). iOS and Android ran the probe on a **simulator** and an **emulator** only, so those cells wait for the owner's phones; the xUnit suite does not run on Android, so the probe run is its proof. Numbers: "Local model: measured per platform".

### Local model: measured per platform (REQ-FN-060, BRD-108)

One real measurement per row, with device and date; change a row only from a recorded run (the runbook's step "Record").

| Platform | Device | Date | Model | How measured | First token (s) | Tokens per second | Peak memory |
|---|---|---|---|---|---|---|---|
| Windows 11 | Windows 11 laptop (Mi NoteBook Pro, Core i5-11300H, 16 GB) | 2026-09-24 | Qwen2.5 0.5B (an earlier third-party ONNX copy, not TechieRag's conversion) | ONNX Runtime GenAI 0.16.0 directly, plain process | 0.2–0.4 | 45–52 | 555 MB |
| Windows 11 | Windows 11 laptop (Mi NoteBook Pro, Core i5-11300H, 16 GB) | 2026-09-24 | Phi-3 mini 4k (desktop default) | ONNX Runtime GenAI 0.16.0 directly, plain process | not recorded | 7.5 | 3.0–3.3 GB |
| macOS | owner's Mac (Apple M4 Max, 36 GB, macOS 27) | 2026-09-25 | Qwen2.5 0.5B (TechieRag's conversion) | ONNX Runtime GenAI 0.16.0 directly, plain process | 0.03 | 329–359 | 610 MB |
| macOS | owner's Mac (Apple M4 Max, 36 GB, macOS 27) | 2026-09-25 | Phi-3 mini 4k | ONNX Runtime GenAI 0.16.0 directly, plain process | 0.12 | 61–69 | 2.7 GB |
| Mac Catalyst | owner's Mac (Apple M4 Max, 36 GB, macOS 27) | 2026-09-25 | Phi-3 mini 4k (Catalyst default) | probe app, second button, Debug build, through `TechieRag.Local`; two runs | 0.11–0.52 | 65–73 | 1.96–2.17 GB |
| iOS (simulator) | iPhone 17 Pro simulator, iOS 26.1, on the owner's Mac (a simulator, not a device) | 2026-09-25 | Qwen2.5 0.5B (phone default, TechieRag's conversion) | probe app, second button, Debug build, through `TechieRag.Local` | 0.10 | 301 | 460 MB |
| Windows 11 | Windows 11 laptop (Mi NoteBook Pro, Core i5-11300H, 16 GB) | 2026-09-25 | Phi-3 mini 4k (desktop default) | probe app, second button, Debug build, through `TechieRag.Local`; a run just after the 2.7 GB download and a fresh launch | 0.26–0.32 | 7.4–12.9 | 3.41–3.44 GB |
| Android (emulator) | Android emulator `pixel_5_-_api_32` (Pixel 5 profile, Android 12, x86_64, 2 GB RAM) on the Windows 11 laptop (an emulator, not a phone) | 2026-09-25 | Qwen2.5 0.5B (phone default, TechieRag's conversion) | probe app, second button, Debug build, through `TechieRag.Local`; a run just after the 333 MB download and a fresh launch | 0.41 | 22.5–43.2 | 737–775 MB |
| Android | owner's Android phone | not yet run | — | — | — | — | — |
| iOS | owner's iPhone | not yet run | — | — | — | — | — |

Peak memory: Windows, peak working set; macOS process, peak resident set; Mac Catalyst and iOS, the probe's peak physical footprint (matches `footprint <pid>`, what the iOS limit counts). First token includes reading the prompt. Ranges span a run after the download and a fresh launch; the laptop and its emulator share one host, so speeds vary with load. Evidence: `tests/.artifacts/probe/` (per head) and `tests/.artifacts/local-llm-bench/`.

### Local model: engine comparison on the Windows laptop (2026-09-24)

Both engines directly on the Windows 11 laptop (Core i5-11300H, 16 GB; WSL gets 7.6 GB) before the owner chose one (`DECISIONS.md` 2026-09-25). Same prompt, greedy, 128 tokens, three runs per row on a shared host. Raw JSON: `tests/.artifacts/local-llm-bench/`.

| Platform | Model | Engine | Load (s) | First token (s) | Tokens per second | Peak memory |
|---|---|---|---|---|---|---|
| Windows 11 | Qwen2.5 0.5B (0.9 GB ONNX int4 / 0.5 GB GGUF q4_k_m) | ONNX Runtime GenAI 0.16.0 | 2.7–3.3 | 0.16–0.50 | 37–52 | 555 MB |
| Windows 11 | Qwen2.5 0.5B | LLamaSharp 0.27.0 | 1.0–1.5 | 0.43–0.79 | 23–31 | 509 MB |
| Windows 11 | Phi-3 mini 4k (2.7 GB ONNX int4 / 2.4 GB GGUF q4) | ONNX Runtime GenAI 0.16.0 | 12.5–24.7 | 1.0–2.1 (one cold 11.0) | 5.7–7.5 | 3.0–3.3 GB |
| Windows 11 | Phi-3 mini 4k | LLamaSharp 0.27.0 | 3.3–4.3 | 1.5–1.8 | 5.8–6.0 | 3.6 GB |
| WSL2 Ubuntu (same laptop) | Qwen2.5 0.5B | ONNX Runtime GenAI 0.16.0 | 1.1–1.5 | 0.09–0.18 | 74–86 (contended runs 25–35) | 582 MB |
| WSL2 Ubuntu | Qwen2.5 0.5B | LLamaSharp 0.27.0 | 0.4–0.6 | 0.12–0.23 | 58–64 (contended runs 10–34) | 612 MB |
| WSL2 Ubuntu | Phi-3 mini 4k | ONNX Runtime GenAI 0.16.0 | 5.3–6.1 | 0.52–1.44 | 6.1–8.4 | 3.1–3.3 GB |
| WSL2 Ubuntu | Phi-3 mini 4k | LLamaSharp 0.27.0 | 2.2–2.7 | 0.67–2.0 | 4.6–6.7 | 3.7 GB |

Both engines count three fixed sentences identically (Qwen2.5 10 / 13 / 12, Phi-3 12 / 14 / 13).

### What the packages do per platform

- **Model root (REQ-RAG-053).** Every model downloads to `<LocalApplicationData>/TechieRag/models/<model>`: `%LOCALAPPDATA%\TechieRag\models` on Windows; `~/Library/Application Support/TechieRag/models` on macOS and Mac Catalyst (inside the container when sandboxed); the app's private `files` folder on Android; `<app container>/Library/Application Support/TechieRag/models` on iOS. .NET reports `Documents` as `LocalApplicationData` on iOS and Mac Catalyst, so TechieRag uses `<home>/Library/Application Support` there: models never land in the user's Documents (proved 2026-09-25 on Mac Catalyst and the iOS 26.1 simulator). Move it with `builder.UseModelRoot(path)`, `ModelRoot.Set(path)` or `TECHIERAG_MODEL_ROOT` before the first load. A complete copy an older version left next to the assembly is still used.
- **Phone default (REQ-RAG-054).** `UseEmbedded()` picks bge-m3 (1024 dimensions, 2.3 GB) on Windows, macOS and Mac Catalyst, and all-MiniLM-L6-v2 (384 dimensions, 91 MB, English) on Android and iOS. `UseEmbedded(EmbeddedModel.BgeM3)` on a phone throws `NotSupportedException` naming 2.3 GB. Vectors from the two models are not comparable; a corpus is re-embedded when the model changes (the embedding signature already flags it).
- **Download size and progress (REQ-RAG-055).** `ModelDownloadService.Instance.DownloadSizeKnown` fires once per download with the bytes still to fetch, before the first byte; set `e.Decline = true` to stop (for example off Wi-Fi) and the call throws `ModelDownloadDeclinedException`. `ProgressChanged` then reports `BytesDownloaded` of `TotalBytes`. Interrupted downloads resume from `<file>.part`. The reranker downloads through the same service, and `DownloadAsync(modelName, folder, files)` is public for later packages.
- **Local model (REQ-RAG-057 to REQ-RAG-066).** `UseLocalLlm()` picks Qwen2.5 0.5B Instruct (Apache-2.0) on Android and iOS with a 2,048-token context, and Phi-3 mini 4k Instruct (MIT) elsewhere with 4,096; `UseLocalLlm("phi-3-mini-4k-instruct")` on a phone throws `NotSupportedException`. Nothing downloads until the host accepts the model's terms (`LocalLlmOptions.TermsAccepted` or `ConfirmTermsAsync`, which receives the licence, its URL and the download size); the files then come once from Hugging Face (Qwen: `techierathore/Qwen2.5-0.5B-Instruct-onnx-genai`; each pinned to a commit) through `ModelDownloadService` (size first, resumable, `TECHIERAG_MODEL_BASE_URL` as `<mirror>/<model>-<format>/<file>`) into `<ModelRoot>/<model>-<format>`, each checked against its SHA-256; after that the model works offline. Before loading, free memory (`MemAvailable` on Android, `os_proc_available_memory` on iOS, `GlobalMemoryStatusEx` on Windows, the runtime's estimate on Mac Catalyst) is compared with weights + context + 256 MB, and a shortfall throws `LocalModelMemoryException` instead of letting the system end the app.
- **Any Hugging Face model (REQ-RAG-108).** `UseLocalLlm(LocalModel.FromHuggingFace("Arm/gemma-3-1b-instruct-onnx-genai-int4-emb-int8"))` runs any public ONNX Runtime GenAI model by name (optional `folder` and `version`). The model card's licence, terms URL and size reach `ConfirmTermsAsync` (or `GetTermsAsync()`) before any file; only the engine's files download, each checked against Hugging Face's fingerprint (SHA-256, or the git SHA-1 of a small file). An unpinned name is pinned to one commit once, so later starts are offline. Gated or private models are refused (no token); `TECHIERAG_MODEL_BASE_URL` does not apply. The model's own chat template and `genai_config.json` limits are used.
- **Native ONNX Runtime (REQ-FN-055).** `TechieRag.Embedded` ships `buildTransitive/TechieRag.Embedded.targets`: on Mac Catalyst it links ONNX Runtime's static xcframework (ONNX Runtime's own package links nothing there); on iOS and Mac Catalyst Debug builds it compiles a `RegisterCustomOps` stub; on Android it checks ONNX Runtime's `.aar` (arm64-v8a, x86_64) arrived. A consuming app writes no native wiring. Properties: `TechieRagOnnxRuntimeVersion` (when the app pins another ONNX Runtime), `TechieRagDisableOnnxNativeWiring`, `TechieRagDisableOnnxCustomOpsStub`.
- **Native ONNX Runtime GenAI (REQ-FN-058).** `TechieRag.Local` ships `buildTransitive/TechieRag.Local.targets`: on Mac Catalyst it links the `ios-arm64_x86_64-maccatalyst` slice of GenAI's xcframework (GenAI's own package links nothing there); on Android it checks GenAI's `.aar` (arm64-v8a, x86_64) arrived. iOS gets GenAI's xcframework from GenAI's own package, and Windows, macOS and Linux get its native library as a NuGet runtime asset. A consuming app writes no native wiring. Properties: `TechieRagOnnxRuntimeGenAIVersion` (when the app pins another GenAI), `TechieRagDisableLocalNativeWiring`.
- **SQLite search (REQ-RAG-056).** `SqliteVecStore` search is an exact cosine scan in managed code on every platform, and it is the supported path: it reads only the vector column, scores each stored vector in place with SIMD, keeps the best top-K in a bounded heap, then fetches the winners' rows. sqlite-vec is not used (the dead loading code is removed): it is a native extension phones and the Catalyst sandbox do not load. Measured 2026-09-24 with `SqliteSearchBenchmarkTests` (`TechieRagSearchBenchmark=1`), WSL Ubuntu 24.04 on the Windows 11 laptop above, 8 logical CPUs, .NET 10.0.10, top-10, median of 5 after a warm-up; the old path is the pre-2026-09-24 implementation, kept in the tests as the reference:

| Chunks | Dimensions | Old path (ms) | New path (ms) | Speed-up | Top-10 ranking |
|---:|---:|---:|---:|---:|---|
| 1,000 | 1024 | 72.2 | 16.3 | 4.4x | identical |
| 10,000 | 1024 | 625.4 | 69.7 | 9.0x | identical |
| 50,000 | 1024 | 1896.4 | 238.2 | 8.0x | identical |
| 1,000 | 384 | 12.1 | 3.7 | 3.3x | identical |
| 10,000 | 384 | 177.8 | 27.1 | 6.6x | identical |
| 50,000 | 384 | 690.4 | 108.6 | 6.4x | identical |

Raw output: `tests/.artifacts/benchmarks/sqlite-search.md` and `.json`. Re-run: `TechieRagSearchBenchmark=1 dotnet test tests/TechieRag.Tests --filter SqliteSearchBenchmarkTests`.

### The probe app (REQ-FN-056)

`samples/TechieRag.Probe` is a .NET MAUI app with Windows, Mac Catalyst, Android and iOS heads. Its one screen shows the platform, the model `UseEmbedded()` picks there and the model root; the button **Embed, store, search** (AutomationId `RunEmbedButton`) embeds three sentences, stores them in SQLite, searches "What is the capital of France?" and shows the top result (`TopResultLabel`), the load, embed, store and search timings (`TimingsLabel`) and one result line (`ResultLineLabel`, also written to the console with the prefix `TECHIERAG_PROBE_RESULT:` and to `probe-result.txt` in the app data folder). A pass is `Done` with the top result "Paris is the capital city of France." The first press includes the model download (2.3 GB on desktops, 91 MB on phones); press it a second time for warm timings. The second button, **Generate one sentence** (AutomationId `RunGenerateButton`, REQ-FN-059), loads the platform's default local model through `TechieRag.Local` (a terms dialog before the one-time download), asks for one sentence about the sea and shows it (`GeneratedSentenceLabel`), the load time, time to first token, tokens per second and peak memory (`GenerationTimingsLabel`) and its own status (`GenerateStatusLabel`); the result line gets the prefix `OK generate`. Peak memory is the peak physical footprint on iOS and Mac Catalyst and the peak working set elsewhere. Unattended: `TECHIERAG_PROBE_AUTORUN=local` (Android: the intent extra `--ez autorunlocal true`) presses it once the page appears; a first run still waits on the terms dialog's **Accept**, which an unattended run presses through the app's own automation (AutomationId / accessibility label), never by skipping it. Ran 2026-09-25 on Mac Catalyst (Phi-3 mini), the iOS 26.1 simulator (Qwen2.5 0.5B, through a local mirror), the Windows 11 laptop (Phi-3 mini) and the Android emulator `pixel_5_-_api_32` (Qwen2.5 0.5B from its default Hugging Face address); numbers under "Local model: measured per platform". Windows: `Invoke-ProbeWindows.ps1 -ButtonId RunGenerateButton -StatusId GenerateStatusLabel -AcceptTerms` presses **Accept** by AutomationId `PrimaryButton` in the probe's own window; Android: **Accept** is `android:id/button1` in the probe's package (`tests/.artifacts/probe/android-local-20260925/drive-android-local.sh`).

Recorded runs:

| Head | Device | Date | Model | Top result | Load / embed / store / search (ms) | Evidence |
|---|---|---|---|---|---|---|
| Windows | Windows 11 laptop (Mi NoteBook Pro) | 2026-09-24 | bge-m3 | Paris (0.796) | first press 149,242 (incl. 2.3 GB download) / 594 / 326 / 353; second run 21,271 / 1,361 / 330 / 345 | `tests/.artifacts/probe/windows-result*.txt`, `windows-probe*.png` |
| Android | emulator, Pixel 5 profile, Android 12 x86_64 | 2026-09-24 | all-MiniLM-L6-v2 | Paris (0.824) | first press 113,914 (incl. 91 MB download) / 1,268 / 2,753 / 4,040; second press 0 / 1,334 / 208 / 217 | `tests/.artifacts/probe/android-*` |
| Mac Catalyst | owner's Mac (Apple M4 Max) | 2026-09-25 | bge-m3 | Paris (0.796) | first press 107,208 (incl. 2.3 GB download) / 64 / 32 / 35; re-run 3,959 / 90 / 45 / 37 | `tests/.artifacts/probe/maccatalyst/` on the Mac |
| Android | owner's phone | — | — | — | — | not run yet |
| iOS | owner's iPhone | — | — | — | — | not run yet |

CI (`.github/workflows/probe.yml`, REQ-FN-057) builds all four heads on every push (Mac Catalyst and iOS on the macOS runner, iOS for the simulator), presses the button on the Windows runner through UI Automation and on an Android emulator through the `autorun` intent, and ends with a "Probe heads" table on the run page: each head built, failed or not run.

### Owner's runbook: build, deploy, run and record the probe (REQ-FN-069, BRD-165)

Run every command from the repository root. Each head needs the .NET 10 SDK and its MAUI workload (`dotnet workload install maui` installs all of them).

**Windows (any Windows 10 or 11 PC)**

1. Build: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-windows10.0.19041.0`
2. Deploy: nothing to install; the app is `samples\TechieRag.Probe\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TechieRag.Probe.exe`.
3. Run: start it and press **Embed, store, search**, or let the script press it and save the result: `powershell -ExecutionPolicy Bypass -File samples\TechieRag.Probe\scripts\Invoke-ProbeWindows.ps1 -Exe <the exe above> -OutDir tests\.artifacts\probe`. Wait for `Done` (the first press downloads 2.3 GB), then press once more for warm timings.
4. Record: step "Record" below.

**Mac (Mac Catalyst head)**

1. Once: install the Xcode that matches the .NET iOS workload (Xcode 26 for .NET 10), open it once to accept its licence, then `sudo dotnet workload install maui-maccatalyst`.
2. Build: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-maccatalyst`. A Debug build compiles the ONNX custom-ops stub with `xcrun clang`. A link error naming `RegisterCustomOps` or `onnxruntime` means the native wiring failed: copy the error into the Remarks of REQ-FN-055 and stop.
3. Deploy and run: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-maccatalyst -t:Run` opens the app. Press **Embed, store, search**, wait for `Done` (the first press downloads 2.3 GB), then press it again.
4. Record: step "Record".

**Android phone**

1. Once: on the phone open Settings, About phone, tap Build number seven times; then in Developer options turn on USB debugging. Connect it by USB, accept the prompt, and check that `adb devices` lists it (`adb` is in the Android SDK's `platform-tools`).
2. Build: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-android`
3. Deploy: `adb install -r samples/TechieRag.Probe/bin/Debug/net10.0-android/com.techierathore.techierag.probe-Signed.apk`
4. Run: open **TechieRag Probe** on the phone and press **Embed, store, search** (the first press downloads 91 MB, so use Wi-Fi), then press it again. Unattended alternative: `bash samples/TechieRag.Probe/scripts/run-android-emulator.sh <the apk> tests/.artifacts/probe` works against a connected phone too and saves the result line and a screenshot.
5. Record: step "Record".

**iPhone**

1. Once, on the Mac: sign Xcode in to your Apple ID (Xcode, Settings, Accounts); `sudo dotnet workload install maui-ios`; connect the iPhone by USB, trust the Mac, and turn on Developer Mode (Settings, Privacy and Security). In Xcode create any iOS app with bundle identifier `com.techierathore.techierag.probe` and run it once on the phone: that creates the development provisioning profile the probe signs with.
2. Build: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-ios -p:RuntimeIdentifier=ios-arm64`
3. Deploy and run: `dotnet build samples/TechieRag.Probe/TechieRag.Probe.csproj -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 -t:Run -p:_DeviceName=<the iPhone's UDID, from Finder or xcrun devicectl list devices>`. On the phone trust the developer if asked (Settings, General, VPN and Device Management), open **TechieRag Probe**, press **Embed, store, search** (91 MB on the first press), then press it again.
4. Record: step "Record".

**Record (every device)**

1. Keep the `ResultLineLabel` text of both presses (a screenshot is enough) and note the device model and the date.
2. Add a row to "Recorded runs" above: head, device, date, model, top result, the four timings of both presses.
3. When the status is `Done` and the top result is the Paris sentence, change that platform's `TechieRag` and `TechieRag.Embedded` cells in the matrix above and in the phase-1 BRD §9 to `tested (<device>, <date>)`. If it failed, leave `supported` and write the error in the Remarks of REQ-FN-056.
4. Press **Generate one sentence** (AutomationId `RunGenerateButton`, REQ-FN-059) the same way, accept the model's terms in the dialog, and record the time to first token, tokens per second and peak memory from `GenerationTimingsLabel` in that platform's row of "Local model: measured per platform" above (device, date, model), and set the `TechieRag.Local` cell to `tested (<device>, <date>)`. Phones and the iPhone download Qwen2.5 0.5B (333 MB) from its default address on Hugging Face (`techierathore/Qwen2.5-0.5B-Instruct-onnx-genai`, pinned commit); no `TECHIERAG_MODEL_BASE_URL` is needed (first used on 2026-09-25 by the Android emulator and the Windows tests, every file's SHA-256 matched).
5. Or paste the result lines to an agent with "record this probe run"; it makes the same edits.

## How to call it

Install from nuget.org (no token, no `nuget.config` edit):

```
dotnet add package TechieRag
dotnet add package TechieRag.Embedded
dotnet add package TechieRag.Telemetry   # once REQ-FN-065 publishes it
dotnet add package TechieRag.Local       # in-process model; runs once a runtime is chosen per platform
```

Register through the builder or DI:

```csharp
using TechieRag;
using TechieRag.Embedded;

var rag = new TechieRagBuilder()
    .UseEmbedded()                       // embeddings: the model downloads once, then works offline
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
| Typed streaming | `await foreach (LlmStreamEvent e in provider.ChatStreamEventsAsync(messages, options)) { /* e.Kind: TextDelta → e.Text, ToolCall → e.ToolCall, Completed → e.Usage, e.FinishReason */ }` |
| Streaming agent loop | `await foreach (AgentStreamEvent e in new AgentLoopRunner(provider, registry).RunStreamAsync(messages, options, progress)) …` |
| Agentic retrieval (classic loop) | `new ToolRegistry().RegisterKnowledgeBase(new TechieRagRetrievalSource(rag), new RetrievalToolOptions { TopK = 5 }, state); options.SystemPrompt = AgenticInstructions.Default;` |
| Agents (Microsoft Agent Framework) | `dotnet add package TechieRag.Agents` then `var agent = new TechieRagAgentBuilder(rag).UseLmStudio("http://localhost:1234", "qwen3-8b").Build(); var r = await agent.AskAsync("question"); // r.Answer, r.Sources, r.Searches` |
| Agents over TechieRag's own LLM, tools and trace | `new TechieRagAgentBuilder(rag).UseConfiguredLlm().WithToolHandler(registry).WithTrace(progress).WithChatHistoryProvider(new ConversationMemoryChatHistoryProvider(memory)).Build();` or `services.AddTechieRagAgent(b => b.UseConfiguredLlm());` |
| Seam adapters | `IChatClient c = new LlmProviderChatClient(provider); IList<AITool> t = ToolHandlerFunctions.From(handler); IToolHandler h = AIToolHandler.FromAgent(agent.Agent); aiAgent.WithAgentSteps(progress);` |
| MCP tool servers | `builder.WithTools(t => t.AddMcpServer(new McpServerConfig { Command = "npx", Args = … }));` |
| Flows | `var flow = FlowSerializer.Deserialize(json); await new FlowRunner(runtime).RunAsync(flow, input);` |
| Workspaces and threads | `builder.WithPersistence(StoreProvider.Sqlite, "Data Source=ws.db"); var ws = rag.GetWorkspaceManager()!;` |
| Reranking | `builder.WithReranker(RerankSource.LocalOnnx); await rag.SearchAsync("q", new SearchOptions { Rerank = true });` |
| Connectors | `await ConnectorRunner.RunAsync(new RepositoryConnector(options), rag, runOptions);` |
| Web ingestion | `await rag.IngestUrlAsync("https://…"); await rag.CrawlAsync("https://…", new WebCrawlOptions { MaxDepth = 1 });` |
| Token tracking | `var status = rag.GetTokenTracker().GetBudgetStatus();` |
| Telemetry | `services.AddTechieRagTelemetry(o => { o.EnableTracing = true; o.Endpoint = "http://localhost:4318"; });` |
| Local model | `builder.UseLocalLlm(o => o.ConfirmTermsAsync = (terms, ct) => AskUserAsync(terms.LicenceName, terms.TermsUrl));` or `UseLocalLlm("qwen2.5-0.5b-instruct")` or `UseLocalLlm(new DirectoryInfo(folder), LocalChatTemplate.ChatMl)`; `LocalLlm.Register()` for `LlmSource.Local` and `local/<model>` |

Connector stop codes (REQ-RAG-082): switch on these `ConnectorErrorCodes` constants, never on the English message. A budget that ends the run early comes back on `ConnectorRunResult.LimitCode` / `ConnectorIngestionResult.LimitCode` with `ReachedLimit = true` (sync state kept; the next run resumes); a limit that fails the run is thrown as `ConnectorException` with `ErrorCode` set.

| Constant | Value | Where it appears | Meaning |
|---|---|---|---|
| `RunByteBudgetReached` | `ConnectorRunByteBudgetReached` | `LimitCode` | fetched text reached `ConnectorRunOptions.MaxTotalBytes` |
| `RunItemLimitReached` | `ConnectorRunItemLimitReached` | `LimitCode` | `ConnectorRunOptions.MaxItems` items fetched |
| `RunPageLimitReached` | `ConnectorRunPageLimitReached` | `LimitCode` | `ConnectorRunOptions.MaxPages` pages listed |
| `ImapLiteralTooLarge` | `ConnectorImapLiteralTooLarge` | `ConnectorException.ErrorCode` | the IMAP server announced a literal beyond `ImapMailboxOptions.MaxMessageBytes`; dropped before allocating |
| `ImapResponseLineTooLong` | `ConnectorImapResponseLineTooLong` | `ConnectorException.ErrorCode` | the IMAP server sent an unterminated response line past the reader's line limit |

Exact signatures are in `docs/TechieRag-AI-Reference.md` (also installed into a consumer's repository as `.techierag/TechieRag-AI-Reference.md` on build); the maintainer's map of each service is `docs/TechieRag-DevGuide.md`.
