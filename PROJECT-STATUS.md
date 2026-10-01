---
project: TechieRag
last_updated: 2026-10-01
current_phase: Phase 2 of 2 · UAT — handoff done, 85 of 85 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-10-01
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). All 85 rows Verified; awaiting UAT per the UsageGuide. Handoff refreshed on 2026-10-01 for the OpenCode Go headers (BRD-168), the sign-in-required code and keys only through code: UsageGuide, phase-2 DevGuide, the AI reference and both installed `/techierag` agent files.

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

- 🔶 **Chatur:** publish the next package after 1.0.8; Chatur then re-checks TR-RAG-003, 004, 005.
- 🔶 **Breaking change:** an app with a key in its appsettings section fails at startup after the next package; check Sevak.
- 🔶 **OpenCode Go key:** pasted in chat 2026-10-01; rotate it if the transcript is shared. Live test reads `TechieRagOpenCodeGoKey`.
- 🔶 **Local model tests:** 13 TechieRag.Local tests fail here: phi-3-mini needs 4.6 GB free, WSL had 3.5 GB. Not caused by this change.
- 🔶 **Verify boot:** the Windows probe has no web view, so the DevTools port never opens; screenless rows were graded by tests.
- 🔶 **iPhone run (REQ-FN-060):** Developer Mode is off and no Apple ID is in Xcode. Steps: `docs/TechieRag-iPhone-Setup.md`.
- 🔶 **Framework:** feedback checks match TR-RAG ids across apps (MISS-TechieRag-20261001-04, wrongly logged as fixed; still open).
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-01 | triage-and-fix | 85/85 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-01 | amend-docs | 85/85 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-01 | amend-docs | 84/85 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-01 | build-phase | 85/85 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-01 | handoff-phase | 85/85 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

## Standards compliance

- Last check 2026-10-01: 0 findings, see the checklist Remarks.

## Deferred / future

- The physical iPhone row of the UsageGuide's per-platform table.
- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items.
- Ollama and Gemini multi-turn tool use.
- OpenCode Go models on `/responses` and `/messages`.
- Rename TechieDesk to Sevak in about 15 XML doc comments.
- Update the Android emulator in Visual Studio's SDK.
