---
project: TechieRag
last_updated: 2026-10-15
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · Release — handoff done, 21 of 21 verified
last_verified_build: PASS
last_verified_date: 2026-10-15
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak). All 21 rows Verified on 2026-10-15. New today, from Lekhak TR-RAG-007: a failed LLM call throws `LlmHttpException` carrying the status code, the response body and the provider's own message (REQ-RAG-134, BRD-194). The AI reference, agent guides, release notes, UsageGuide and P3 DevGuide describe it, and Lekhak's feedback file has the reply. It ships in the next package after 1.1.5.

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

- 🔶 **Release:** NuGet is at 1.1.5; publish the next release with the "Next release after 1.1.5" section of `RELEASE-NOTES.md`.
- 🔶 **Owner git:** commit TechieRag and Lekhak (TR-RAG-007 reply in `docs/Lekhak-TechieRag-Feedback.md`).
- 🔶 **Behaviour change:** failed LLM calls throw `LlmHttpException`; exact-type checks must accept it; rerankers, embedders not covered (REQ-RAG-134).
- 🔶 **No live Venice or GLM call:** no key here; wire format and error text tested on a loopback server.
- 🔶 **Gmail live test flaky:** REQ-RAG-110 failed twice on Gmail IMAP search, then passed.
- 🔶 **ONNX Runtime issue:** ORT-001 ready to post; fill the macOS version.
- 🔶 **Local model re-check:** REQ-RAG-057/059/063/065 skip below 4.6 GB free.
- 🔶 **Postgres (REQ-RAG-044):** `TechieRagTestPostgres` unset; live test skipped.
- 🔶 **Windows probe head not driven** (TechieFlow TF-006); rows graded by their tests.
- 🔶 **Keys in chat:** OpenCode Go key and Gmail app password; rotate if shared.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-09 | triage-and-fix + handoff | 15/15 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-11 | fix-issues + handoff | 16/16 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-11 | triage-and-fix + handoff | 20/20 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-11 | fix-issues + handoff | 20/20 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-15 | triage-and-fix + handoff | 21/21 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 3 open · 4 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-10-15: 0 findings, see the checklist Remarks.

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items.
- Ollama and Gemini multi-turn tool use.
- OpenCode Go models on `/responses` and `/messages`.
- Rename TechieDesk to Sevak in about 15 XML doc comments.
- Update the Android emulator in Visual Studio's SDK.
- Gemini model listing, if a consumer asks.
- Typed Venice switches (web search, thinking); the model-id suffix works today.
