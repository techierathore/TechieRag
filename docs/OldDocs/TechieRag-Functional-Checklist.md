# TechieRag — Functional Checklist

> Migrated from docs/trrag-refactoring-roadmap.md + docs/techierag-v2-llm-implementation-spec.md on 2026-06-25. Phase structure, completion %, and status remarks carried over verbatim — verify before building.

## Table of Contents

1. [Goal](#goal)
2. [Requirements Status](#requirements-status)
3. [Functional requirements](#functional-requirements)
4. [RAG / AI requirements (→ /techierag)](#rag-ai-requirements-techierag)
5. [Non-functional](#non-functional)

## Goal

Deliver a configurable .NET RAG library (BRD §1) whose ingestion, embedding, vector storage, retrieval, and LLM-generation capabilities are all pluggable by configuration. This checklist tracks the library/backend (`REQ-FN-*`), the RAG/AI domain (`REQ-RAG-*`), and cross-cutting non-functionals (`REQ-NFR-*`). All items were delivered across v1.1 (roadmap, completed 2025-12-30) and v2 (LLM spec, completed 2026-02-18) and are migrated as **Done (pre-existing)**.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
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

**Status values:** `Not Started` · `In Progress` · `Implemented` · `Verified` · `Done (pre-existing)` (migrated as already complete — build agents must NOT rebuild; terminal like `Verified`) · `PARTIAL` · `FAIL` · `Blocked` · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug or library reference.

> **Deferred (not yet built, NOT a migrated BRD):** formal automated xUnit test suite (v2 Phase 7 — validated manually only) and optional OpenTelemetry exporters. Tracked in PROJECT-STATUS "Deferred / future"; no `REQ-*` assigned because they are not BRD requirements.

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
