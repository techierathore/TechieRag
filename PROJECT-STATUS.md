---
project: TechieRag
last_updated: 2026-10-04
current_phase: Phase 2 of 2 · UAT — handoff done, 89 of 89 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-10-04
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). All 89 rows Verified; handoff done 2026-10-04. The Ollama fixes for Lekhak (REQ-RAG-112, REQ-RAG-113) are in the UsageGuide, DevGuide, AI reference and the agent guide, and Lekhak's feedback file says how to use them. Awaiting UAT per the UsageGuide.

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

- 🔶 **Release notes:** `LlmConfig.MaxContextTokens` is now `int?` (REQ-RAG-113).
- 🔶 **Next package:** Lekhak re-checks TR-RAG-002/003; Chatur TR-RAG-003/004/005; Sevak needs `ImapMailActions`.
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 need 4.6 GB free; WSL has 7.8 GB total. Raise `memory=` in `.wslconfig`.
- 🔶 **Postgres (REQ-RAG-044):** container runs, but `TechieRagTestPostgres` is unset in WSL and Windows; live test skipped.
- 🔶 **Windows head:** the probe built but never answered on its debug port; rows graded by test only.
- 🔶 **Breaking change:** an appsettings key fails startup after the next package; check Sevak.
- 🔶 **Keys in chat:** OpenCode Go key (2026-10-01) and Gmail app password (2026-10-03); rotate if shared.
- 🔶 **iPhone evidence:** `tests/.artifacts/probe/ios-device-20261004/` is swept after 7 days; copy it to keep it.
- 🔶 **Flaky:** 6 `EmbeddingSignatureStampTests` on Windows; live Gmail test once.
- 🔶 **Owner git:** commit today's work in TechieRag and Lekhak.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-04 | probe-run | 87/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | record-probe | 87/87 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | amend-docs | 87/89 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | verify-phase | 89/89 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-10-04 | handoff-phase | 89/89 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

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
