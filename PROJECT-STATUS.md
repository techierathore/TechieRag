---
project: TechieRag
last_updated: 2026-09-24
current_phase: Phase 2 of 2 · Build — 5 not built, 70 of 82 verified, 1 not applicable
last_verified_build: PASS
last_verified_date: 2026-09-24
---

# TechieRag — Status

## Where I am

Phase 2 of 2 (Agents, separation, platforms, local model, streaming, sign-in, and the v3 library features harvested from the application ledger). Build pass done: 70 of 82 rows Verified. Typed streaming, `TechieRag.Agents`, the agentic contract, subscription sign-in, the probe app and every scan fix are in. Open: six local-model rows wait on your engine choice, five rows wait on your devices or the first CI run, and pgvector waits on a real PostgreSQL.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieRag
```
OpenCode:
```
/flow-master *build-phase TechieRag
```
Run it after answering `docs/TechieRag-Decision-Request.md`; it then builds REQ-RAG-057, 058, 063, 065, REQ-FN-058, 059.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 1 |
| Needs re-verify | 0 |
| Blocked | 6 |
| PARTIAL | 5 |

- [ ] REQ-FN-055 — native ONNX Runtime wiring for Mac Catalyst, iOS and Android (PARTIAL)
- [ ] REQ-FN-056 — probe app on all four heads (PARTIAL)
- [ ] REQ-FN-057 — CI builds the probe for all four heads (PARTIAL)
- [ ] REQ-FN-060 — local-model speed per platform on named devices (PARTIAL)
- [ ] REQ-RAG-053 — model root under the per-user app data folder on every platform (PARTIAL)
- [ ] REQ-RAG-044 — `PgVectorStore` proven against a real PostgreSQL (Implemented)
- [ ] REQ-FN-058 — `TechieRag.Local` native runtime libraries for four platforms (Blocked)
- [ ] REQ-FN-059 — probe app's second button generates a sentence (Blocked)
- [ ] REQ-RAG-057 — `UseLocalLlm()` answers a prompt (Blocked)
- [ ] REQ-RAG-058 — one runtime per platform behind `ILocalLlmRuntime` (Blocked)
- (2 more open rows in docs/TechieRag-P2-Checklist.md)

## Known blockers

- 🔶 **Your decisions** (`docs/TechieRag-Decision-Request.md`): 1 local-model engine per platform, 2 Mac local model, 3 where the phone model files come from, 4 the ChatGPT sign-in id. Decisions 1–3 block REQ-RAG-057, 058, 063, 065, REQ-FN-058, 059.
- 🔶 **Your devices:** a Mac, an Android phone and an iPhone for REQ-FN-055, 056, 060 and REQ-RAG-053 (runbook in the UsageGuide Platform notes).
- 🔶 **First CI run** after your next push, for REQ-FN-057 (`.github/workflows/probe.yml`).
- 🔶 **REQ-RAG-044:** set `TechieRagTestPostgres` to a pgvector server; the live tests skip without it.
- 🔶 **Live checks not yet run:** LM Studio for `TechieRag.Agents` (`TechieRagLiveLmStudioModel`) and a real ChatGPT sign-in (`TechieRagLiveChatGptSubscription=1`).
- 🔶 **Owner git:** commit today's work; `git rm -r --cached tests/TechieDesk.Tests` still drops the tracked `res.trx`.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-24 | build-phase | 70/82 Verified, 1 N/A | docs/TechieRag-P2-Checklist.md#requirements-status |
| 2026-09-24 | `*day1-brownfield` TechieRag (Large, 2 phases; BRD-115…163 harvested; apps/ deleted) | 📝 docs rewritten; library tests PASS via build ladder (rung 2); no verify run | [P1](docs/TechieRag-Checklist.md#requirements-status) · [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-24 | `*amend-docs` TechieRag (BRD-81/87 amended; BRD-88…114 added) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*amend-docs` TechieRag + TechieDesk (BRD-83…87; governance reversal) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*triage-issues` → `*fix-issues` → verify (REQ-FN-003, REQ-FN-004) | ✅ 2/2 Verified; 723 lib tests pass; version rules 9/9 | [P1](docs/TechieRag-Checklist.md#requirements-status) |

## Library feedback summary

- None filed upstream by this run. Incoming requests from Sevak: `docs/Sevak-TechieRag-Feedback.md` (TR-RAG-009, 015, 020–022 closed today).

## Standards compliance

- Last check 2026-09-24: 0 findings, see the checklist Remarks. Build ladder rung 2 PASS; 968 tests pass, 42 skip (live-gated).

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Ollama and Gemini multi-turn tool use (noted in the UsageGuide).
- Rename TechieDesk to Sevak in about 15 XML doc comments under `src/TechieRag`.
