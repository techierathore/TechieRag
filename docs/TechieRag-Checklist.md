# TechieRag — Checklist

> Migrated from docs/trrag-refactoring-roadmap.md + docs/techierag-v2-llm-implementation-spec.md on 2026-06-25. Phase structure, completion %, and status remarks carried over verbatim — verify before building. All UI lives in the `samples/TechieRagWeb` Blazor Server sample.
>
> Merged on 2026-06-26 from the former docs/TechieRag-UI-Checklist.md (REQ-UI-*) and docs/TechieRag-Functional-Checklist.md (REQ-FN/RAG/NFR-*) into this single checklist. Rows, statuses, %, remarks, and detail anchors carried over verbatim.

## Table of Contents

1. [Goal](#goal)
2. [Requirements Status](#requirements-status)
3. [UI / Pages](#ui--pages)
4. [Functional requirements](#functional-requirements)
5. [RAG / AI requirements (→ /techierag)](#rag-ai-requirements-techierag)
6. [Non-functional](#non-functional)

## Goal

Deliver a configurable .NET RAG library (BRD §1) whose ingestion, embedding, vector storage, retrieval, and LLM-generation capabilities are all pluggable by configuration. This single checklist is the whole app's work list: it tracks the UI (`REQ-UI-*`, the `TechieRagWeb` Blazor Server sample + Qdrant administration UI), the library/backend (`REQ-FN-*`), the RAG/AI domain (`REQ-RAG-*`), and cross-cutting non-functionals (`REQ-NFR-*`). All items were delivered across v1.1 (roadmap, completed 2025-12-30) and v2 (LLM spec, completed 2026-02-18) and are migrated as **Done (pre-existing)**.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-UI-001 | Home landing + navigation | Done (pre-existing) | 100% | v1.1; per trrag-refactoring-roadmap.md Phase 5 | [view](#d-req-ui-001) |
| REQ-UI-002 | Settings page (embedding + vector store) | Done (pre-existing) | 100% | v1.1; roadmap Phase 5.2; v2 TrBlazeUI rewrite (spec Phase 6). ⚠ DevGuide 2026-06-25: "Reset to Defaults" never calls `RagManager.ReconfigureAsync` (Settings.razor:326-331) so the live instance keeps old config until next Save; `EnableTelemetry` is persisted but never read by `TechieRagManager` (no usage at TechieRagManager.cs:94-253) — toggle is a no-op (static — confirm at runtime). Core save/init path unaffected | [view](#d-req-ui-002) |
| REQ-UI-003 | LLM Settings page (provider/fallback/usage/resilience/prompts) | Needs re-verify | 90% | v2; spec Phase 6 (#20), completed 2026-02-18. ⚠ DevGuide 2026-06-25: "Reset" (`ResetToDefaultsAsync`, LlmSettings.razor:415-425) is in-memory only — never calls SaveConfigAsync or ReconfigureAsync and shows a success toast anyway, so the reset is silently lost (static — confirm at runtime) | [view](#d-req-ui-003) |
| REQ-UI-004 | Ingestion + Text Ingestion pages | Done (pre-existing) | 100% | v1.1; roadmap Phase 5.3; v2 TrBlazeUI rewrite | [view](#d-req-ui-004) |
| REQ-UI-005 | Chat page (RAG chat, streaming, sources, top-K, filter) | Needs re-verify | 85% | v2; spec Phase 6 (#21), completed 2026-02-18. ⚠ DevGuide 2026-06-25: with Streaming ON (the default) neither `HandleDirectLlm` nor `HandleAutoRag` streaming branch accumulates `totalTokens`/`totalCost` (Chat.razor:259-288) — footer reads "0 tokens / $0.0000"; streamed Auto-RAG also runs the vector search twice per question (Chat.razor:280 + TechieRagClient.cs:454) (static — confirm at runtime) | [view](#d-req-ui-005) |
| REQ-UI-006 | LLM Playground page (completion/structured/chat) | Needs re-verify | 80% | v2; spec Phase 6 (#23, NEW). ⚠ DevGuide 2026-06-25: Temperature/Max Tokens inputs are never parsed into `LlmCompletionOptions` for any call (LlmPlayground.razor:47,53 vs 221/232/268) — no-op controls; "Structured Output" tab renders the raw model JSON string and never deserializes into the labelled types (:269,134) (static — confirm at runtime) | [view](#d-req-ui-006) |
| REQ-UI-007 | Tool Demo page (agent loop, execution trace) | Needs re-verify | 75% | v2; spec Phase 6 (#24, NEW). ⚠ DevGuide 2026-06-25: the Execution Trace always shows a single hardcoded step regardless of real tool calls — `AgentLoopRunner.RunAsync` (AgentLoopRunner.cs:61-126) exposes no per-step callback, so `executionSteps` only ever gets `new ExecutionStep(1, ...)` (ToolDemo.razor:275-278). Root cause is a library API gap (static — confirm at runtime). ✅ FIXED 2026-06-25: `AgentLoopRunner.RunAsync` now reports an `IProgress<AgentStep>` (new `Models/AgentStep.cs`); ToolDemo passes a `Progress<AgentStep>` that appends each tool request/execution + final answer to the live trace (`ToExecutionStep`). Core lib builds clean (0 errors); kept `Needs re-verify` until the sample is booted to confirm the trace renders. ⚠ DevGuide 2026-06-30 (`--update`): re-mapped ToolDemo lineage to as-built anchors (`RunAsync` now AgentLoopRunner.cs:66; `progress.Report` at :99/:108/:132/:156; page injects `ITechieRag` directly, not `TechieRagManager`); core lib re-built clean (0 errors); still `Needs re-verify` — sample boot remains PAT-gated (TrBlazeUI 401) | [view](#d-req-ui-007) |
| REQ-UI-008 | Token Usage dashboard page | Done (pre-existing) | 100% | v2; spec Phase 6 (#22, NEW). ⚠ DevGuide 2026-06-25: Estimated Cost reads $0.0000 for any model absent from the hard-coded pricing table even though tokens are counted (TokenUsageTracker.cs:207-217) — silent under-reporting for unlisted models (static — confirm at runtime) | [view](#d-req-ui-008) |
| REQ-UI-009 | Test LLM connection UI | Done (pre-existing) | 100% | v2; spec Phase 6. ⚠ DevGuide 2026-06-25: Test uses the last-built instance via `RagManager.GetLlmProvider()` (TechieRagManager.cs:365) — unsaved form edits are not tested until Save runs ReconfigureAsync; also blocks sync-over-async (cs:367) (static — confirm at runtime) | [view](#d-req-ui-009) |
| REQ-UI-010 | All pages render via TrBlazeUI components + Lucide icons | Done (pre-existing) | 100% | v2; spec Phase 6 (#19 — migrate ALL pages) | [view](#d-req-ui-010) |
| REQ-UI-011 | Qdrant Admin: Docker container lifecycle UI | Done (pre-existing) | 100% | v1.1; roadmap Phase 7.1/7.3 | [view](#d-req-ui-011) |
| REQ-UI-012 | Qdrant Admin: collection CRUD | Needs re-verify | 90% | v1.1; roadmap Phase 7.4. ⚠ DevGuide 2026-06-25: cluster Version is hard-coded `"1.12.x"` (QdrantAdminService.cs:318) not read from the server; the Collections grid "Vectors" column duplicates "Points" — both bound to the same `PointsCount` (cs:344-345) (static — confirm at runtime) | [view](#d-req-ui-012) |
| REQ-UI-013 | Qdrant Admin: vector browse / search / detail / bulk delete | Needs re-verify | 85% | v1.1; roadmap Phase 7.5. ⚠ DevGuide 2026-06-25: vector pagination encodes the offset as a numeric `PointId.Num` cursor (QdrantAdminService.cs:437) instead of Qdrant's `next_page_offset`, so Next/Previous beyond page 1 return wrong/empty results for UUID or non-contiguous IDs (static — confirm at runtime) | [view](#d-req-ui-013) |
| REQ-FN-001 | Configuration & builder (fluent / appsettings / DI / config object) | Done (pre-existing) | 100% | v1.1; roadmap Phase 2 (completed 2025-12-30) | [view](#d-req-fn-001) |
| REQ-FN-002 | AI-agent autodistribution (MSBuild skill deploy) | Done (pre-existing) | 100% | v1.1; per ai-agent-autodistribution-guide.md | [view](#d-req-fn-002) |
| REQ-FN-003 | NuGet packaging & publishing (GitHub Actions) | Done (pre-existing) | 100% | v1.1; per NUGET-PUBLISHING-GUIDE.md + publish-nuget.yml | [view](#d-req-fn-003) |
| REQ-RAG-001 | Document ingestion & processing (9 formats + chunking) | Done (pre-existing) | 100% | v1.1; roadmap Phase 4 (completed 2025-12-30) | [view](#d-req-rag-001) |
| REQ-RAG-002 | Embedding providers (6 + custom) | Done (pre-existing) | 100% | v1.1; roadmap Phase 4.3 | [view](#d-req-rag-002) |
| REQ-RAG-003 | Vector stores (SQLite-vec / pgvector / Qdrant) | Done (pre-existing) | 100% | v1.1; roadmap Phase 3 | [view](#d-req-rag-003) |
| REQ-RAG-004 | Semantic search & retrieval (topK, filter, scoring) | Done (pre-existing) | 100% | v1.1; roadmap Phase 3/5 | [view](#d-req-rag-004) |
| REQ-RAG-005 | Offline embedded embedding (BGE-M3 ONNX) | Done (pre-existing) | 100% | v1.1; per TechieRag.Embedded-UserGuide.md | [view](#d-req-rag-005) |
| REQ-RAG-006 | LLM provider integration (6 providers) | Done (pre-existing) | 100% | v2; spec Phase 3 (completed 2026-02-18) | [view](#d-req-rag-006) |
| REQ-RAG-007 | Auto-RAG generation (Ask / AskStream / ChatWithRag) | Done (pre-existing) | 100% | v2; spec Phase 4 | [view](#d-req-rag-007) |
| REQ-RAG-008 | Structured / typed output (CompleteAsync&lt;T&gt;) | Done (pre-existing) | 100% | v2; spec Phase 1/3 | [view](#d-req-rag-008) |
| REQ-RAG-009 | Tool calling & agent loop | Done (pre-existing) | 100% | v2; spec Phase 5. ⚠ DevGuide 2026-06-25: `AgentLoopRunner.RunAsync` (AgentLoopRunner.cs:61-126) returns only the final `LlmResponse` and exposes no per-iteration/per-tool callback or step log, so consumers cannot observe the loop's intermediate steps — this is why the sample's Tool Demo execution trace is unwired (see REQ-UI-007). Core loop (tool declaration/registration + max-iteration guard) works as specified; this is an observability/API-surface gap, not a loop defect (static — confirm at runtime). ✅ RESOLVED 2026-06-25: added an optional `IProgress<AgentStep>` parameter to `AgentLoopRunner.RunAsync` (new `Models/AgentStep.cs` + `AgentStepKind`) that reports each tool-call request, each tool execution (name/args/result/success), and the final answer; core library builds clean (0 errors) | [view](#d-req-rag-009) |
| REQ-RAG-010 | Conversation memory (token-budget trimming) | Done (pre-existing) | 100% | v2; spec Phase 2 | [view](#d-req-rag-010) |
| REQ-RAG-011 | Token tracking & budgets | Done (pre-existing) | 100% | v2; spec Phase 2 | [view](#d-req-rag-011) |
| REQ-RAG-012 | Resilience & retry (backoff / 429 / circuit breaker) | Done (pre-existing) | 100% | v2; spec Phase 2 | [view](#d-req-rag-012) |
| REQ-RAG-013 | Fallback LLM provider | Done (pre-existing) | 100% | v2; spec Phase 2 | [view](#d-req-rag-013) |
| REQ-RAG-014 | Prompt templates (default + custom) | Done (pre-existing) | 100% | v2; spec Phase 2 | [view](#d-req-rag-014) |
| REQ-NFR-001 | Performance targets (token est, streaming, batch) | Done (pre-existing) | 100% | v2; per spec §NFR | [view](#d-req-nfr-001) |
| REQ-NFR-002 | Reliability (retry + circuit breaker + fallback) | Done (pre-existing) | 100% | v2; per spec §NFR | [view](#d-req-nfr-002) |
| REQ-NFR-003 | Scalability (budget/model/history scaling) | Done (pre-existing) | 100% | v2; per spec §NFR | [view](#d-req-nfr-003) |
| REQ-NFR-004 | Security (key handling, HTTPS, tool validation, budget block) | Done (pre-existing) | 100% | v2; per spec §NFR | [view](#d-req-nfr-004) |
| REQ-NFR-005 | Observability (completion events, logging) | Done (pre-existing) | 100% | v2; per spec §NFR | [view](#d-req-nfr-005) |
| REQ-NFR-006 | Portability / accessibility (formats, languages, uniform API) | Done (pre-existing) | 100% | v1.1+v2 | [view](#d-req-nfr-006) |
| REQ-NFR-007 | Backward compatibility (v1 → v2 additive) | Done (pre-existing) | 100% | v2; spec Phase 1–6 backward-compat mandate | [view](#d-req-nfr-007) |

**Status values:** `Not Started` · `In Progress` · `Implemented` (code done, not yet verified) · `Verified` (self-smoke or verifier PASS — acceptance AND data-render AND visual gates all pass) · `Done (pre-existing)` (migrated from an earlier dev plan as already complete — build agents must NOT rebuild; terminal like `Verified`) · `Needs re-verify` (a defect or change was logged — must be re-run before it can return to `Verified`) · `PARTIAL` (some acceptance unmet — say what in Remarks) · `FAIL` (verifier ran and failed — bug in Remarks) · `Blocked` (external/library gap — link the TR-/TR-RAG- entry in Remarks) · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug or library reference. This is the home for bugs and change notes — do not spawn a separate file. Visual-gate failures are prefixed `⚠ visual:`; security findings `⚠ SECURITY`.

> **Note:** the UI REQs are migrated as `Done (pre-existing)`. Restoring/running the sample for live UI verification requires the `TrBlazeUI.*` GitHub Packages credential (see PROJECT-STATUS Known blockers).

> **Deferred (not yet built, NOT a migrated BRD):** formal automated xUnit test suite (v2 Phase 7 — validated manually only) and optional OpenTelemetry exporters. Tracked in PROJECT-STATUS "Deferred / future"; no `REQ-*` assigned because they are not BRD requirements.

## UI / Pages

<!-- Each REQ carries an explicit `<a id="d-REQ-ID">` anchor (lowercase) so the
     Details column above links straight to it in both Markdown and rendered HTML.
     UI scope is the `TechieRagWeb` sample — a Blazor Server demo (TrBlazeUI components +
     Lucide icons) that exercises every TechieRag capability across ten routed pages,
     plus the Qdrant administration UI. Traces to BRD §9 F-WEB and F-QDRANT (BRD-62…BRD-73). -->

### Page: Home (`/`)

<a id="d-req-ui-001"></a>
- **REQ-UI-001** — Landing page with navigation to all feature pages (BRD-62…BRD-68 entry points).
  - *Acceptance:* page renders; nav links resolve to every feature route.

### Page: Settings (`/settings`)

<a id="d-req-ui-002"></a>
- **REQ-UI-002** — Configure embedding source/endpoint/model and vector store type/connection; Save + Initialize (BRD-62).
  - *Acceptance:* settings persist to `techierag-config.json`; Save shows a success toast; Initialize creates the client.

### Page: LLM Settings (`/llm-settings`)

<a id="d-req-ui-003"></a>
- **REQ-UI-003** — Tabs for Primary provider, Fallback, Usage/budget, Resilience, and Prompts configuration (BRD-63).
  - *Acceptance:* each tab saves its config section; values reload correctly.

### Page: Ingestion (`/ingestion`) + Text Ingestion (`/text-ingestion`)

<a id="d-req-ui-004"></a>
- **REQ-UI-004** — Upload/manage documents and ingest raw text; show document list + stats (BRD-64).
  - *Acceptance:* file ingest returns a doc id and updates the document/chunk counts; raw-text ingest works with metadata.

### Page: Chat (`/chat`)

<a id="d-req-ui-005"></a>
- **REQ-UI-005** — RAG chat with mode selector, document filter, top-K, streaming toggle, and a sources panel (BRD-65).
  - *Acceptance:* answers stream token-by-token; sources show relevance scores; filter scopes results.

### Page: LLM Playground (`/llm-playground`)

<a id="d-req-ui-006"></a>
- **REQ-UI-006** — Direct LLM testing: Completion, Structured Output, and Chat tabs (BRD-66).
  - *Acceptance:* completion returns text + token counts; structured output parses to a typed object.

### Page: Tool Demo (`/tool-demo`)

<a id="d-req-ui-007"></a>
- **REQ-UI-007** — Agent loop demonstration with built-in and custom tools and an execution trace (BRD-67).
  - *Acceptance:* the LLM calls tools (e.g. get_weather, calculate_math); the trace shows each step.

### Page: Token Usage (`/token-usage`)

<a id="d-req-ui-008"></a>
- **REQ-UI-008** — Usage dashboard: tracking, budget status, per-model breakdown, recent operations (BRD-68).
  - *Acceptance:* counts/costs update across a session; budget alert shows at threshold.

### Cross-page: connection test + UI framework

<a id="d-req-ui-009"></a>
- **REQ-UI-009** — Test LLM connection before running queries (BRD-69).
  - *Acceptance:* test succeeds for a reachable provider; shows a clear error on failure.

<a id="d-req-ui-010"></a>
- **REQ-UI-010** — All form inputs and layout use TrBlazeUI components (Input, Select, Field, Card, DataTable, Tabs, Dialog, Toast) and Lucide icons; styling via Tailwind class parameter, not inline styles (BRD-70).
  - *Acceptance:* no raw HTML form controls remain; navigation uses Lucide icons.

### Page: Qdrant Admin (`/qdrant-admin`)

<a id="d-req-ui-011"></a>
- **REQ-UI-011** — Docker status detection and container create/start/stop/remove + logs (BRD-71).
  - *Acceptance:* container lifecycle actions reflect real Docker state; status indicator updates.

<a id="d-req-ui-012"></a>
- **REQ-UI-012** — Collection create / list / inspect / delete with cluster info (BRD-72).
  - *Acceptance:* a created collection appears in the table; delete removes it.

<a id="d-req-ui-013"></a>
- **REQ-UI-013** — Paginated vector browse + search, vector detail (payload, chunk, source), bulk delete (BRD-73).
  - *Acceptance:* vectors page through; detail modal shows payload and source; bulk delete removes selected vectors.

## Functional requirements

<a id="d-req-fn-001"></a>
- **REQ-FN-001** — Configure TechieRag via fluent builder, `appsettings.json` binding, the `AddTechieRag(...)` DI extension, or a hand-built `TechieRagConfig`; any provider swappable by config alone (BRD-19, BRD-20, BRD-21, BRD-22, BRD-23).

<a id="d-req-fn-002"></a>
- **REQ-FN-002** — `TechieRag.targets` auto-deploys AI skill files (`.techierag/`, `.claude/commands/`, `.opencode/command/`) into a consumer repo on build, refreshed on each package update (BRD-57, BRD-58).

<a id="d-req-fn-003"></a>
- **REQ-FN-003** — GitHub Actions builds, tests, packs, and publishes both packages to GitHub Packages (auto) and NuGet.org (gated on secret); semantic versioning overridden from tag/run number (BRD-59, BRD-60, BRD-61).

## RAG / AI requirements (→ /techierag)

<a id="d-req-rag-001"></a>
- **REQ-RAG-001** — Ingest from file / directory / raw text; extract text from 9 formats; chunk with configurable size+overlap; manage document lifecycle (BRD-1…BRD-7).

<a id="d-req-rag-002"></a>
- **REQ-RAG-002** — Generate single + batch embeddings behind `IEmbeddingProvider` across Ollama, LM Studio, ONNX, Azure OpenAI, HTTP, and custom (BRD-8…BRD-11).

<a id="d-req-rag-003"></a>
- **REQ-RAG-003** — Store and similarity-search embeddings via SQLite-vec, pgvector, or Qdrant with full CRUD, batch upsert, filtered search, and stats (BRD-12…BRD-15).

<a id="d-req-rag-004"></a>
- **REQ-RAG-004** — Semantic search via `SearchAsync(query, topK, documentFilter?)` returning ranked results with 0–1 scores and chunk metadata (BRD-16…BRD-18).

<a id="d-req-rag-005"></a>
- **REQ-RAG-005** — Fully offline embedding via `.UseEmbedded()` (BGE-M3 ONNX), one-time model download to a platform cache, with progress events (BRD-24…BRD-26).

<a id="d-req-rag-006"></a>
- **REQ-RAG-006** — Unified `ILlmProvider` for completion/chat/streaming/tool-calling across Ollama, LM Studio, OpenAI-compatible, Azure AI Foundry, Gemini, Anthropic, and custom (BRD-27…BRD-33).

<a id="d-req-rag-007"></a>
- **REQ-RAG-007** — Auto-RAG via `AskAsync`, `AskStreamAsync`, `ChatWithRagAsync`, `ChatWithRagStreamAsync`; identical to v1 when no LLM configured (BRD-34…BRD-38).

<a id="d-req-rag-008"></a>
- **REQ-RAG-008** — Typed JSON output deserialized to `T` via `CompleteAsync<T>` (BRD-39).

<a id="d-req-rag-009"></a>
- **REQ-RAG-009** — Tool declaration (`ToolDefinition`), delegate/`IToolHandler` registration, and an agent loop with a max-iteration guard (BRD-40…BRD-43).

<a id="d-req-rag-010"></a>
- **REQ-RAG-010** — Optional conversation memory with token-budget trimming and custom implementations (BRD-44…BRD-46).

<a id="d-req-rag-011"></a>
- **REQ-RAG-011** — Token usage + cost tracking per operation/session/model with budgets, alerts, and optional blocking (BRD-47…BRD-50).

<a id="d-req-rag-012"></a>
- **REQ-RAG-012** — Automatic retry with backoff, HTTP-429/`Retry-After` handling, and a circuit breaker on all LLM calls (BRD-51…BRD-53).

<a id="d-req-rag-013"></a>
- **REQ-RAG-013** — Fallback LLM that takes over automatically when the primary fails (BRD-54).

<a id="d-req-rag-014"></a>
- **REQ-RAG-014** — Customizable RAG prompt (system prompt, context template, limits) and full replacement via custom `IPromptTemplate` (BRD-55, BRD-56).

## Non-functional

<a id="d-req-nfr-001"></a>
- **REQ-NFR-001** — Performance: immediate token estimation; real-time streaming; batch embedding; backoff cap 30s; agent cap 10; context trimmed to 4,000 tokens (BRD-74).

<a id="d-req-nfr-002"></a>
- **REQ-NFR-002** — Reliability: retry + circuit breaker absorb transient failures; optional fallback preserves continuity (BRD-75).

<a id="d-req-nfr-003"></a>
- **REQ-NFR-003** — Scalability: budgets 0→`long.MaxValue`; arbitrary model counts; unbounded history with windowing (BRD-76).

<a id="d-req-nfr-004"></a>
- **REQ-NFR-004** — Security: keys as config (consumer secrets manager), HTTPS, tool-name validation, null-checks, budget blocking (BRD-77).

<a id="d-req-nfr-005"></a>
- **REQ-NFR-005** — Observability: per-completion telemetry events (model, duration, tokens); `ILoggerFactory` integration with `NullLogger` fallback (BRD-78).

<a id="d-req-nfr-006"></a>
- **REQ-NFR-006** — Portability/accessibility: 9 formats, 100+ languages (BGE-M3), uniform `ILlmProvider` API, builder + config paths (BRD-79).

<a id="d-req-nfr-007"></a>
- **REQ-NFR-007** — Backward compatibility: all v2 additions additive; v1 methods/config unchanged; `TechieRag.Embedded` unchanged (BRD-80).
</content>
</invoke>
