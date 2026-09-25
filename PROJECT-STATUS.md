---
project: TechieRag
last_updated: 2026-09-25
current_phase: Phase 2 of 2 · Handoff — 83 of 83 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-09-25
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). All 83 rows Verified; handoff ran on 2026-09-25 and the library is ready for UAT. The UsageGuide, both DevGuides (phase 1 and the new phase 2), the Architecture and the BRDs are current and rendered. The physical iPhone measurement is the one open item and waits on two owner steps.

## Next command to run

Manual UAT: open `docs/TechieRag-UsageGuide.md` and work through "How to test, screen by screen"; when it passes, set `current_phase: Released` in this file by hand. Optional afterwards, the end-user manual:

Claude Code:
```
/TechieFlow:agents:flow-master *productguide TechieRag
```
OpenCode:
```
/flow-master *productguide TechieRag
```
Why: every row is Verified and handoff has run; what remains is your own test pass per the UsageGuide.

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

- 🔶 **iPhone run (REQ-FN-060):** the iPhone 17 Pro is connected and its device build passes, but Developer Mode is off on the phone and no Apple ID is signed in to Xcode. Steps: `docs/TechieRag-iPhone-Setup.md`. Then paste `record the iPhone probe run for REQ-FN-060`.
- 🔶 **BRD §9 platform matrix:** the phase-1 BRD's Windows and Mac Catalyst cells for `TechieRag`, `TechieRag.Embedded` and `TechieRag.Local` should say "tested", as the UsageGuide does. Fix with the analyst's `*amend-docs`.
- 🔶 **DevGuide findings:** nine small code and comment findings from 2026-09-25 are Remarks on their checklist rows and in the phase-2 DevGuide's Known issues; none changes a verdict. Fix them with `*fix-issues` when convenient.
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-25 | build-phase | 78/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 81/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 81/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 83/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | handoff-phase | 83/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

## Standards compliance

- Last check 2026-09-25: 0 findings, see the checklist Remarks.

## Deferred / future

- The physical iPhone row of the UsageGuide's measured-per-platform table, after the owner's two steps.
- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac, behind the same provider (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Ollama and Gemini multi-turn tool use.
- Rename TechieDesk to Sevak in about 15 XML doc comments under `src/TechieRag`.
- Update the Android emulator in Visual Studio's SDK (31.2.10 is older than its system image).
