---
project: TechieRag
stack: .NET 10 / class library (NuGet) / TechieRag.Embedded (ONNX BGE-M3) / Blazor Server sample (TrBlazeUI)
last_updated: 2026-06-27
current_phase: Build
last_verified_build: PASS
last_verified_date: 2026-06-25
---

# TechieRag — Status

> Migrated to the single-checklist framework (2026-06-27); prior Verified/Done verdicts predate the new visual-truth gate and have not been visually confirmed.

## Where I am
TechieRag is a configurable .NET 10 RAG library (NuGet), ~96% complete. Day-1 docs were reverse-engineered
on 2026-06-25 from the codebase plus the v1.1 refactoring roadmap (completed 2025-12-30) and the v2 LLM
implementation spec (completed 2026-02-18). The v1.1 core (config/builder, 3 vector stores, 9 document
processors, 6 embedding providers, semantic search, the `TechieRagWeb` sample, and Qdrant admin) and the
entire v2 LLM layer (6 LLM providers, auto-RAG generation, agent/tool loop, conversation memory, token
tracking, resilience, fallback, prompt templates, and 4 new sample pages) are all shipped and were validated
by manual/integration testing (21 documented scenarios). The roadmap + spec were migrated this session into
the UI + Functional checklists — every requirement is marked `Done (pre-existing)`. The two library packages
and the test project build green; the sample is blocked from restoring only by a missing TrBlazeUI GitHub
Packages credential. Remaining work is a formal automated test suite and optional OpenTelemetry exporters
(both deferred — not BRD requirements).

DevGuide generated 2026-06-25, refreshed 2026-06-30 (`--update`: Tool Calling Demo re-mapped to as-built agent-loop anchors; core lib re-built clean, 0 errors; 0 new defects): docs/TechieRag-DevGuide.md (single doc — 1 role/anonymous, 11 screens) + .html.
⚠ STATIC-ONLY (sample can't boot without the TrBlazeUI PAT). 11 code-confirmed defects logged to the UI
checklist; 6 REQs flagged `Needs re-verify` (REQ-UI-003/005/006/007/012/013).

## Next command to run
```
/TechieFlow:agents:flow-master *build-phase TechieRag      (OpenCode: /flow-master *build-phase TechieRag)
```
Adds the remaining tests to the open REQ tail in docs/TechieRag-Checklist.md (Done items not rebuilt). Live UI verify of samples/TechieRagWeb still needs the TrBlazeUI GitHub Packages token (externally blocked).

## Open requirements
- 6 UI requirements flagged `Needs re-verify` by the 2026-06-25 DevGuide (static code-confirmed defects):
  REQ-UI-003 (LLM Settings Reset is a no-op), REQ-UI-005 (Chat streaming footer never counts tokens/cost +
  double retrieval), REQ-UI-006 (Playground Temperature/MaxTokens no-op + Structured output not parsed),
  REQ-UI-007 (Tool Demo execution trace unwired — library `AgentLoopRunner` has no step callback),
  REQ-UI-012 (Qdrant version hard-coded, Vectors col duplicates Points), REQ-UI-013 (vector pagination cursor bug).
- All other `REQ-UI-*`, 3 `REQ-FN-*`, 14 `REQ-RAG-*`, and 7 `REQ-NFR-*` remain `Done (pre-existing)`.
  See the **Requirements Status** table in docs/TechieRag-Checklist.md.

## Known blockers
- **Sample restore needs a credential (not a code defect):** `samples/TechieRagWeb` references `TrBlazeUI.Components`
  and `TrBlazeUI.Icons.Lucide` from GitHub Packages (`nuget.pkg.github.com/techierathore`), which return
  **401 Unauthorized** without an authenticated `nuget.config` token. The two shipped library packages restore
  from nuget.org alone. Add a GitHub PAT with `read:packages` to `nuget.config` to build the sample.
- **Dependency advisory (NU1903):** `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (transitive via the SQLite-vec store /
  test project) has a known high-severity advisory — bump when an updated version is available.

## Verification log
| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-06-25 | Discovery (day-1 brownfield) | Docs generated; roadmap+spec migrated; build PASS (rung #2, 3 non-sample projects) | docs/TechieRag-Checklist.md#requirements-status |

## Library feedback summary
- TrBlazeUI: 0 major, 0 minor — docs/TechieRag-TrBlazeUI-Feedback.md
- TechieRag: 0 major, 0 minor — docs/TechieRag-TechieRag-Feedback.md

## Standards compliance (last verifier check)
- Underscore fields: 0 violations (grep over src/ + tests/, 2026-06-25)
- Test method underscores: 0 violations (grep over tests/, 2026-06-25)
- Mis-prefixed fields: N/A — no-prefix project (camelCase convention; no obj-prefix grep applies)

## Deferred / future
- Formal automated xUnit test suite (v2 Phase 7 — validated manually only).
- OpenTelemetry metrics (`TechieRagMetrics`) + distributed tracing (`TechieRagActivitySource`) for Prometheus/Grafana/Jaeger.
- Resolve the SQLitePCLRaw advisory (NU1903) by upgrading the transitive package.

## Framework migration note (2026-06-27)
- The two checklists merged into ONE `docs/TechieRag-Checklist.md` (all REQ-UI/FN/RAG/NFR-* in one Requirements Status table; originals archived to docs/OldDocs/, every verdict verbatim).
- The build commands `*build-ui-phase`/`*build-rag-phase`/`*build-functional-phase` were dissolved into ONE `*build-phase TechieRag` (flow-master; it calls /trblazeui + /techierag as sub-agents).
- New VISUAL-TRUTH gate: the verifier now also checks each screen LOOKS right (no overlap/clip/off-viewport), not just that controls have data — every prior Verified/Done verdict predates this and is not yet visually confirmed.
- New `*fix-issues TechieRag {folder}` (flow-master): drop screenshots of broken screens in a folder → it triages + fixes + re-verifies.
- RESTART any open Claude Code / OpenCode session against this project to load the new task/agent defs.
