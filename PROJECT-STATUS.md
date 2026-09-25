---
project: TechieRag
last_updated: 2026-09-25
current_phase: Phase 2 of 2 · Build — 5 not built, 78 of 83 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-09-25
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). 78 of 83 rows Verified. `TechieRag.Local` runs ONNX Runtime GenAI on every platform and now also runs any ONNX GenAI model named on Hugging Face (BRD-166), proven with Arm's Gemma 3 1B. Five rows wait on your devices, your next push, a PostgreSQL you choose, or Windows/Android runs.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieRag
```
OpenCode:
```
/flow-master *build-phase TechieRag
```
Why: 5 rows are not built yet: REQ-RAG-044, REQ-FN-057, REQ-RAG-058, REQ-FN-058, REQ-FN-060; working docs/TechieRag-P2-Checklist.md. Run it on the Windows laptop for the Windows and Android rows, after the owner items below.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 0 |
| PARTIAL | 5 |

- [ ] REQ-FN-057 — F-PLATFORM: CI builds the probe for all four heads on every push and runs its button on an Android emulator where CI allows; an unbuildable head is reported, never skipped (PARTIAL)
- [ ] REQ-FN-058 — F-LOCAL-LLM: `TechieRag.Local` ships buildTransitive targets with the native runtime libraries for all four platforms; no hand-written csproj changes (PARTIAL)
- [ ] REQ-FN-060 — F-LOCAL-LLM: tokens per second, time to first token and peak memory recorded per platform on named devices in the support matrix (PARTIAL)
- [ ] REQ-RAG-044 — The shipped `IVectorStore` set is SQLite, pgvector and Qdrant; `PgVectorStore` shall be proven against a real PostgreSQL through `LivePgVectorStoreTes (PARTIAL)
- [ ] REQ-RAG-058 — F-LOCAL-LLM: one public provider over an internal `ILocalLlmRuntime` with one implementation per platform per `DECISIONS.md`; identical behaviour everywhere (PARTIAL)

## Known blockers

- 🔶 **Publish the phone model** (our own Qwen2.5 0.5B) on your Hugging Face account and send its address: `docs/MODEL-PUBLISHING-GUIDE.md`; files in `~/TechieRag-model-upload/qwen2.5-0.5b-instruct-onnx/` on the Mac.
- 🔶 **REQ-RAG-044:** set `TechieRagTestPostgres` to a pgvector server you choose; agents may not create one to test against.
- 🔶 **REQ-FN-057:** the first CI run after your next push.
- 🔶 **REQ-RAG-058, REQ-FN-058:** Windows and Android runs, on the Windows laptop (no Android SDK on this Mac).
- 🔶 **REQ-FN-060:** an Android phone and a physical iPhone (runbook in the UsageGuide Platform notes).
- 🔶 **Owner git:** commit today's work.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-24 | `*amend-docs` TechieRag (BRD-81/87 amended; BRD-88…114 added) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*amend-docs` TechieRag + TechieDesk (BRD-83…87; governance reversal) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*triage-issues` → `*fix-issues` → verify (REQ-FN-003, REQ-FN-004) | ✅ 2/2 Verified; 723 lib tests pass; version rules 9/9 | [P1](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-09-25 | build-phase | 77/82 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-25 | build-phase | 78/83 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |

## Library feedback summary

- OnnxRuntime: 1 open · 0 closed — docs/TechieRag-OnnxRuntime-Feedback.md
- OnnxRuntimeGenAI: 1 open · 0 closed — docs/TechieRag-OnnxRuntimeGenAI-Feedback.md

## Standards compliance

- Last check 2026-09-25: 0 findings, see the checklist Remarks.

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- LLamaSharp with Metal on the Mac, behind the same provider, if Mac speed matters later (decision 2 option B).
- Hugging Face tokens for gated models in `LocalModel.FromHuggingFace` (refused with a clear message today).
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Ollama and Gemini multi-turn tool use (noted in the UsageGuide).
- Rename TechieDesk to Sevak in about 15 XML doc comments under `src/TechieRag`.
