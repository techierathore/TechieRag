# TechieRag — Checklist

| | |
|---|---|
| App | TechieRag |
| Size | Medium |
| Phase | 1 of 2 |

## Goal

Deliver the configurable .NET RAG library described in `docs/TechieRag-BRD.md` §1: ingestion, embedding, vector storage, retrieval and generation, all pluggable by configuration. Phase 1 is the shipped core; every row was carried on 2026-09-24 from the June checklist with its id and status preserved (the fourteen application rows were re-classed from REQ-UI to REQ-FN and marked N/A because their screens moved to Sevak). Phase 2 is `docs/TechieRag-P2-Checklist.md`.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-FN-001 | Configuration & builder (fluent / appsettings / DI / config object) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-001) |
| REQ-FN-002 | AI-agent autodistribution (MSBuild skill deploy) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-002) |
| REQ-FN-003 | NuGet packaging & publishing (GitHub Actions) | Done (pre-existing) | 100% | Verified 2026-09-03 (owner dispatch of publish-nuget.yml still the owner's; version rules 9/9 incl. live nuget.org). Migrated 2026-09-24, status preserved. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-003) |
| REQ-NFR-001 | Performance targets (token est, streaming, batch) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-001) |
| REQ-NFR-002 | Reliability (retry + circuit breaker + fallback) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-002) |
| REQ-NFR-003 | Scalability (budget/model/history scaling) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-003) |
| REQ-NFR-004 | Security (key handling, HTTPS, tool validation, budget block) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-004) |
| REQ-NFR-005 | Observability (completion events, logging) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-005) |
| REQ-NFR-006 | Portability / accessibility (formats, languages, uniform API) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-006) |
| REQ-NFR-007 | Backward compatibility (v1 → v2 additive) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-nfr-007) |
| REQ-RAG-001 | Document ingestion & processing (9 formats + chunking) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-001) |
| REQ-RAG-002 | Embedding providers (6 + custom) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-002) |
| REQ-RAG-003 | Vector stores (SQLite-vec / pgvector / Qdrant) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-003) |
| REQ-RAG-004 | Semantic search & retrieval (topK, filter, scoring) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-004) |
| REQ-RAG-005 | Offline embedded embedding (BGE-M3 ONNX) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-005) |
| REQ-RAG-006 | LLM provider integration (6 providers) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-006) |
| REQ-RAG-007 | Auto-RAG generation (Ask / AskStream / ChatWithRag) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-007) |
| REQ-RAG-008 | Structured / typed output (CompleteAsync&lt;T&gt;) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-008) |
| REQ-RAG-009 | Tool calling & agent loop | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-009) |
| REQ-RAG-010 | Conversation memory (token-budget trimming) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-010) |
| REQ-RAG-011 | Token tracking & budgets | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-011) |
| REQ-RAG-012 | Resilience & retry (backoff / 429 / circuit breaker) | Done (pre-existing) | 100% | Verified 2026-07-02: Retry-After parsed via LlmHttpGuard in all six providers, 4 unit tests. Migrated 2026-09-24, status preserved. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-012) |
| REQ-RAG-013 | Fallback LLM provider | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-013) |
| REQ-RAG-014 | Prompt templates (default + custom) | Done (pre-existing) | 100% | Migrated 2026-09-24 by day-1 brownfield from the June checklist; was Done (pre-existing) 100% (library tests PASS 2026-09-24 via the build ladder). History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-rag-014) |
| REQ-FN-007 | Sevak (formerly TechieDesk): Home landing + navigation; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-001, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-007) |
| REQ-FN-008 | Sevak (formerly TechieDesk): Settings page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-002, Done (pre-existing) 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-008) |
| REQ-FN-009 | Sevak (formerly TechieDesk): LLM Settings page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-003, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-009) |
| REQ-FN-010 | Sevak (formerly TechieDesk): Ingestion + Text Ingestion pages; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-004, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-010) |
| REQ-FN-011 | Sevak (formerly TechieDesk): Chat page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-005, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-011) |
| REQ-FN-012 | Sevak (formerly TechieDesk): LLM Playground page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-006, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-012) |
| REQ-FN-013 | Sevak (formerly TechieDesk): Tool Demo page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-007, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-013) |
| REQ-FN-014 | Sevak (formerly TechieDesk): Token Usage dashboard page; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-008, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-014) |
| REQ-FN-015 | Sevak (formerly TechieDesk): Test LLM connection UI; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-009, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-015) |
| REQ-FN-016 | Sevak (formerly TechieDesk): All pages render via TrBlazeUI components + Lucide icons; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-010, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-016) |
| REQ-FN-017 | Sevak (formerly TechieDesk): Qdrant Admin: Docker container lifecycle UI; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-011, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-017) |
| REQ-FN-018 | Sevak (formerly TechieDesk): Qdrant Admin: collection CRUD; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-012, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-018) |
| REQ-FN-019 | Sevak (formerly TechieDesk): Qdrant Admin: vector browse / search / detail / bulk delete; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-013, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-019) |
| REQ-FN-020 | Sevak (formerly TechieDesk): Rename app TechieRagWeb → TechieDesk; screen moved to the Sevak repository 2026-09-24 | N/A | 100% | Was REQ-UI-014, Verified 100% on 2026-09-24. The screen belongs to Sevak since the split (BRD-87); kept here as history of the moved application. History: `docs/OldDocs/TechieRag-Checklist.md`. | [view](#d-req-fn-020) |

**Status values:** `Not Started` · `In Progress` · `Implemented` · `Verified` · `Done (pre-existing)` · `Needs re-verify` · `PARTIAL` · `FAIL` · `Blocked` · `Owner-UAT` · `N/A`. `Owner-UAT` marks a row only the owner can close, by following the UsageGuide test plan; the verifier cannot reach it from this machine.

## Surface: Configuration

<a id="d-req-fn-001"></a>
- **REQ-FN-001** — Configuration & builder (fluent / appsettings / DI / config object). *BRD:* BRD-19, BRD-20, BRD-21, BRD-22, BRD-23. *Phase 1.*
  - *Acceptance:* When a developer configures the library through `TechieRagBuilder` on the configuration API and calls `Build()`, then an `ITechieRag` with those providers returns.

## Surface: Autodistribution

<a id="d-req-fn-002"></a>
- **REQ-FN-002** — AI-agent autodistribution (MSBuild skill deploy). *BRD:* BRD-57, BRD-58. *Phase 1.*
  - *Acceptance:* When a consumer builds a project referencing `TechieRag` on their machine, then `.techierag/TechieRag-AI-Reference.md` and the two command files appear in the repository root.

## Surface: Packaging

<a id="d-req-fn-003"></a>
- **REQ-FN-003** — NuGet packaging & publishing (GitHub Actions). *BRD:* BRD-59, BRD-60, BRD-61. *Phase 1.*
  - *Acceptance:* When a maintainer pushes to `main` or dispatches the release workflow on GitHub Actions, then the packages are built, tested and packed by GitHub Actions.

## Non-functional

<a id="d-req-nfr-001"></a>
- **REQ-NFR-001** — Performance targets (token est, streaming, batch). *BRD:* BRD-74. *Phase 1.*
  - *Acceptance:* When token estimation, streaming and batch embedding are measured, then estimation is immediate, streaming unbuffered and batches accepted.

<a id="d-req-nfr-002"></a>
- **REQ-NFR-002** — Reliability (retry + circuit breaker + fallback). *BRD:* BRD-75. *Phase 1.*
  - *Acceptance:* When transient failures are injected, then retry, circuit breaker and fallback keep the call succeeding within their limits.

<a id="d-req-nfr-003"></a>
- **REQ-NFR-003** — Scalability (budget/model/history scaling). *BRD:* BRD-76. *Phase 1.*
  - *Acceptance:* When budgets, model counts and history grow, then tracking and trimming keep working without a fixed ceiling.

<a id="d-req-nfr-004"></a>
- **REQ-NFR-004** — Security (key handling, HTTPS, tool validation, budget block). *BRD:* BRD-77. *Phase 1.*
  - *Acceptance:* When keys, endpoints, tool names and inputs are reviewed, then keys stay configuration, HTTPS is supported, tool names validated and inputs null-checked.

<a id="d-req-nfr-005"></a>
- **REQ-NFR-005** — Observability (completion events, logging). *BRD:* BRD-78. *Phase 1.*
  - *Acceptance:* When a completion finishes, then an event with model, duration and token counts fires and logs flow through the host's `ILoggerFactory`.

<a id="d-req-nfr-006"></a>
- **REQ-NFR-006** — Portability / accessibility (formats, languages, uniform API). *BRD:* BRD-79. *Phase 1.*
  - *Acceptance:* When the format, language and provider breadth are checked, then nine formats, 100+ languages and one `ILlmProvider` API across six backends hold.

<a id="d-req-nfr-007"></a>
- **REQ-NFR-007** — Backward compatibility (v1 → v2 additive). *BRD:* BRD-80. *Phase 1.*
  - *Acceptance:* When a v1 consumer upgrades to v2, then existing methods and configuration keep working unchanged.

## Surface: Ingestion

<a id="d-req-rag-001"></a>
- **REQ-RAG-001** — Document ingestion & processing (9 formats + chunking). *BRD:* BRD-1, BRD-2, BRD-3, BRD-4, BRD-5, BRD-6, BRD-7. *Phase 1.*
  - *Acceptance:* When a developer calls `IngestAsync` with a PDF path on the ingestion API, then the file is processed by the PDF processor and a document id returns.

## Surface: Embedding providers

<a id="d-req-rag-002"></a>
- **REQ-RAG-002** — Embedding providers (6 + custom). *BRD:* BRD-8, BRD-9, BRD-10, BRD-11. *Phase 1.*
  - *Acceptance:* When a developer calls `EmbedAsync` and `EmbedBatchAsync` on any provider, then vectors of the provider's dimension return for one text and for a batch.

## Surface: Vector stores

<a id="d-req-rag-003"></a>
- **REQ-RAG-003** — Vector stores (SQLite-vec / pgvector / Qdrant). *BRD:* BRD-12, BRD-13, BRD-14, BRD-15. *Phase 1.*
  - *Acceptance:* When a developer upserts, batch-upserts, searches with a document filter, deletes and reads stats on each store, then every operation behaves the same.

## Surface: Semantic search

<a id="d-req-rag-004"></a>
- **REQ-RAG-004** — Semantic search & retrieval (topK, filter, scoring). *BRD:* BRD-16, BRD-17, BRD-18. *Phase 1.*
  - *Acceptance:* When a developer calls `SearchAsync` with a query and top-K on the search API, then that many results return in descending score order.

## Surface: Embedded package

<a id="d-req-rag-005"></a>
- **REQ-RAG-005** — Offline embedded embedding (BGE-M3 ONNX). *BRD:* BRD-24, BRD-25, BRD-26. *Phase 1.*
  - *Acceptance:* When a developer calls `UseEmbedded()` on the Embedded package and embeds text, then a 1024-dimension vector returns with no network after the first run.

## Surface: LLM providers

<a id="d-req-rag-006"></a>
- **REQ-RAG-006** — LLM provider integration (6 providers). *BRD:* BRD-27, BRD-28, BRD-29, BRD-30, BRD-31, BRD-32, BRD-33. *Phase 1.*
  - *Acceptance:* When a developer calls complete, chat, stream and tool-calling members on any `ILlmProvider`, then each behaves per the contract and capability flags.

## Surface: RAG generation

<a id="d-req-rag-007"></a>
- **REQ-RAG-007** — Auto-RAG generation (Ask / AskStream / ChatWithRag). *BRD:* BRD-34, BRD-35, BRD-36, BRD-37, BRD-38. *Phase 1.*
  - *Acceptance:* When a developer calls `AskAsync` with a question, then the answer, its sources with scores and token usage return.

## Surface: Structured output

<a id="d-req-rag-008"></a>
- **REQ-RAG-008** — Structured / typed output (CompleteAsync&lt;T&gt;). *BRD:* BRD-39. *Phase 1.*
  - *Acceptance:* When a developer calls `CompleteAsync<T>` with a prompt, then the model's JSON deserialises into `T`.

## Surface: Agent loop

<a id="d-req-rag-009"></a>
- **REQ-RAG-009** — Tool calling & agent loop. *BRD:* BRD-40, BRD-41, BRD-42, BRD-43. *Phase 1.*
  - *Acceptance:* When a developer runs the agent loop on a tool-using prompt, then tools execute and a final answer returns.

## Surface: Conversation memory

<a id="d-req-rag-010"></a>
- **REQ-RAG-010** — Conversation memory (token-budget trimming). *BRD:* BRD-44, BRD-45, BRD-46. *Phase 1.*
  - *Acceptance:* When a developer enables `WithConversationMemory()` and chats twice, then the second turn sees the first.

## Surface: Token tracking

<a id="d-req-rag-011"></a>
- **REQ-RAG-011** — Token tracking & budgets. *BRD:* BRD-47, BRD-48, BRD-49, BRD-50. *Phase 1.*
  - *Acceptance:* When a developer completes a prompt with tracking on, then `ITokenTracker` records tokens and estimated cost for the operation and the session.

## Surface: Resilience

<a id="d-req-rag-012"></a>
- **REQ-RAG-012** — Resilience & retry (backoff / 429 / circuit breaker). *BRD:* BRD-51, BRD-52, BRD-53. *Phase 1.*
  - *Acceptance:* When a provider call fails transiently, then it is retried with exponential backoff up to `MaxRetries` within the timeout.

## Surface: Fallback provider

<a id="d-req-rag-013"></a>
- **REQ-RAG-013** — Fallback LLM provider. *BRD:* BRD-54. *Phase 1.*
  - *Acceptance:* When the primary provider fails after retries, then the fallback provider answers the same request.

## Surface: Prompt templates

<a id="d-req-rag-014"></a>
- **REQ-RAG-014** — Prompt templates (default + custom). *BRD:* BRD-55, BRD-56. *Phase 1.*
  - *Acceptance:* When a developer sets a system prompt and context template with `WithPromptTemplate`, then the RAG prompt uses them.

## Surface: Sevak application (moved)

<a id="d-req-fn-007"></a>
- **REQ-FN-007** — Sevak (formerly TechieDesk): Home landing + navigation; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-62, BRD-63, BRD-64, BRD-65, BRD-66, BRD-67, BRD-68. *Phase 1.*
  - *Acceptance:* When a user opens the Settings page on Sevak, then the embedding source and vector store can be configured and saved.

<a id="d-req-fn-008"></a>
- **REQ-FN-008** — Sevak (formerly TechieDesk): Settings page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-62. *Phase 1.*
  - *Acceptance:* When a user opens the Settings page on Sevak, then the embedding source and vector store can be configured and saved.

<a id="d-req-fn-009"></a>
- **REQ-FN-009** — Sevak (formerly TechieDesk): LLM Settings page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-63. *Phase 1.*
  - *Acceptance:* When a user opens the LLM Settings page on Sevak, then provider, fallback, usage, resilience and prompts can be configured.

<a id="d-req-fn-010"></a>
- **REQ-FN-010** — Sevak (formerly TechieDesk): Ingestion + Text Ingestion pages; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-64. *Phase 1.*
  - *Acceptance:* When a user uploads a file or pastes text on Sevak, then the document is ingested and listed.

<a id="d-req-fn-011"></a>
- **REQ-FN-011** — Sevak (formerly TechieDesk): Chat page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-65. *Phase 1.*
  - *Acceptance:* When a user asks a question on the Chat page on Sevak, then the answer streams with sources, top-K and a document filter.

<a id="d-req-fn-012"></a>
- **REQ-FN-012** — Sevak (formerly TechieDesk): LLM Playground page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-66. *Phase 1.*
  - *Acceptance:* When a user runs completion, structured output and chat on the LLM Playground page on Sevak, then each returns a result.

<a id="d-req-fn-013"></a>
- **REQ-FN-013** — Sevak (formerly TechieDesk): Tool Demo page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-67. *Phase 1.*
  - *Acceptance:* When a user runs a tool-using prompt on the Tool Demo page on Sevak, then tool calls and results show in the trace.

<a id="d-req-fn-014"></a>
- **REQ-FN-014** — Sevak (formerly TechieDesk): Token Usage dashboard page; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-68. *Phase 1.*
  - *Acceptance:* When a user opens the Token Usage page on Sevak, then usage, budget status and the per-model breakdown show.

<a id="d-req-fn-015"></a>
- **REQ-FN-015** — Sevak (formerly TechieDesk): Test LLM connection UI; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-69. *Phase 1.*
  - *Acceptance:* When a user presses Test connection on Sevak, then a reachable provider reports success and an unreachable one a clear error.

<a id="d-req-fn-016"></a>
- **REQ-FN-016** — Sevak (formerly TechieDesk): All pages render via TrBlazeUI components + Lucide icons; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-70. *Phase 1.*
  - *Acceptance:* When a user opens any page on Sevak, then it renders with TrBlazeUI components and Lucide icons.

<a id="d-req-fn-017"></a>
- **REQ-FN-017** — Sevak (formerly TechieDesk): Qdrant Admin: Docker container lifecycle UI; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-71. *Phase 1.*
  - *Acceptance:* When a user opens the Qdrant Admin page on Sevak, then Docker status shows and the container can be created, started, stopped and removed.

<a id="d-req-fn-018"></a>
- **REQ-FN-018** — Sevak (formerly TechieDesk): Qdrant Admin: collection CRUD; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-72. *Phase 1.*
  - *Acceptance:* When a user manages collections on the Qdrant Admin page on Sevak, then create, list, inspect and delete reflect the server.

<a id="d-req-fn-019"></a>
- **REQ-FN-019** — Sevak (formerly TechieDesk): Qdrant Admin: vector browse / search / detail / bulk delete; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-73. *Phase 1.*
  - *Acceptance:* When a user browses vectors on the Qdrant Admin page on Sevak, then search, detail and bulk delete work against the collection.

<a id="d-req-fn-020"></a>
- **REQ-FN-020** — Sevak (formerly TechieDesk): Rename app TechieRagWeb → TechieDesk; screen moved to the Sevak repository 2026-09-24. *BRD:* BRD-81, BRD-82. *Phase 1.*
  - *Acceptance:* When a reader opens the product statement on Sevak, then Sevak is the application showing the full capabilities of TechieRag, freemium, limited not gated.

