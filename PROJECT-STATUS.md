---
project: TechieRag
last_updated: 2026-09-25
current_phase: Phase 2 of 2 · Build — 2 not built, 81 of 83 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-09-25
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). 81 of 83 rows Verified. The phone model now downloads by default from the owner's Hugging Face repository, pinned to one commit. `PgVectorStore` is proven against the WinPostgre container. The local model is proven on Windows and the Android emulator. CI's restore failure is fixed, pending a push; the Galaxy S23 ran the local model; an iPhone remains.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieRag
```
OpenCode:
```
/flow-master *build-phase TechieRag
```
Why: 2 rows are not built yet: REQ-FN-057, REQ-FN-060; working docs/TechieRag-P2-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 0 |
| PARTIAL | 2 |

- [ ] REQ-FN-057 — F-PLATFORM: CI builds the probe for all four heads on every push and runs its button on an Android emulator where CI allows; an unbuildable head is reported, never skipped (PARTIAL)
- [ ] REQ-FN-060 — F-LOCAL-LLM: tokens per second, time to first token and peak memory recorded per platform on named devices in the support matrix (PARTIAL)

## Known blockers

- 🔶 **REQ-FN-057:** the restore failure in CI run 36134404881 is fixed (`ProbeHead`); it needs the run page of your next push.
- 🔶 **REQ-FN-060:** the Android phone (Galaxy S23) is recorded; only a physical iPhone run on the Mac remains.
- 🔶 **BRD §9 platform matrix:** the Windows `TechieRag.Local` cell should now say "tested", as the UsageGuide does. Fix with the analyst's `*amend-docs`.
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-03 | `*amend-docs` TechieRag + TechieDesk (BRD-83…87; governance reversal) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*triage-issues` → `*fix-issues` → verify (REQ-FN-003, REQ-FN-004) | ✅ 2/2 Verified; 723 lib tests pass; version rules 9/9 | [P1](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-09-25 | build-phase | 77/82 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 78/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 81/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 2 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

## Standards compliance

- Last check 2026-09-25: 0 findings, see the checklist Remarks.

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac, behind the same provider, if Mac speed matters later (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace` (refused with a clear message today).
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Ollama and Gemini multi-turn tool use (noted in the UsageGuide).
- Rename TechieDesk to Sevak in about 15 XML doc comments under `src/TechieRag`.
- Update the Android emulator in Visual Studio's SDK (31.2.10 is older than its system image; it still runs).
