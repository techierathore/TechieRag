---
project: TechieRag
last_updated: 2026-11-12
current_phase: Phase 1 of 3 (Shipped core library (v1.1 to v3)) · Handoff — 27 of 27 verified, 14 not applicable
last_verified_build: PASS
last_verified_date: 2026-11-12
---

# TechieRag — Status

## Where I am

Phase 1 of 3 (Shipped core library (v1.1 to v3)) is handed off: all 27 rows are Verified, including Sevak's three 9 November fixes (REQ-RAG-158 to REQ-RAG-160, live Claude call passed). The packages are set to 1.1.10. The release notes, AI reference, UsageGuide, ProductGuide, DevGuides, Sevak feedback replies and metrics report describe the fixes. Nothing is tagged, published or committed.

## Next command to run

Claude Code:
```
(owner) set current_phase to Released after UAT — no agent command
```
OpenCode:
```
(owner) set current_phase to Released after UAT — no agent command
```
Why: every row in this phase's scope is terminal and handoff has run; waiting on the owner; working docs/TechieRag-Checklist.md. Commit, tag `v1.1.10` and publish first.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 0 |

- None

## Known blockers

- REQ-FN-006 (Verified 2026-10-06) now fails its test: `apps/` still holds TechieDesk build output, logs and two `.db` files from 25 September. Not touched; delete `apps/` if nothing there is wanted.
- Phase 3 is unchanged: REQ-RAG-146 (GPU runtime choice) and REQ-RAG-155 (OpenAI and Gemini keys) wait on you.
- TR-RAG-059, TR-RAG-060 and AstroLyfe's entry are left for Thursday on Windows.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-11 | triage-and-fix | 26/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-11 | devguide | 26/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-12 | verify-phase | 26/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-12 | verify-phase | 27/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-12 | handoff-phase | 27/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 3 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 0 open · 10 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-11-11: 0 findings in the files the fix run wrote, see the checklist Remarks.

## Deferred / future

- Anthropic still sends `budget_tokens` for thinking and `tool_choice` `any` for `ToolChoice = "required"`; Claude Opus 5.5 and Sonnet 5.5 refuse both with a 400. Not in Sevak's report.
- Gemini thought signatures on function calls (newer Gemini models may ask for them back); not sent today.
- Image generation through the builder, `ITechieRag` and dependency injection; today only the factory route.
- Checklist rows for the 1.1.7 Lekhak work whose tests carry REQ-RAG-135 and REQ-RAG-136.
- Open consumer feedback entries (TR-RAG-020/021/022/026/035, etc.) for a future release.
- Picture reading on phones: out of this phase (owner, 2026-11-05).
