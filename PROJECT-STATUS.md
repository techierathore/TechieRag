---
project: TechieRag
last_updated: 2026-10-04
current_phase: Phase 2 of 2 · UAT — handoff done, 87 of 87 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-10-03
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). All 87 rows Verified. On 2026-10-04 the probe ran on the owner's iPhone 17 Pro (iOS 27.0.1), both buttons: Paris (0.824); Qwen2.5 0.5B at 155 tokens per second, 485 MB peak. All four platforms now read tested in the UsageGuide and BRD matrices. Four local-model rows keep their 2026-09-25 result. Awaiting UAT.

## Next command to run

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

- 🔶 **Sevak:** manage mode needs a package release with `ImapMailActions`; publish after UAT.
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 need 4.6 GB free; WSL has 7.8 GB total. Raise `memory=` in `.wslconfig`.
- 🔶 **Postgres (REQ-RAG-044):** live test skipped; `TechieRagTestPostgres` is not visible in WSL.
- 🔶 **Chatur:** publish the package after 1.0.8; Chatur re-checks TR-RAG-003, 004, 005.
- 🔶 **Breaking change:** an appsettings key fails startup after the next package; check Sevak.
- 🔶 **Keys in chat:** OpenCode Go key (2026-10-01) and Gmail app password (2026-10-03); rotate if shared.
- 🔶 **iPhone evidence:** `tests/.artifacts/probe/ios-device-20261004/` is swept after 7 days; copy it to keep it.
- 🔶 **Framework:** feedback checks match TR-RAG ids across apps (MISS-TechieRag-20261001-04).
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-03 | amend-docs | 85/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-03 | build-phase | 86/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-03 | verify-phase | 87/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | probe-run | 87/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | record-probe | 87/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

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
