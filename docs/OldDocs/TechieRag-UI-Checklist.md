# TechieRag — UI Mockup Checklist

> Migrated from docs/trrag-refactoring-roadmap.md + docs/techierag-v2-llm-implementation-spec.md on 2026-06-25. Phase structure, completion %, and status remarks carried over verbatim — verify before building. All UI lives in the `samples/TechieRagWeb` Blazor Server sample.

## Table of Contents

1. [Scope](#scope)
2. [Requirements Status](#requirements-status)
3. [Page details](#page-details)

## Scope

UI scope is the `TechieRagWeb` sample application — a Blazor Server demo (TrBlazeUI components + Lucide icons) that exercises every TechieRag capability across ten routed pages, plus the Qdrant administration UI. Traces to BRD §9 F-WEB and F-QDRANT (BRD-62…BRD-73). All pages were delivered in v1.1 (core pages) and v2 (4 new AI pages + full TrBlazeUI migration); everything below is migrated as **Done (pre-existing)**.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-UI-001 | Home landing + navigation | Done (pre-existing) | 100% | v1.1; per trrag-refactoring-roadmap.md Phase 5 | [view](#d-req-ui-001) |
| REQ-UI-002 | Settings page (embedding + vector store) | Done (pre-existing) | 100% | v1.1; roadmap Phase 5.2; v2 TrBlazeUI rewrite (spec Phase 6). ⚠ DevGuide 2026-06-25: "Reset to Defaults" never calls `RagManager.ReconfigureAsync` (Settings.razor:326-331) so the live instance keeps old config until next Save; `EnableTelemetry` is persisted but never read by `TechieRagManager` (no usage at TechieRagManager.cs:94-253) — toggle is a no-op (static — confirm at runtime). Core save/init path unaffected | [view](#d-req-ui-002) |
| REQ-UI-003 | LLM Settings page (provider/fallback/usage/resilience/prompts) | Needs re-verify | 90% | v2; spec Phase 6 (#20), completed 2026-02-18. ⚠ DevGuide 2026-06-25: "Reset" (`ResetToDefaultsAsync`, LlmSettings.razor:415-425) is in-memory only — never calls SaveConfigAsync or ReconfigureAsync and shows a success toast anyway, so the reset is silently lost (static — confirm at runtime) | [view](#d-req-ui-003) |
| REQ-UI-004 | Ingestion + Text Ingestion pages | Done (pre-existing) | 100% | v1.1; roadmap Phase 5.3; v2 TrBlazeUI rewrite | [view](#d-req-ui-004) |
| REQ-UI-005 | Chat page (RAG chat, streaming, sources, top-K, filter) | Needs re-verify | 85% | v2; spec Phase 6 (#21), completed 2026-02-18. ⚠ DevGuide 2026-06-25: with Streaming ON (the default) neither `HandleDirectLlm` nor `HandleAutoRag` streaming branch accumulates `totalTokens`/`totalCost` (Chat.razor:259-288) — footer reads "0 tokens / $0.0000"; streamed Auto-RAG also runs the vector search twice per question (Chat.razor:280 + TechieRagClient.cs:454) (static — confirm at runtime) | [view](#d-req-ui-005) |
| REQ-UI-006 | LLM Playground page (completion/structured/chat) | Needs re-verify | 80% | v2; spec Phase 6 (#23, NEW). ⚠ DevGuide 2026-06-25: Temperature/Max Tokens inputs are never parsed into `LlmCompletionOptions` for any call (LlmPlayground.razor:47,53 vs 221/232/268) — no-op controls; "Structured Output" tab renders the raw model JSON string and never deserializes into the labelled types (:269,134) (static — confirm at runtime) | [view](#d-req-ui-006) |
| REQ-UI-007 | Tool Demo page (agent loop, execution trace) | Needs re-verify | 75% | v2; spec Phase 6 (#24, NEW). ⚠ DevGuide 2026-06-25: the Execution Trace always shows a single hardcoded step regardless of real tool calls — `AgentLoopRunner.RunAsync` (AgentLoopRunner.cs:61-126) exposes no per-step callback, so `executionSteps` only ever gets `new ExecutionStep(1, ...)` (ToolDemo.razor:275-278). Root cause is a library API gap (static — confirm at runtime). ✅ FIXED 2026-06-25: `AgentLoopRunner.RunAsync` now reports an `IProgress<AgentStep>` (new `Models/AgentStep.cs`); ToolDemo passes a `Progress<AgentStep>` that appends each tool request/execution + final answer to the live trace (`ToExecutionStep`). Core lib builds clean (0 errors); kept `Needs re-verify` until the sample is booted to confirm the trace renders | [view](#d-req-ui-007) |
| REQ-UI-008 | Token Usage dashboard page | Done (pre-existing) | 100% | v2; spec Phase 6 (#22, NEW). ⚠ DevGuide 2026-06-25: Estimated Cost reads $0.0000 for any model absent from the hard-coded pricing table even though tokens are counted (TokenUsageTracker.cs:207-217) — silent under-reporting for unlisted models (static — confirm at runtime) | [view](#d-req-ui-008) |
| REQ-UI-009 | Test LLM connection UI | Done (pre-existing) | 100% | v2; spec Phase 6. ⚠ DevGuide 2026-06-25: Test uses the last-built instance via `RagManager.GetLlmProvider()` (TechieRagManager.cs:365) — unsaved form edits are not tested until Save runs ReconfigureAsync; also blocks sync-over-async (cs:367) (static — confirm at runtime) | [view](#d-req-ui-009) |
| REQ-UI-010 | All pages render via TrBlazeUI components + Lucide icons | Done (pre-existing) | 100% | v2; spec Phase 6 (#19 — migrate ALL pages) | [view](#d-req-ui-010) |
| REQ-UI-011 | Qdrant Admin: Docker container lifecycle UI | Done (pre-existing) | 100% | v1.1; roadmap Phase 7.1/7.3 | [view](#d-req-ui-011) |
| REQ-UI-012 | Qdrant Admin: collection CRUD | Needs re-verify | 90% | v1.1; roadmap Phase 7.4. ⚠ DevGuide 2026-06-25: cluster Version is hard-coded `"1.12.x"` (QdrantAdminService.cs:318) not read from the server; the Collections grid "Vectors" column duplicates "Points" — both bound to the same `PointsCount` (cs:344-345) (static — confirm at runtime) | [view](#d-req-ui-012) |
| REQ-UI-013 | Qdrant Admin: vector browse / search / detail / bulk delete | Needs re-verify | 85% | v1.1; roadmap Phase 7.5. ⚠ DevGuide 2026-06-25: vector pagination encodes the offset as a numeric `PointId.Num` cursor (QdrantAdminService.cs:437) instead of Qdrant's `next_page_offset`, so Next/Previous beyond page 1 return wrong/empty results for UUID or non-contiguous IDs (static — confirm at runtime) | [view](#d-req-ui-013) |

**Status values:** `Not Started` · `In Progress` · `Implemented` (code done, not yet verified) · `Verified` (self-smoke or verifier PASS) · `Done (pre-existing)` (migrated as already complete — build agents must NOT rebuild; terminal like `Verified`) · `PARTIAL` · `FAIL` · `Blocked` · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug or library reference.

> **Note:** these are migrated as `Done (pre-existing)`. Restoring/running the sample for live UI verification requires the `TrBlazeUI.*` GitHub Packages credential (see PROJECT-STATUS Known blockers).

## Page details

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
