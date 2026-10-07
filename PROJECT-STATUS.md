---
project: TechieRag
last_updated: 2026-10-06
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · UAT — handoff done, 11 of 11 verified
last_verified_build: PASS
last_verified_date: 2026-10-06
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak). All 11 rows Verified and handed off on 2026-10-06: the UsageGuide has their test plan and limitations, and the new phase-3 DevGuide maps each surface to file and line. The two gaps the handoff found (REQ-RAG-116, REQ-RAG-119) were fixed and re-verified the same day. Phase 2 (90 rows) is also Verified. Both wait for UAT per the UsageGuide, and both ship in the next package with the TR-RAG-048 version fix.

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

- 🔶 **Release 1.1.2:** publish the `v1.1.2` release; Sevak waits (TR-RAG-048).
- 🔶 **Behaviour changes:** default SQLite folder and repeat tool registration (REQ-RAG-121/122).
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 skip below 4.6 GB free; WSL has 7.8 GB total.
- 🔶 **Postgres (REQ-RAG-044):** `TechieRagTestPostgres` is unset; live test skipped.
- 🔶 **Breaking change:** an appsettings key fails startup; check Sevak.
- 🔶 **Keys in chat:** OpenCode Go key and Gmail app password; rotate if shared.
- 🔶 **Owner git:** commit today's work in TechieRag and Sevak.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-06 | triage-and-fix | 90/90 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-06 | amend-docs | 90/90 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-06 | build-phase | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-06 | handoff-phase | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-06 | fix-issues | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

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
