---
project: TechieRag
last_updated: 2026-09-25
current_phase: Phase 2 of 2 · UAT — handoff done, 84 of 84 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-09-25
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). All 84 rows Verified. The four document misses from 2026-09-24 are closed: the Architecture no longer says stores are fixed at 1024 dimensions, two BRD items are reworded, one acceptance line is corrected, and both platform matrices agree. The library waits on your test pass. Product Guide generated 2026-09-25: docs/TechieRag-ProductGuide.md (+ .html); 14 tasks, 12 screenshots.

## Next command to run

Manual UAT: open `docs/TechieRag-UsageGuide.md` and work through "How to test, screen by screen"; when it passes, change the phase line at the top of this file from UAT to Released by hand. No agent command is next.

Claude Code:
```
(owner) set current_phase to Released after UAT — no agent command
```
OpenCode:
```
(owner) set current_phase to Released after UAT — no agent command
```
Why: every row in this phase's scope is terminal and handoff has run; waiting on the owner; working docs/TechieRag-P2-Checklist.md.

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

- 🔶 **iPhone run (REQ-FN-060):** the iPhone 17 Pro is connected and its device build passes, but Developer Mode is off on the phone and no Apple ID is signed in to Xcode. Steps: `docs/TechieRag-iPhone-Setup.md`. Then paste `record the iPhone probe run for REQ-FN-060`.
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-25 | handoff-phase | 83/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | log-miss | 83/84 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 84/84 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | amend-docs | 84/84 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | amend-docs | 84/84 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

## Standards compliance

- Last check 2026-09-25: 0 findings, see the checklist Remarks.

## Deferred / future

- The physical iPhone row of the UsageGuide's measured-per-platform table, after the owner's two steps.
- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac, behind the same provider (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Ollama and Gemini multi-turn tool use.
- Rename TechieDesk to Sevak in about 15 XML doc comments under `src/TechieRag`.
- Update the Android emulator in Visual Studio's SDK (31.2.10 is older than its system image).
