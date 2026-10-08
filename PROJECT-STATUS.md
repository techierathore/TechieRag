---
project: TechieRag
last_updated: 2026-10-08
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · UAT — handoff done, 14 of 14 verified
last_verified_build: PASS
last_verified_date: 2026-10-08
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak). All 14 rows Verified on 2026-10-08. New today: model listing and an endpoint on `CreateForModel` (Lekhak, BRD-186/187). Sevak's TR-RAG-012 is now documented and TR-RAG-036 is pinned by tests (phase-2 rows re-verified). `RELEASE-NOTES.md` holds the next package's notes. Everything ships in the next package after 1.1.2, waiting for UAT.

## Next command to run

Claude Code:
```
(owner) set current_phase to Released after UAT — no agent command
```
OpenCode:
```
(owner) set current_phase to Released after UAT — no agent command
```
Why: every row in this phase's scope is terminal and handoff has run; waiting on the owner; working docs/TechieRag-P3-Checklist.md.

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

- 🔶 **ONNX Runtime issue to post:** ORT-001 in `docs/TechieRag-OnnxRuntime-Feedback.md` is ready; fill the macOS version.
- 🔶 **Next release (after 1.1.2):** paste `RELEASE-NOTES.md` into the GitHub Release.
- 🔶 **Behaviour changes:** default SQLite folder and repeat tool registration (REQ-RAG-121/122).
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 skip below 4.6 GB free; WSL has 7.8 GB total.
- 🔶 **Postgres (REQ-RAG-044):** `TechieRagTestPostgres` is unset; live test skipped.
- 🔶 **Keys in chat:** OpenCode Go key and Gmail app password; rotate if shared.
- 🔶 **Owner git:** commit TechieRag, Sevak and Lekhak (feedback replies).

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-06 | build-phase | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-06 | handoff-phase | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-06 | fix-issues | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-07 | triage-and-fix | 12/12 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-08 | fix-issues | 14/14 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 0 open · 4 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-10-03: 0 findings, see the checklist Remarks.

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items.
- Ollama and Gemini multi-turn tool use.
- OpenCode Go models on `/responses` and `/messages`.
- Rename TechieDesk to Sevak in about 15 XML doc comments.
- Update the Android emulator in Visual Studio's SDK.
- Model listing for Anthropic and Gemini (their own list calls), if a consumer asks.
