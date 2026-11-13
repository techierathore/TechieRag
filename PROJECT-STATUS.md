---
project: TechieRag
last_updated: 2026-11-13
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · Release — handoff done, 40 of 42 verified
last_verified_build: PASS
last_verified_date: 2026-11-13
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak): ready to release 1.1.10. Today's second run fixed Sevak TR-RAG-064 (REQ-RAG-164: current Claude prices, unknown models read "price not known", never $0), Verified. REQ-RAG-158 to REQ-RAG-164 are now BRD-216 to BRD-222 in the phase-1 BRD; phase 1 is 31 of 31 Verified. Shipped documents describe every fix. The live-test PostgreSQL is stopped. Nothing is committed, tagged or published.

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
| Blocked | 2 |

- [ ] REQ-RAG-146 — On a machine with a GPU the catalogue shall name a GPU model that reads pictures: Phi-4-multimodal-instruct, `microsoft/Phi-4-multimodal-instruct-onnx`, folder `gpu/gpu-int4-rtn-block-32` (MIT, about 5.1 GB), pinned to one commit, loaded through an ONNX Runtime GenAI GPU runtime. Asking for it where no GPU runtime is present shall refuse before the download with a message saying so *(owner, added 2026-11-05; which GPU runtime is open, see the Decision Request) (Blocked)
- [ ] REQ-RAG-155 — The library shall generate images: an `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, for OpenAI, Gemini and Venice, routed like the chat providers, with no key ever persisted; this re-scopes BRD-161 for images only *(Sevak feedback TR-RAG-058; Sevak BRD-127 and BRD-281; owner, 2026-11-07) (Blocked)

## Known blockers

- The Gmail test account has one message left in Trash: the live Gmail test lost the network during its restore step at about 09:28 UTC. It passed on a rerun; move that message back to INBOX, or Gmail deletes it after 30 days.
- REQ-FN-006 (Verified 2026-10-06) still fails its test: `apps/` holds TechieDesk build output, logs and two `.db` files from 25 September. Not touched.
- REQ-RAG-146 (GPU runtime choice) and REQ-RAG-155 (OpenAI and Gemini keys) wait on you.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-12 | verify-phase | 26/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-12 | verify-phase | 27/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-12 | handoff-phase | 27/27 Verified, 14 N/A | docs/TechieRag-Checklist.md#requirements-status |
| 2026-11-13 | triage-and-fix + handoff | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-13 | triage-and-fix + handoff + amend-docs | 40/42 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 3 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 0 open · 10 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-11-11: 0 findings in the files the fix run wrote, see the checklist Remarks.

## Deferred / future

- Anthropic still sends `budget_tokens` for thinking and `tool_choice` `any`; Opus 5.5 and Sonnet 5.5 refuse both.
- The default Anthropic model is still `claude-sonnet-4-5-20250929`, which the pricing page marks deprecated.
- Cache reads and writes are not priced by the token tracker.
- Image generation through the builder and dependency injection; today only the factory route.
- Open consumer feedback entries (TR-RAG-020/021/022/026/035) for a future release.
- Picture reading on phones: out of this phase (owner, 2026-11-05).
