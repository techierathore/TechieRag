---
project: TechieRag
last_updated: 2026-11-07
current_phase: Phase 3 of 3 (Consumer feedback from Sevak) · Build — 4 not built, 34 of 40 verified
last_verified_build: PASS
last_verified_date: 2026-11-05
---

# TechieRag — Status

## Where I am

Phase 3 of 3 (Consumer feedback from Sevak). Local picture reading is built; 34 of 40 items are Verified. Four rows were added on 2026-11-07 for Sevak's assistant phases and are not built: PostgreSQL proven end to end, the host-data skill with undo, image generation (REQ-RAG-152 to REQ-RAG-155). The calendar test passed alone but was skipped in the verify run for memory; the graphics-card picture model is refused but not loaded (DirectML engine too old).

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieRag
```
OpenCode:
```
/flow-master *build-phase TechieRag
```
Why: REQ-RAG-152 to REQ-RAG-155 ship as a package before Sevak's phase 4 build; REQ-RAG-150 still needs the owner's measurement decision.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 4 |
| In Progress | 0 |
| Implemented | 1 |
| Needs re-verify | 0 |
| Blocked | 1 |

- [ ] REQ-RAG-152 — PostgreSQL end to end: vectors, conversations and workspaces in one harness-started PostgreSQL 18 with pgvector (Not Started)
- [ ] REQ-RAG-153 — Host-data skill: a host registers its entities and the agent gets tools for them (Not Started)
- [ ] REQ-RAG-154 — Every host-data change comes back as a typed record and can be undone (Not Started)
- [ ] REQ-RAG-155 — Image generation through `IImageGenerationProvider` for OpenAI, Gemini and Venice (Not Started)
- [ ] REQ-RAG-150 — Calendar-page test: every meeting title and time appears in the local model's text (Implemented)
- [ ] REQ-RAG-146 — Graphics-card picture model (Phi-4 multimodal) loads through a GPU runtime (Blocked)

## Known blockers

- Sevak's phase 4 build waits for BRD-210 to BRD-213 (REQ-RAG-152 to REQ-RAG-155) as a package.
- REQ-RAG-146: the DirectML engine package you chose stops at 0.14.1, below this library's engine 0.16.0 (ORTGENAI-003). Loading on a graphics card needs a new choice: WinML or CUDA, both from engine 0.17.0. The refusal before download is built and verified.
- REQ-RAG-150: the calendar test passed on this laptop when run alone (13:13), but in the verify run, with every suite running, only 2.9 GB of the 4.7 GB it needs was free, so it was skipped.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-29 | amend-docs | 23/29 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-10-29 | build-phase | 29/29 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-05 | amend-docs | 29/36 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-05 | build-phase | 34/36 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |
| 2026-11-07 | amend-docs | 34/40 Verified | docs/TechieRag-P3-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 3 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md
- TechieFlow: 1 open · 4 fixed upstream, not yet re-checked (TF-005, TF-006, TF-007 …) · 4 closed — docs/TechieRag-TechieFlow-Feedback.md

## Standards compliance

- Last check 2026-11-05: 0 findings, see the checklist Remarks. No code changed on 2026-11-07.

## Deferred / future

- Gemini thought signatures on function calls (newer Gemini models may ask for them back); not sent today.
- Checklist rows for the 1.1.7 Lekhak work whose tests carry REQ-RAG-135 and REQ-RAG-136 (TF-008).
- Open consumer feedback entries (TR-RAG-020/021/022/026/035, etc.) for a future release.
- OS built-in models as a later `ILocalLlmRuntime`.
- Picture reading on phones: out of this phase (owner, 2026-11-05).
- Picture reading takes about 6 minutes per page on this laptop's processor; shrinking a large picture before it is sent would cut that.
