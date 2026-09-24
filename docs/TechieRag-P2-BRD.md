# TechieRag — Business Requirements — Phase 2: Agents, platforms, local model, and the harvested v3 library

| | |
|---|---|
| App | TechieRag |
| Kind | library |
| Size | Medium |
| Phase | 2 of 2 |
| Status | Approved |
| Date | 2026-09-24 |

## 1. Summary

Phase 2 adds what makes the library go where its consumers go. From the 2026-09-03 amendments: the agentic retrieval contract in core and the `TechieRag.Agents` package on Microsoft Agent Framework, and the repository separation that gave the application, now **Sevak**, its own repository (executed 2026-09-24). From the 2026-09-24 amendments: four-platform groundwork inside .NET MAUI apps, the `TechieRag.Local` package that runs a language model in-process with no server and no network after one download, typed streaming events so a tool-using turn can stream, and subscription sign-in through a browser flow the host app drives. Harvested on 2026-09-24 from the application's ledger, where they had been recorded between July and September 2026: the v3 library features already built and mostly verified (chunking strategies, more formats, web ingestion, data connectors, MCP tools, flow orchestration, workspaces and persistent memory, reranking, provider breadth, the telemetry package), each given its own TechieRag id with its status carried in. Finally, the packaging and configuration gaps the 2026-09-24 code scan found. Ids run on from phase 1: BRD-83 to BRD-163.

## 2. Screens and flow

Each row is a public surface this phase adds or extends; the Route column names its entry point. No mockups exist for a library.

| Screen | Route | Role | Mockup | Fields |
|---|---|---|---|---|
| Agents package | `TechieRag.Agentic`, `TechieRag.Agents`: `TechieRagAgentBuilder` | Agent builder | — (library, no mockup) | search_knowledge_base, list_documents, seam adapters |
| Packaging and quality follow-ups | `TechieRag.Telemetry`, `publish-*.yml`, `TechieRagBuilder.Build()` | Maintainer | — (library, no mockup) | Telemetry package, dimensions, config mapping, one publish path |
| Repository separation | `TechieRag.slnx`, Sevak repository | Maintainer | — (library, no mockup) | src/, tests/, packages 1.0.7 |
| Platform groundwork | `ModelPaths`, `TechieRag.Embedded.targets`, `samples/TechieRag.Probe` | MAUI app developer | — (library, no mockup) | app-data root, native wiring, 384-dim default, matrix |
| Local model | `TechieRag.Local`: `UseLocalLlm()` | App developer | — (library, no mockup) | model id, folder, memory gate, terms, download |
| Typed streaming | `ILlmProvider` typed streaming method, `AgentLoopRunner` streaming run | Developer | — (library, no mockup) | LlmTextDelta, LlmToolCallEvent, LlmStreamCompleted |
| Subscription sign-in | `UseChatGptSubscriptionLlm(callback)`, `LlmConnectorCatalog` | App developer | — (library, no mockup) | URL, user code, session store, VendorTerms |
| Ingestion breadth | `IChunker`, `Processors/`, `Web/` | Developer | — (library, no mockup) | chunking strategy, XLSX/PPTX/CSV/audio, URL, crawl, YouTube |
| Data connectors | `IDataConnector`, `ConnectorRunner`, `Connectors/` | Developer | — (library, no mockup) | repository, Confluence, email, sync state, transport guards |
| MCP tools | `McpClient`, `McpToolHandler`, `IMcpServerRegistry` | Agent builder | — (library, no mockup) | stdio, HTTP, trust policy |
| Flow orchestration | `FlowRunner`, `FlowRuntime`, `IFlowGuardrail`, `AgentToolHandler` | Agent builder | — (library, no mockup) | nodes, conditions, MaxSteps, guardrails, FlowMessage |
| Workspaces and memory | `WorkspaceManager`, `IWorkspaceStore`, `IConversationStore`, `DbConversationMemory` | Developer | — (library, no mockup) | threads, pinning, threshold, chat mode, dedupe |
| Reranking | `WithReranker`, `IReranker`, `SearchOptions.Rerank` | Developer | — (library, no mockup) | TopN, CandidateCount, source |
| Provider breadth | `ModelRouter`, `LlmConnectorCatalog`, `Embedding/`, `IMultimodalLlmProvider`, `Speech/` | Developer | — (library, no mockup) | model-name routing, embedders, images, prompt caching, speech |

**Primary journey:**
1. An app developer references the packages inside a MAUI app; the native wiring and writable model folders arrive from the packages, and the probe app proves all four heads.
2. The developer calls `UseLocalLlm()`; the model downloads once after the user accepts its terms, and answers with no network.
3. A tool-using chat turn streams its text while tool calls run through the guards; a user with a permitting vendor subscription signs in once through the browser.

## 3. Requirements

One item per thing the verifier will test, grouped by surface. Same rules as the phase-1 BRD. Items 83 to 114 are carried verbatim from the previous BRD; items 115 to 163 were harvested on 2026-09-24 and are attributed in their text.

### Agents package

Agents on Microsoft Agent Framework over TechieRag, with the retrieval contract in core.

- **BRD-83** — The library shall provide, in core with no new package dependencies, an agentic retrieval contract (`TechieRag.Agentic`): `search_knowledge_base` and `list_documents` tools with a stable model-facing description and JSON schema; a structured tool result carrying citation refs, source document, page, relevance score, a `strong` / `weak` / `none` / `limit_reached` status and a next-step hint; a per-turn search budget; and default retrieve-first, re-search-on-weak, cite-by-ref instructions — bindable to `ToolRegistry` / `IToolHandler` and to `TechieRag.Agents`, over an `IRetrievalSource` that may be an `ITechieRag` or a delegate *(F-AGENT; added 2026-09-03)* *Screen:* Agents package
  - *Acceptance:* When a developer registers the knowledge-base tools from `TechieRag.Agentic` on the agent loop, then `search_knowledge_base` returns refs, scores and a strong/weak/none status.
- **BRD-84** — A developer can add a Microsoft Agent Framework 1.20 agent over TechieRag by referencing `TechieRag.Agents` and using `TechieRagAgentBuilder` in the existing fluent style — `UseLmStudio(endpoint, model)` as the primary local target, `UseOllama`, `UseOpenAI`, `UseOpenAICompatible`, `UseConfiguredLlm()`, `UseCustomChatClient(...)` — obtaining an `ITechieRagAgent` that exposes the MAF `AIAgent`, creates `AgentSession`s, answers with typed `SearchResult` sources and retrieval traces, streams `RagStreamEvent`s, and registers through `AddTechieRagAgent(...)`; the package targets `net10.0;net8.0`, ships no MSBuild targets, never uses `HarnessAgent` or MAF hosted tools, and keeps model-facing strings invariant English *(F-AGENTS)* *Screen:* Agents package
  - *Acceptance:* When a developer builds an agent with `TechieRagAgentBuilder` over a TechieRag instance, then the agent answers from the documents through Microsoft Agent Framework.
- **BRD-85** — `TechieRag.Agents` shall expose public interop adapters at the library's seams so a package consumer keeps one provider configuration, one tool catalogue, one egress gate and one trace: `ILlmProvider` → `IChatClient` (all providers, routing, retry, fallback and token events preserved); `IToolHandler` → `AITool` (raw JSON schema, `RequiresConfirmation` → approval-required) and `AITool` / `AIAgent` → `IToolHandler` (so a MAF agent can be a flow node); MAF middleware → `IProgress<AgentStep>` emitting only the existing four `AgentStepKind`s; `IConversationMemory` → `ChatHistoryProvider` *(F-AGENTS)* *Screen:* Agents package
  - *Acceptance:* When a developer adapts an `ILlmProvider`, `IToolHandler` or `IConversationMemory` through the public seam adapters, then the agent uses them unchanged.

### Packaging and quality follow-ups

Closes the packaging, configuration and store gaps the 2026-09-24 code scan found.

- **BRD-86** — `TechieRag.Agents` shall be packed and published alongside the other two packages on both feeds at the same version, with the same SourceLink, symbol and README metadata *(F-PKG; added 2026-09-03)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a maintainer runs either publishing workflow on GitHub Actions, then `TechieRag.Agents` is packed and pushed at the same version with the same metadata.
- **BRD-156** — Both publishing workflows shall pack and publish `TechieRag.Telemetry` with the same SourceLink, symbol and README settings as the other packages; today no workflow packs it *(from the 2026-09-24 code scan, Architecture open question 4)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a maintainer runs either publishing workflow on GitHub Actions, then a `TechieRag.Telemetry` package is packed and pushed at the same version.
- **BRD-157** — The shipped `IVectorStore` set is SQLite, pgvector and Qdrant; `PgVectorStore` shall be proven against a real PostgreSQL through `LivePgVectorStoreTests` (`TechieRagTestPostgres`) before it counts as delivered *(TechieRag checklist REQ-RAG-044, migrated 2026-09-03 from TechieDesk BRD-125; Implemented 85%)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When `LivePgVectorStoreTests` run against a real PostgreSQL, then upsert, search and delete pass.
- **BRD-158** — `TechieRagBuilder.Build()` shall pass the embedding provider's `Dimensions` into every vector store instead of the 1024 default, so Cohere, OpenAI and Gemini embedders work with pgvector and Qdrant *(from the 2026-09-24 code scan, Architecture open question 2)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a developer builds with a 1536-dimension embedder and pgvector, then the `Embedding` column is `vector(1536)`.
- **BRD-159** — `AddTechieRag(IConfiguration)` and `AddTechieRag(TechieRagConfig)` shall map every configuration field the builder accepts, including `VectorStore.ApiKey`, embedding `Dimensions`, `ApiFormat`, `ApiPath`, `RequestDelayMs` and the `Prompt` section *(from the 2026-09-24 code scan, Architecture open question 5)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a developer sets `VectorStore.ApiKey` and `Prompt.SystemPrompt` in appsettings, then the built instance uses both.
- **BRD-160** — Exactly one path shall publish to nuget.org: the manual dispatch of `publish-nuget.yml`; the automatic `publish-nuget-org` job in `publish-github-packages.yml` is removed *(from the 2026-09-24 code scan, Architecture open question 3; DECISIONS.md 2026-09-03)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a maintainer pushes a `v*` tag on GitHub, then GitHub Packages receives the packages and nuget.org receives nothing until the manual dispatch.
- **BRD-161** — Image generation, realtime audio, batch, fine-tuning, moderation and OCR endpoints are deferred and re-scoped on demand *(TechieRag checklist REQ-RAG-046, migrated 2026-09-03 from TechieDesk BRD-127; Not Started)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a reader checks the deferred endpoints, then the BRD says deferred and no code claims them.
- **BRD-162** — Unit tests shall cover processors, providers, the agent loop, memory and cost math; live tests are gated by attributes that skip with a reason *(harvested 2026-09-24 from the TechieDesk BRD BRD-111, library part; TechieDesk checklist REQ-RAG-030 Verified; 634 test methods on 2026-09-24)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When `dotnet test tests/TechieRag.Tests` runs on a host without live credentials, then every non-live test passes and live tests skip with a reason.
- **BRD-163** — Consumer-facing install documentation shall lead with the public feed (`dotnet add package TechieRag` / `TechieRag.Embedded`, no authentication, no PAT, no `nuget.config` edit); GitHub Packages survives only as a labelled internal-builds section *(TechieRag checklist REQ-FN-004, Verified 2026-09-03; had no BRD item)* *Screen:* Packaging and quality follow-ups
  - *Acceptance:* When a reader opens the README installation section, then the first path is `dotnet add package TechieRag` from nuget.org with no token.

### Repository separation

This repository holds the library only; the application consumes the packages.

- **BRD-87** — TechieDesk shall move to its own repository: `apps/*`, `tests/TechieDesk.Tests`, `tests/appium`, `tests/verify`, `playwright.config.ts`, `publish-desktop.yml`, the TechieDesk docs, mockups, screenshots, `uiIssues/` and an app-configured `.tfcore/` leave this repository; the solution keeps `src/*` and `tests/TechieRag.Tests` only; the README's sample-application section links to the TechieDesk repository; TechieDesk consumes the three packages from NuGet at pinned versions; and library requirements are ledgered in this BRD and checklist from 2026-09-03 *(F-REPO)* — **amended 2026-09-24:** the new repository is **Sevak** (TechieDesk renamed in full at the split: repository, projects, namespaces, bundle id, artefacts, documents; requirement ids keep their numbers); the app paths were copied there on 2026-09-24 and its package-consumption goal `Sevak#REQ-FN-054` started; the deletion here waits for Sevak to build and test green from the published 1.0.7 packages; the README links to the Sevak repository, which is private until Sevak v0.1; cross-repository references carry the repository name (`Sevak#REQ-FN-054`, `TechieRag#REQ-RAG-054`) *Screen:* Repository separation
  - *Acceptance:* When a maintainer opens the repository after the split, then only `src/`, `tests/TechieRag.Tests` and library docs remain and Sevak consumes the 1.0.7 packages.

### Platform groundwork

Makes the packages work inside .NET MAUI apps on Windows, macOS, Android and iOS.

- **BRD-88** — The BRD and the UsageGuide shall carry a platform support matrix stating, per package (`TechieRag`, `TechieRag.Embedded`, `TechieRag.Agents`, `TechieRag.Local`) and per platform (Windows, macOS / Mac Catalyst, Android, iOS), whether it is supported, tested on a named device, or not supported; a cell is never "supported" without a recorded run *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a reader opens the UsageGuide's platform matrix, then every package × platform cell reads supported, tested (device and date) or not supported.
- **BRD-89** — Model and cache folders shall no longer default to `AppContext.BaseDirectory`; the default root is the per-user application data folder (`Environment.SpecialFolder.LocalApplicationData`), writable on all four platforms, the host can override it, and the embedding model, the reranker and the local language model share that root *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a developer calls `UseEmbedded()` in a MAUI app with no override, then model files land under the per-user application data folder on every platform.
- **BRD-90** — `TechieRag.Embedded` shall ship `buildTransitive` MSBuild targets that give a consuming app the correct native ONNX Runtime wiring on Mac Catalyst and iOS (the static xcframework `NativeReference`, `ForceLoad`, the CoreML weak framework, the Debug custom-ops stub and the interpreter setting hand-written today in the app csproj) and on Android (arm64-v8a and x86_64), so no consuming app writes native wiring by hand; Sevak's csproj then removes its copy *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a developer builds the probe app for Mac Catalyst, iOS or Android with no native wiring in its csproj, then ONNX Runtime loads and embeds.
- **BRD-91** — On Android and iOS, `UseEmbedded()` with no explicit model shall use a small 384-dimension model (`UseMiniLM` / `UseBgeSmall` class) and shall refuse bge-m3 with a clear message naming its 2.3 GB size; desktop defaults are unchanged *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a developer calls `UseEmbedded()` on Android or iOS, then the 384-dimension model is selected and asking for bge-m3 throws a message naming 2.3 GB.
- **BRD-92** — Every model download shall report its total size before starting so the host can ask for Wi-Fi or decline, and shall expose progress through the existing `ModelDownloadService` event shape *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a model download starts in any consuming app, then a size-known event fires before the first byte and progress events follow.
- **BRD-93** — `SqliteVecStore`'s managed similarity search shall be made efficient (vectorised cosine similarity) and documented as the supported path on all platforms; the dead sqlite-vec loading code is removed or marked not implemented; search time for 1,000, 10,000 and 50,000 chunks is measured and recorded in the UsageGuide *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a developer runs the search benchmark on 1,000, 10,000 and 50,000 chunks, then the recorded timings and identical ranking to the old path appear in the UsageGuide.
- **BRD-94** — A sample app `samples/TechieRag.Probe` (.NET MAUI; Windows, Mac Catalyst, Android and iOS heads) shall have one screen and one button that embeds three texts, stores them, searches, and shows the top result with timings; it is the four-platform proof for every package *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a user presses the probe app's button on Windows, Mac Catalyst, Android or iOS, then the top result and embed, store and search timings appear on screen.
- **BRD-95** — CI shall build the probe for all four heads on every push (macOS runner for Catalyst and iOS) and run the probe's button on an Android emulator where CI allows; a head that cannot be built in CI is reported as such, never skipped silently *(F-PLATFORM)* *Screen:* Platform groundwork
  - *Acceptance:* When a developer opens the workflow run on the CI run page after a push, then all four probe heads show built, failed or not run.

### Local model

Runs a language model inside the app process, no server, no network after one download.

- **BRD-96** — A developer can add an in-process local language model by referencing `TechieRag.Local` and calling `.UseLocalLlm()`, registered through `UseCustomLlmProvider`, with a platform-appropriate default model (small on Android and iOS, larger allowed on desktop) and overloads for a model id or a local folder; nothing of it enters the core package *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer references `TechieRag.Local` and calls `UseLocalLlm()` in a MAUI app, then a provider with the platform default model answers a prompt.
- **BRD-97** — The package shall have one public provider and an internal runtime interface with one implementation per platform, chosen per the recorded comparison in `DECISIONS.md`; the provider's behaviour is identical on every platform and the consuming app never sees which runtime is used *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When the conformance suite runs on each platform that has a runtime, then every test passes and the app cannot observe which runtime is in use.
- **BRD-98** — The provider shall fully implement `ILlmProvider` including the typed streaming method of BRD-110, apply the model's chat template, honour `Temperature`, `MaxTokens`, `TopP`, `StopSequences`, `Seed` and cancellation, and refuse a prompt longer than the model context with a clear error *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When the conformance suite exercises every `ILlmProvider` member and option on the local provider, then each is honoured and an over-long prompt throws before inference.
- **BRD-99** — Before loading, the provider shall check available memory against the model's needs and refuse with a clear message rather than let the operating system kill the app *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer loads a model on a device with less free memory than it needs, then load throws a message naming the shortfall and the app keeps running.
- **BRD-100** — Weights shall be downloaded once to the application data root of BRD-89, resumable, honouring `TECHIERAG_MODEL_BASE_URL`, SHA-256 verified, size reported before starting, progress events in the `ModelDownloadService` shape *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer interrupts and restarts a model download in the probe app, then it resumes, verifies SHA-256 and reports size before starting.
- **BRD-101** — Download shall require explicit acceptance of the model's terms of use, surfaced to the host so it can show a dialog; weights are never inside the package or the app bundle *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer starts a model download without signalling terms acceptance, then nothing downloads and the terms URL is exposed to the host.
- **BRD-102** — The package shall ship `buildTransitive` targets so a consuming MAUI app gets the right native runtime libraries on all four platforms with no hand-written csproj changes *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer builds the probe app for all four heads with no native wiring in its csproj, then `TechieRag.Local` loads its runtime on each.
- **BRD-103** — `CompleteAsync<T>` and JSON mode shall return valid JSON for the requested schema (grammar- or schema-constrained where the runtime allows); `SupportsToolCalling` reports false until a test proves otherwise *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer calls `CompleteAsync<T>` with the three test schemas on the local provider, then each response parses into T.
- **BRD-104** — `LlmSource` shall gain a `Local` member, `LlmProviderFactory` an arm and `LlmConnectorCatalog` a row so `ModelRouter` resolves `local/<model>`; configuration needs no endpoint and no key *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer configures `LlmSource.Local` with no endpoint or key, then the factory builds the provider and `ModelRouter` resolves `local/<model>`.
- **BRD-105** — `EstimateTokenCount` shall use the model's real tokenizer *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a developer calls `EstimateTokenCount` on the fixed test strings, then the count equals the runtime tokenizer's count.
- **BRD-106** — The probe app of BRD-94 shall gain a second button that generates one sentence from the local model and shows time to first token and tokens per second *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a user presses the second button on the probe app's screen, then one generated sentence, time to first token and tokens per second appear.
- **BRD-107** — Live tests shall be gated by a `LiveLocalLlmFactAttribute` in a non-parallel collection and skip with a reason when no model is present *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When the test suite runs on a host with no local model, then every live local test skips with a printed reason and nothing fails.
- **BRD-108** — Tokens per second, time to first token and peak memory shall be measured and recorded per platform on named devices in the support matrix of BRD-88 *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a reader opens the support matrix, then tokens per second, time to first token and peak memory are recorded per platform with device and date.
- **BRD-109** — The UsageGuide shall gain a `TechieRag.Local` section, the platform matrix is updated, and every "offline" or "air-gapped" claim across the docs is corrected to "downloads once, then works offline" *(F-LOCAL-LLM)* *Screen:* Local model
  - *Acceptance:* When a reader opens the UsageGuide, then a `TechieRag.Local` section exists and no page claims offline without saying downloads once first.

### Typed streaming

Streams a reply as typed events so a tool-using turn can stream.

- **BRD-110** — `ILlmProvider` shall gain an additive streaming method that yields typed events — a text delta, a decided tool call (name, arguments, id), and a final record with usage and finish reason — so one turn can stream assistant text and still surface tool calls; `ChatStreamAsync` and `CompleteStreamAsync` stay unchanged and are implemented over it; all six shipped providers implement it (providers whose vendor streams tool-call deltas assemble them; the others emit the tool call as one event after the text); `SupportsStreaming` keeps its meaning; closes Chatur feedback TR-RAG-002 *(F-LLM; added 2026-09-24)* *Screen:* Typed streaming
  - *Acceptance:* When a developer streams a tool-using turn through the new method on any of the six providers, then text deltas, a tool-call event and a completed event arrive in order.
- **BRD-111** — `AgentLoopRunner` shall offer a streaming run over the typed events of BRD-110 that yields assistant text as it arrives and executes every tool call through `ToolRegistry` exactly as the non-streaming loop does; `TechieRag.Agents`' `IChatClient` adapter exposes the same through its streaming method *(F-AGENT; added 2026-09-24; closes Chatur TR-RAG-002 for agent turns)* *Screen:* Typed streaming
  - *Acceptance:* When a developer runs `AgentLoopRunner`'s streaming run on a tool-using prompt, then text streams as it arrives and the tool executes through `ToolRegistry`.

### Subscription sign-in

Lets a user bring a vendor subscription through a browser sign-in the host app drives.

- **BRD-112** — A developer can add a subscription-billed hosted LLM through a browser or device-code sign-in, one builder method per vendor whose terms permit it (first: OpenAI, `.UseChatGptSubscriptionLlm(signInCallback)`), where the library returns the sign-in URL and user code to the callback, the host app opens the browser, the library waits for completion and yields an `ILlmProvider`; the resulting session can be persisted and restored by the host through a documented seam so a user signs in once *(F-LLM; added 2026-09-24; closes Chatur TR-RAG-001)* *Screen:* Subscription sign-in
  - *Acceptance:* When a developer calls `UseChatGptSubscriptionLlm(callback)` in a test host, then the callback receives URL and code and a working provider is yielded after authorisation.
- **BRD-113** — `LlmSource` shall gain a subscription member, `LlmProviderFactory` an arm, and `LlmConnectorCatalog` one row per subscription vendor carrying the vendor's stated terms as text ("personal use only", "not permitted for third-party tools", or the vendor's own words) and the date checked, so `ModelRouter` resolves a subscription model name and a host can show the terms before sign-in; a vendor with no permitted flow has no method and a catalog row saying so *(F-LLM; added 2026-09-24)* *Screen:* Subscription sign-in
  - *Acceptance:* When a developer reads the connector catalog, then each subscription vendor row carries its terms text and check date, and Anthropic's row reads not permitted.
- **BRD-114** — Before BRD-112 is built, the current sign-in policy and flow of each vendor (OpenAI, Anthropic, Google, Groq, xAI, Meta) shall be checked against the vendor's live documentation and recorded in `DECISIONS.md` with the date; known at 2026-09-24: OpenAI permits its sign-in in external tools for personal use, Anthropic prohibits subscription tokens outside its own clients *(F-LLM; added 2026-09-24)* *Screen:* Subscription sign-in
  - *Acceptance:* When a reader opens `DECISIONS.md`, then one dated entry per vendor records its sign-in policy, source URL and what the flow returns.

### Ingestion breadth

Adds chunking strategies, more document formats and web ingestion to the core ingestion path.

- **BRD-115** — The system shall offer pluggable chunking through `IChunker` with recursive, token, markdown/code-aware and sentence strategies (`WithChunking`, `WithCustomChunker`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-107, library part; TechieDesk checklist REQ-RAG-026 Verified)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer sets `WithChunking(ChunkingStrategy.Markdown)` and ingests a markdown file, then chunks follow heading boundaries; each strategy has a test.
- **BRD-116** — The system shall extract text from XLSX, PPTX and CSV files through dedicated processors *(harvested 2026-09-24 from the TechieDesk BRD BRD-114, library part; TechieDesk checklist REQ-RAG-033 Verified)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer ingests an XLSX, a PPTX and a CSV file, then text from cells, slides and rows is extracted by the dedicated processor.
- **BRD-117** — The system shall ingest audio files through an `AudioTranscriptionProcessor` that transcribes via an OpenAI-compatible speech endpoint; a local Whisper ONNX path is not implemented *(harvested 2026-09-24 from the TechieDesk BRD BRD-121, library part; TechieDesk checklist REQ-RAG-040 Verified 90%)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer ingests an audio file with a speech endpoint configured, then its transcript is chunked and embedded.
- **BRD-118** — A developer can ingest a single web page by URL: fetch, clean to text, chunk and embed (`WebIngestionExtensions`, `WebPageReader`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-112/60, library part; TechieDesk checklist REQ-RAG-016 Verified)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer ingests a URL, then the page's readable text is fetched, cleaned, chunked and embedded as one document.
- **BRD-119** — A developer can crawl a site with depth and maximum-link limits (`SiteCrawler`, `WebCrawlOptions`) and ingest every page reached *(harvested 2026-09-24 from the TechieDesk BRD BRD-61, library part; TechieDesk checklist REQ-RAG-017 Verified)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer crawls a site with depth 2 and a link cap, then pages within the limits are ingested and none beyond.
- **BRD-120** — A developer can ingest a YouTube video's transcript by URL (`YouTubeTranscriptReader`); blocked today because YouTube's timed-text endpoint returns empty bodies, an owner decision on the approach is pending *(harvested 2026-09-24 from the TechieDesk BRD BRD-62, library part; TechieDesk checklist REQ-RAG-018 Blocked 40%)* *Screen:* Ingestion breadth
  - *Acceptance:* When a developer ingests a YouTube URL, then the transcript is chunked and embedded, or a clear coded error explains why not.
- **BRD-121** — Every outbound web fetch shall pass a connect-time SSRF guard (`HttpWebContentFetcher.CreateGuardedHandler`) that refuses private, loopback and link-local targets after redirects and DNS rebinding *(harvested 2026-09-24 from the TechieDesk BRD BRD-112, library part; TechieDesk checklist REQ-RAG-031 remarks)* *Screen:* Ingestion breadth
  - *Acceptance:* When a fetch redirects to a private or loopback address, then the guarded handler refuses the connection and the error names the guard.

### Data connectors

Pulls documents from repositories, Confluence and mailboxes through one connector framework.

- **BRD-122** — The system shall provide an `IDataConnector` framework with a `ConnectorRunner` that returns per-item results and per-item failure reasons and keeps incremental `ConnectorSyncState` *(harvested 2026-09-24 from the TechieDesk BRD BRD-113/65, library part; TechieDesk checklist REQ-RAG-032 Verified, REQ-FN-020 Implemented 90%)* *Screen:* Data connectors
  - *Acceptance:* When a developer runs a connector through `ConnectorRunner`, then per-item results and failures return and sync state advances.
- **BRD-123** — A developer can connect a GitHub or GitLab repository and ingest its files with branch and glob filters (`RepositoryConnector`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-63, library part; TechieDesk checklist REQ-RAG-019 Verified)* *Screen:* Data connectors
  - *Acceptance:* When a developer connects a repository with a branch and a glob, then only matching files on that branch are ingested.
- **BRD-124** — A developer can connect a Confluence space and ingest its pages, with scheme, host and port pinned on cursors and links so the token never reaches a host named in a response body (`ConfluenceConnector`, TR-RAG-018) *(harvested 2026-09-24 from the TechieDesk BRD BRD-64, library part; TechieDesk checklist REQ-RAG-020 Verified 95%)* *Screen:* Data connectors
  - *Acceptance:* When a developer connects a Confluence space, then its pages are ingested and no request leaves the configured host.
- **BRD-125** — A developer can ingest a mailbox over IMAP (generic, Gmail, Microsoft 365) or a local mbox file with folder, date, sender and subject filters, optional PDF/DOCX/XLSX/PPTX attachments, quoted-reply and signature stripping and incremental sync; TLS is required and plaintext IMAP refused (`EmailConnector`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-135, library part; TechieDesk checklist REQ-RAG-049 Implemented 90%: only the mbox path exercised)* *Screen:* Data connectors
  - *Acceptance:* When a developer connects an IMAP mailbox or an mbox file with filters, then matching messages and allowed attachments are ingested incrementally.
- **BRD-126** — Connector transports shall reuse the SSRF-guarded connect-time handler, cap a run at `MaxTotalBytes` (64 MB), and the IMAP client shall refuse command injection, cap literals by `MaxMessageBytes`, apply real asynchronous timeouts and cap line length and count (TR-RAG-017, 019, 027…030) *(harvested 2026-09-24 from the TechieDesk BRD BRD-113/135, library part; TechieDesk checklist REQ-RAG-032/049 remarks, fixed)* *Screen:* Data connectors
  - *Acceptance:* When a connector run exceeds `MaxTotalBytes` or an IMAP server sends an oversized literal, then the run stops with a coded error.
- **BRD-127** — `ConnectorRunner` shall hand each fetched document to ingestion as it arrives instead of collecting every document before ingesting, so a run stopped part-way keeps what it fetched; `IConnectorTransport` shall support methods beyond GET where a connector needs them (TR-RAG-020…022) *(harvested 2026-09-24 from the TechieDesk BRD BRD-113, library part; TechieDesk checklist open feedback)* *Screen:* Data connectors
  - *Acceptance:* When a connector run is cancelled after some documents were fetched, then those documents are already ingested.

### MCP tools

Consumes Model Context Protocol tool servers inside the agent loop.

- **BRD-128** — The system shall consume Model Context Protocol tool servers in the agent loop through `McpClient` with stdio and HTTP transports and `McpToolHandler` *(harvested 2026-09-24 from the TechieDesk BRD BRD-119, library part; TechieDesk checklist REQ-RAG-038 Verified)* *Screen:* MCP tools
  - *Acceptance:* When a developer registers an MCP server over stdio or HTTP, then its tools appear in the agent loop and execute through `McpToolHandler`.
- **BRD-129** — The system shall expose an `IMcpServerRegistry` with an in-memory implementation, a `McpTrustPolicy`, and the tool-to-server mapping (`McpToolHandler.ServerNameFor`, `McpWorkspaceTools.ToolsByServer`) so a host's egress guard knows which server each tool comes from (TR-RAG-041) *(harvested 2026-09-24 from the TechieDesk BRD BRD-86, library part; TechieDesk checklist REQ-RAG-023 Implemented 90%)* *Screen:* MCP tools
  - *Acceptance:* When a host asks which server a tool belongs to, then `ServerNameFor` answers and an untrusted server's tools are refused by the trust policy.

### Flow orchestration

Runs declarative multi-step agent flows with guardrails, handoffs and agent-as-tool.

- **BRD-130** — The system shall run declarative agent flows: five node kinds (Agent, Tool, Condition, Handoff, Terminal), `FlowCondition` routing as data with 11 operators, cycles refused unless `AllowCycles`, every run capped by `MaxSteps`, handoffs passing only `HandoffContextMode` text plus `CarryVariables`, agent-as-tool with bounded recursion, and `FlowStep : AgentStep` on the same trace channel *(TechieRag checklist REQ-RAG-042, migrated 2026-09-03 from TechieDesk BRD-123; Implemented 95%)* *Screen:* Flow orchestration
  - *Acceptance:* When a developer runs a five-node flow with a condition and a handoff, then routing follows the data and the run stops at `MaxSteps`.
- **BRD-131** — `IFlowGuardrail` shall run at Input, Output and ToolCall, deny by default, and `FlowRuntime.HostGuardrails` shall be set only by the host so a flow definition cannot name or bypass it; a host's egress confirmation plugs in as a `DelegateFlowGuardrail` *(harvested 2026-09-24 from the TechieDesk BRD BRD-123, library part; TechieDesk checklist REQ-NFR-013 Implemented 95%)* *Screen:* Flow orchestration
  - *Acceptance:* When a host guardrail denies a tool call, then the flow reports the denial and the flow definition cannot bypass the host guardrail.
- **BRD-132** — Every user-visible message the flow engine produces (refusals, budget exhaustion, validation) shall be a `FlowMessage` carrying a `FlowMessageCodes` code with arguments and an English fallback, never a bare English sentence, so the host renders it in the user's language *(TechieRag checklist REQ-RAG-050, migrated 2026-09-03 from TechieDesk BRD-91; Implemented 80%)* *Screen:* Flow orchestration
  - *Acceptance:* When the engine refuses a step, then the message reaching the host carries a `FlowMessageCodes` code with arguments.
- **BRD-133** — `AgentToolHandler.ForFlow` shall report a blocked or budget-exhausted inner run as an unsuccessful `ToolResult` (`IsSuccess = false`), never as a success, so a renderer shows it blocked *(TechieRag checklist REQ-RAG-051, migrated 2026-09-03 from TechieDesk BRD-123; Implemented 95%)* *Screen:* Flow orchestration
  - *Acceptance:* When an inner agent run is blocked or exhausts its budget, then `AgentToolHandler.ForFlow` returns a `ToolResult` with `IsSuccess` false.
- **BRD-134** — A flow definition shall round-trip through `FlowSerializer` and `FlowValidator` shall refuse an invalid graph (unreachable nodes, cycles without `AllowCycles`, unknown agents or tools) with coded validation messages before it runs *(harvested 2026-09-24 from the TechieDesk BRD BRD-92/123, library part; TechieDesk checklist REQ-UI-040 PARTIAL 70%, engine part)* *Screen:* Flow orchestration
  - *Acceptance:* When a developer serialises a flow and validates a graph with a cycle, then the round-trip is identical and validation refuses the cycle with a code.
- **BRD-135** — A flow run shall complete or fail within a configurable time limit; a Tool-node flow that waits on a host confirmation shall end with a coded timeout message instead of hanging *(harvested 2026-09-24 from the TechieDesk BRD REQ-FN-053, library part; TechieDesk checklist In Progress 40%)* *Screen:* Flow orchestration
  - *Acceptance:* When a Tool-node flow waits longer than the time limit for a confirmation, then the run ends with a coded timeout message.

### Workspaces and memory

Isolates documents and settings per workspace and persists conversations in a database.

- **BRD-136** — The system shall provide database-backed conversation memory with threads (`DbConversationMemory`, `IConversationStore`) on SQLite and PostgreSQL *(harvested 2026-09-24 from the TechieDesk BRD BRD-108, library part; TechieDesk checklist REQ-RAG-027 Verified; PostgreSQL untested)* *Screen:* Workspaces and memory
  - *Acceptance:* When a developer configures `WithPersistence(StoreProvider.Sqlite, ...)` and chats in a thread, then messages persist and reload after restart.
- **BRD-137** — `IConversationStore` shall persist every message of a thread (user, assistant, sources as JSON, content parts) per workspace and user *(harvested 2026-09-24 from the TechieDesk BRD BRD-33, library part; TechieDesk checklist REQ-RAG-008 Verified)* *Screen:* Workspaces and memory
  - *Acceptance:* When a message with sources is stored through `IConversationStore`, then it reloads with role, content parts and sources intact.
- **BRD-138** — The system shall build the model context from persisted history with token-aware trimming that keeps the system message and the most recent turns *(harvested 2026-09-24 from the TechieDesk BRD BRD-37, library part; TechieDesk checklist REQ-RAG-009 Verified)* *Screen:* Workspaces and memory
  - *Acceptance:* When persisted history exceeds the token budget, then the context keeps the system message and the latest turns.
- **BRD-139** — The system shall provide workspace primitives (`IWorkspaceStore`, `WorkspaceManager`, `Workspace`): isolated documents and settings, document pinning, a per-workspace similarity threshold and top-K, a rerank toggle, and query mode versus chat mode with an honest "not in my documents" answer *(harvested 2026-09-24 from the TechieDesk BRD BRD-109/32/44/47/48, library part; TechieDesk checklist REQ-RAG-028/007/013/014/015 Verified)* *Screen:* Workspaces and memory
  - *Acceptance:* When a developer creates two workspaces with different documents and settings, then retrieval in one never returns the other's chunks.
- **BRD-140** — Ingestion shall deduplicate by content hash so a document embedded once is reused across workspaces (`IWorkspaceStore.FindDocumentIdByHashAsync`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-43, library part; TechieDesk checklist REQ-RAG-012 Verified)* *Screen:* Workspaces and memory
  - *Acceptance:* When the same file is added to two workspaces, then it is embedded once and both workspaces reference the same document id.
- **BRD-141** — Context assembly shall signal truncation (`WorkspaceContext.WasTruncated`, the `ContextTruncated` event) and evict retrieved chunks before pinned ones *(harvested 2026-09-24 from the TechieDesk BRD REQ-RAG-048, library part; TechieDesk checklist Verified)* *Screen:* Workspaces and memory
  - *Acceptance:* When retrieved context exceeds the budget with pinned chunks present, then retrieved chunks are evicted first and `WasTruncated` is true.
- **BRD-142** — Deleting a document shall remove its vectors and its membership from every workspace; `IWorkspaceStore.RemoveDocumentAsync` deletes only the membership row today (TR-RAG-004) *(harvested 2026-09-24 from the TechieDesk BRD BRD-45, library part; TechieDesk checklist REQ-FN-012 Needs re-verify 60%)* *Screen:* Workspaces and memory
  - *Acceptance:* When a developer deletes a document, then its vectors and every workspace membership are gone.
- **BRD-143** — `PromptTemplateEngine.FormatContext` shall signal truncation the same way `WorkspaceManager` does instead of cutting context silently (TR-RAG-009) *(harvested 2026-09-24 from the TechieDesk BRD BRD-108, library part; TechieDesk checklist open feedback)* *Screen:* Workspaces and memory
  - *Acceptance:* When `PromptTemplateEngine.FormatContext` drops chunks to fit, then the caller is told that truncation happened.

### Reranking

Reorders search candidates with a cross-encoder or an API reranker.

- **BRD-144** — The system shall provide an `IReranker` stage with a local ONNX cross-encoder (`OnnxCrossEncoderReranker`, bge-reranker-v2-m3, in `TechieRag.Embedded`) and API rerankers (`CohereReranker`, `JinaReranker`), configured by `WithReranker` *(harvested 2026-09-24 from the TechieDesk BRD BRD-106, library part; TechieDesk checklist REQ-RAG-025 Verified)* *Screen:* Reranking
  - *Acceptance:* When a developer enables the ONNX cross-encoder or Cohere reranker and searches, then the top results are reordered by reranker score.
- **BRD-145** — A developer can switch reranking per call through `SearchOptions.Rerank` and set the default with `WithRerankEnabledByDefault`; the workspace flag is authoritative when a workspace is active *(harvested 2026-09-24 from the TechieDesk BRD REQ-RAG-047, library part; TechieDesk checklist Verified)* *Screen:* Reranking
  - *Acceptance:* When a developer passes `SearchOptions.Rerank = false` on a rerank-by-default instance, then that search skips the reranker.

### Provider breadth

Widens the provider set: model-name routing, more embedders, images, prompt caching, speech.

- **BRD-146** — A developer can pick a hosted model by name alone: `ModelRouter` resolves the longest unambiguous prefix in `LlmConnectorCatalog` (OpenAI, Anthropic, Gemini, Groq, Mistral, Cohere, DeepSeek, xAI, OpenRouter, Together, Perplexity, Bedrock, Ollama, LM Studio) and `UseLlmForModel` / `UseConnectorLlm` build the provider *(harvested 2026-09-24 from the TechieDesk BRD BRD-115, library part; TechieDesk checklist REQ-RAG-034 Verified 95%)* *Screen:* Provider breadth
  - *Acceptance:* When a developer calls `UseLlmForModel("gpt-4o")`, then the router resolves the OpenAI connector and the provider is built without naming it.
- **BRD-147** — A developer can use Cohere, Voyage, Mistral and Google Gemini embedding providers (`UseCohereEmbedding`, `UseVoyageEmbedding`, `UseMistralEmbedding`, `UseGeminiEmbedding`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-116, library part; TechieDesk checklist REQ-RAG-035 Verified 95%, wire contract only)* *Screen:* Provider breadth
  - *Acceptance:* When a developer calls `UseCohereEmbedding`, `UseVoyageEmbedding`, `UseMistralEmbedding` or `UseGeminiEmbedding`, then embeddings reach that vendor's endpoint.
- **BRD-148** — `EmbeddedEmbeddingProvider` shall encode text as XLM-RoBERTa expects (SentencePiece ids shifted by the fairseq offset, wrapped in `<s>` … `</s>`), every stored vector shall carry an `EmbeddingSignature`, and `DetectStaleEmbeddingsAsync` shall report a corpus embedded by another model or an older revision *(TechieRag checklist REQ-RAG-052, migrated 2026-09-03 from TechieDesk BRD-116/BRD-8; Implemented 95%)* *Screen:* Provider breadth
  - *Acceptance:* When a Hindi query is embedded by `EmbeddedEmbeddingProvider`, then it ranks a relevant English passage above an irrelevant one and stale vectors are detected.
- **BRD-149** — A developer can send images with a chat message through `IMultimodalLlmProvider`, `ChatImage` and `ChatContentPart`, mapped to each vendor's wire shape; audio and document attachments are a later phase *(harvested 2026-09-24 from the TechieDesk BRD BRD-120, library part; TechieDesk checklist REQ-RAG-039 Verified 90%)* *Screen:* Provider breadth
  - *Acceptance:* When a developer sends a chat message with an image to a multimodal provider, then the image reaches the vendor in its wire shape.
- **BRD-150** — The system shall pass prompt-caching hints through to Anthropic and Gemini (`PromptCacheOptions`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-124, library part; TechieDesk checklist REQ-RAG-043 Verified)* *Screen:* Provider breadth
  - *Acceptance:* When a developer sets `PromptCacheOptions` on an Anthropic or Gemini call, then the caching hint is present in the request.
- **BRD-151** — Streaming RAG shall return source citations alongside the text (`RagStreamEvent`, `AskStreamWithSourcesAsync`, `ChatWithRagStreamWithSourcesAsync`) and honour `PromptTemplateEngine` *(harvested 2026-09-24 from the TechieDesk BRD BRD-105, library part; TechieDesk checklist REQ-RAG-024 Verified)* *Screen:* Provider breadth
  - *Acceptance:* When a developer iterates `AskStreamWithSourcesAsync`, then source events arrive with the text events.
- **BRD-152** — Cost shall come from a configurable pricing table (`WithModelPricing`, `ModelPricing`) and streamed-token usage shall be reported correctly on every provider *(harvested 2026-09-24 from the TechieDesk BRD BRD-110, library part; TechieDesk checklist REQ-RAG-029 Verified)* *Screen:* Provider breadth
  - *Acceptance:* When a developer streams a completion with a pricing table set, then usage and cost for the streamed tokens are recorded correctly.
- **BRD-153** — The system shall provide `ISpeechToText` and `ITextToSpeech` abstractions with OpenAI-compatible providers (`UseSpeechToText`) *(harvested 2026-09-24 from the TechieDesk BRD BRD-122, library part; TechieDesk checklist REQ-RAG-041 Verified 90%, no live endpoint)* *Screen:* Provider breadth
  - *Acceptance:* When a developer calls `UseSpeechToText` with an OpenAI-compatible endpoint and transcribes audio, then text returns.
- **BRD-154** — `TechieRag` and `TechieRag.Telemetry` shall target `net10.0` and `net8.0`; `TechieRag.Embedded` targets `net10.0` because ONNX Runtime requires it *(harvested 2026-09-24 from the TechieDesk BRD BRD-118, library part; TechieDesk checklist REQ-RAG-037 Verified)* *Screen:* Provider breadth
  - *Acceptance:* When a consumer on `net8.0` references `TechieRag` and `TechieRag.Telemetry`, then restore and build succeed.
- **BRD-155** — `TechieRag.Telemetry` shall be a separate opt-in package: OTLP or console exporters, tracing and metrics off by default, a loopback endpoint by default and a non-loopback endpoint refused unless `AllowRemoteEndpoint`; the core package links no exporter *(harvested 2026-09-24 from the TechieDesk BRD BRD-117/99, library part; TechieDesk checklist REQ-RAG-036, REQ-NFR-008 Verified)* *Screen:* Provider breadth
  - *Acceptance:* When a developer adds `TechieRag.Telemetry` with defaults on a host app, then nothing is exported; with `EnableTracing` and a loopback endpoint, spans reach it.

## 4. Non-functional requirements

Only what this phase adds. The ones that apply to the whole library are in the phase-1 BRD.

| Id | Area | Requirement | Measure |
|---|---|---|---|
| (none) | — | This phase adds no library-wide non-functional item; performance of the local model is recorded per device under BRD-108, never as a budget the owner has not stated | — |

## 5. Development status

Written by the status gate after every build, verify and handoff; not by hand.

**Snapshot as of 2026-09-24.** Live per-requirement status: `PROJECT-STATUS.md` and the Requirements Status table in `docs/TechieRag-P2-Checklist.md`.

| Screen | Requirements | Verified | Open | Status |
|---|---|---|---|---|
| Surface: Agents package | 3 | 0 | 3 | Planned |
| Surface: Packaging and quality follow-ups | 9 | 2 | 7 | Partial |
| Surface: Repository separation | 1 | 0 | 1 | In progress |
| Surface: Flow orchestration | 6 | 0 | 6 | In progress |
| Surface: Provider breadth | 10 | 9 | 1 | Partial |
| Surface: Platform groundwork | 8 | 0 | 8 | Planned |
| Surface: Local model | 14 | 0 | 14 | Planned |
| Surface: Typed streaming | 2 | 0 | 2 | Planned |
| Surface: Subscription sign-in | 3 | 0 | 3 | Planned |
| Surface: Ingestion breadth | 7 | 5 | 2 | Partial |
| Surface: Data connectors | 6 | 4 | 2 | Partial |
| Surface: MCP tools | 2 | 1 | 1 | Partial |
| Surface: Workspaces and memory | 8 | 6 | 2 | Partial |
| Surface: Reranking | 2 | 2 | 0 | Done |

## 6. Where the rest lives

| What | Where |
|---|---|
| Scope, users and roles, the context diagram | [phase 1 BRD](TechieRag-BRD.md) |
| Non-functional requirements for the whole library | [phase 1 BRD](TechieRag-BRD.md) |
| Constraints, assumptions and risks | [phase 1 BRD](TechieRag-BRD.md) |
| Every phase, its surfaces and its BRD range | [TechieRag-Phases.md](TechieRag-Phases.md) |
| This phase's work list | [TechieRag-P2-Checklist.md](TechieRag-P2-Checklist.md) |
| The decisions behind the 2026-09-24 items | [TechieRag-Update-Brief.md](TechieRag-Update-Brief.md), `DECISIONS.md`, Architecture §6 |
