---
project: TechieRag
last_updated: 2026-09-24
current_phase: Phase 2 of 2 · Build — 39 not built, 29 of 81 verified
last_verified_build: PASS
last_verified_date: 2026-09-03
---

# TechieRag — Status

## Where I am

Day-1 brownfield rewrite done 2026-09-24: the library is a Large, two-phase project (`docs/TechieRag-Phases.md`). Phase 1 (BRD-1…82) is the shipped core, all rows Done or N/A. Phase 2 (BRD-83…163) holds the agents package, the executed repository separation, four-platform groundwork, `TechieRag.Local`, typed streaming, subscription sign-in and 49 harvested v3 items. The application (Sevak) left this repository today; `apps/` is deleted and the library tests pass through the build ladder.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieRag
```
OpenCode:
```
/flow-master *build-phase TechieRag
```
Start with `REQ-FN-006` verify, then `REQ-RAG-067` (typed streaming) before `REQ-RAG-057…066`; `REQ-RAG-106`, `REQ-FN-065…067` are small scan-found fixes.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 38 |
| In Progress | 1 |
| Implemented | 11 |
| Needs re-verify | 1 |
| Blocked | 1 |

- [ ] REQ-FN-006 — repository separation, library side (Implemented; deletion done today, awaiting verify)
- [ ] REQ-RAG-067 — typed streaming events on `ILlmProvider` (prerequisite for `TechieRag.Local`)
- [ ] REQ-RAG-106 — pass the embedder's dimensions into every vector store (scan finding)
- [ ] REQ-FN-065 — pack and publish `TechieRag.Telemetry` (scan finding)
- [ ] REQ-FN-067 — one nuget.org publish path (scan finding)
- [ ] REQ-FN-066 — `AddTechieRag(IConfiguration)` maps every field (scan finding)
- [ ] REQ-RAG-015 / 016 / 017 — agentic contract and `TechieRag.Agents`
- [ ] REQ-RAG-057 — `TechieRag.Local`, `UseLocalLlm()` (after the runtime decision)
- [ ] REQ-RAG-076 — YouTube transcripts (Blocked, owner decision)
- [ ] REQ-RAG-088 — flow run time limit (In Progress)

## Known blockers

- 🔶 **Owner git:** the 2026-09-24 deletions and rewrites are uncommitted; `tests/TechieDesk.Tests/TestResults/res.trx` is still tracked (`git rm -r --cached 'tests/TechieDesk.Tests/TestResults'`).
- 🔶 **Runtime decision for `TechieRag.Local`** (owner, plan 08 step 3): LLamaSharp vs ONNX Runtime GenAI per platform, recorded in `DECISIONS.md` before `REQ-RAG-057…059` start.
- 🔶 **Real devices** (owner): a Mac, an Android phone and an iPhone for `REQ-FN-056/059/060`.
- 🔶 **`REQ-RAG-076` YouTube** (owner): timed-text endpoint returns empty bodies; choose an approach or drop it.
- 🔶 **`REQ-RAG-044` PostgreSQL** (owner): `TechieRagTestPostgres` needs a real server for the live tests.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-09-24 | `*day1-brownfield` TechieRag (Large, 2 phases; BRD-115…163 harvested; apps/ deleted) | 📝 docs rewritten; library tests PASS via build ladder (rung 2); no verify run | [P1](docs/TechieRag-Checklist.md#requirements-status) · [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-24 | `*amend-docs` TechieRag (BRD-81/87 amended; BRD-88…114 added) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*amend-docs` TechieRag + TechieDesk (BRD-83…87; governance reversal) | 📝 docs only | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |
| 2026-09-03 | `*triage-issues` → `*fix-issues` → verify (REQ-FN-003, REQ-FN-004) | ✅ 2/2 Verified; 723 lib tests pass; version rules 9/9 | [P1](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-08-04 | `*verify` ×6 (library rows migrated from the app ledger) | ✅ 2,306 pass; REQ-RAG-052 demoted | [P2](docs/TechieRag-P2-Checklist.md#requirements-status) |

## Library feedback summary

- Sevak (the former app) to TechieRag: 36 open · 7 closed, 0 blocking — `docs/Sevak-TechieRag-Feedback.md` (renamed 2026-09-24; its 43 legacy entries still fail the feedback template's per-entry rules)
- Downstream: Chatur TR-RAG-001/002 and MyDiary TR-RAG-001 accepted 2026-09-24 → BRD-96…114.

## Standards compliance

- Build ladder 2026-09-24: `PASS test on wsl via ~/.dotnet/dotnet (rung 2)` for `tests/TechieRag.Tests`; 41 warnings. Drift scan: 256/256 fields bare camelCase, 208/208 file-scoped namespaces. Doc checker: 0 FAIL on the eight rewritten documents.

## Deferred / future

- OS built-in models (Apple, Android Gemini Nano) as a later `ILocalLlmRuntime`.
- `REQ-RAG-046` deferred endpoints; `TechieRag.Agents` phase D items (proposal §7).
- Sevak free-tier limits (Sevak repository); agent skills as library tools (built app-side today).
