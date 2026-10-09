---
project: TechieRag
last_updated: 2026-10-09
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · Release — handoff done, 15 of 15 verified
last_verified_build: PASS
last_verified_date: 2026-10-09
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak). All 15 rows Verified on 2026-10-09. New today: `ModelChooser`, where a small model picks the model for a request (Chatur TR-RAG-006, REQ-RAG-128). It has 6 tests and a live run on qwen2.5-0.5b. The AI reference, DevGuide, UsageGuide, release notes and the reply in Chatur's feedback file are current. Everything ships in the next package after 1.1.2.

## Next command to run

Claude Code:
```
(owner) commit, then build and publish the package — its shipped documents are current; no agent command
```
OpenCode:
```
(owner) commit, then build and publish the package — its shipped documents are current; no agent command
```
Why: every row in this phase's scope is terminal and the shipped documents were brought up to date; working docs/TechieRag-P3-Checklist.md.

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

- 🔶 **Release (after 1.1.2):** create GitHub Release `v1.1.3` with the `RELEASE-NOTES.md` section, then run *Publish to NuGet.org* on that tag.
- 🔶 **Owner git:** commit TechieRag and Chatur (TR-RAG-006 reply in `Chatur/docs/Chatur-TechieRag-Feedback.md`).
- 🔶 **ONNX Runtime issue to post:** ORT-001 in `docs/TechieRag-OnnxRuntime-Feedback.md` is ready; fill the macOS version.
- 🔶 **Behaviour changes:** default SQLite folder and repeat tool registration (REQ-RAG-121/122).
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 skip below 4.6 GB free; WSL has 7.8 GB total.
- 🔶 **Postgres (REQ-RAG-044):** `TechieRagTestPostgres` is unset; live test skipped.
- 🔶 **Windows probe head not driven:** the verify boot waits for a web view the native probe never opens (TechieFlow TF-006); rows were graded by their tests.
- 🔶 **Keys in chat:** OpenCode Go key and Gmail app password; rotate if shared.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-06 | handoff-phase | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-06 | fix-issues | 11/11 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-07 | triage-and-fix | 12/12 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-08 | fix-issues | 14/14 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-09 | triage-and-fix + handoff | 15/15 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 2 open · 4 closed — docs/TechieRag-TechieFlow-Feedback.md

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