---
project: TechieRag
last_updated: 2026-11-19
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · Release — handoff done, 40 of 41 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-11-19
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak), ready to release as 1.3.0. The 19 November feedback round is built and verified (REQ-RAG-165 to 172). Your two decisions are recorded: the graphics-card picture model is deferred (REQ-RAG-146 N/A) and Intel Macs are left out (REQ-RAG-173 N/A). Nothing waits on you except the OpenAI and Gemini keys. Nothing is committed.

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
| Blocked | 1 |

- [ ] REQ-RAG-155 — The library shall generate images: an `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, for OpenAI, Gemini and Venice, routed like the chat providers, with no key ever persisted; this re-scopes BRD-161 for images only *(Sevak feedback TR-RAG-058; Sevak BRD-127 and BRD-281; owner, 2026-11-07) (Blocked)

## Known blockers

- REQ-RAG-155 waits on OpenAI and Gemini keys in the live-test settings file.
- REQ-FN-057 (phase 2) stays Needs re-verify until a GitHub probe run passes; run 44496269925's iOS failure (SDK 11 on the new macOS image) is fixed by an SDK pin, not yet run in CI.
- Sevak and TrStudio must add the new `TechieRag.PgVector` package when they move to 1.3.0.
- The Gmail live test refused one search once today and passed on re-run; the test account's Trash may hold its messages.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-13 | triage-and-fix + handoff + amend-docs | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-14 | fix-issues | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-19 | fix-issues | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-19 | verify-phase | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-19 | amend-docs | 40/41 Verified, 1 N/A | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 3 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 0 open · 10 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-11-19: 0 findings in the files the fix run wrote, see the checklist Remarks.

## Deferred / future

- The graphics-card picture model (Phi-4-multimodal), with a later engine upgrade (your decision 1, option C).
- A smaller multilingual embedded model for bulk ingest, if bge-m3-int8 (about 0.4 s a chunk) is still too slow for Sevak.
- The core still targets `net8.0`, which reached end of support on 10 Nov 2026, as did .NET 9.
- Anthropic still sends `budget_tokens` for thinking and `tool_choice` `any`; Opus 5.5 and Sonnet 5.5 refuse both.
- The default Anthropic model is still `claude-sonnet-4-5-20250929`, which the pricing page marks deprecated.
- Cache reads and writes are not priced by the token tracker.
