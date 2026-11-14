# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report .` and
     `--report . --json` (run 2026-11-13, inside the triage-and-fix run for REQ-RAG-161 to REQ-RAG-163) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-11-13** · schema v1 · refreshed inside the second `triage-and-fix` run of the day (Sevak TR-RAG-064, 1.1.10)

| Stream | Records |
|---|---|
| `runs.jsonl` | 139 live runs (4 more voided) |
| `gates.jsonl` | 898 (0 backfilled) |
| `sessions.jsonl` | 27 sessions (8 duplicate session ids collapsed) |
| `commits.jsonl` | 87 commits over 40 active days |
| `misses.jsonl` | 48 misses, 49 fix records |

**This repo is counted as two kinds of project.** `core-config.yaml` says `library`; ten records were written while it said `app`. The streams are never rewritten, so the two periods are reported in separate rows and never added together.

**Four runs are left out of every figure**, each named void by a later record: a `build-phase` of 2026-09-25 (start time guessed), a `fix-issues` of 2026-10-29 (superseded by its full record), a `metrics-report` of 2026-11-11 (written with the handoff's start time), and a `triage-and-fix` tail of 2026-11-13 06:41:41 (written by a fix close before this run's misses were logged; the close that followed carries the fix records). All stay on the stream.

## Since the last snapshot (2026-11-12)

- **2026-11-13, this run.** `triage-and-fix` logged three escapes, each a new row (`spec`: no row had covered it): REQ-RAG-161 (Sevak TR-RAG-060, production), REQ-RAG-162 (Sevak TR-RAG-059, production) and REQ-RAG-163 (AstroLyfe TR-RAG-001, production). All three were fixed and graded `Verified` by the chained verify, with REQ-RAG-161 run against a real PostgreSQL 18 + pgvector 0.8.6. Phase 1 then had 30 of 30 rows Verified.
- **2026-11-13, second run.** `triage-and-fix` logged one more escape from Sevak's production report: REQ-RAG-164 (TR-RAG-064, no price for current Claude models), a new row (`spec`). Fixed and `Verified` (36 tests); its fix is the only record of the day measured on its own (116.4k output tokens, `sole`). An `amend-docs` run then folded REQ-RAG-158 to REQ-RAG-164 into the phase-1 BRD as BRD-216 to BRD-222. Phase 1 has 31 of 31 rows Verified.

## 1. First-pass rate (live records only)

| project_type | records | REQs scored | first-pass rate |
|---|---|---|---|
| `library` | 888 | 137 | 89% |
| `app` (an earlier period of the same project) | 10 | 5 | 0% |

The two rows are periods of one project and are not combined. No backfilled records exist.

## 2. Gate catch distribution and escape rate (`gates.jsonl`)

| project_type | failures | caught by a gate | escaped | escape rate |
|---|---|---|---|---|
| `library` | 27 | 0 | 27 | 100% |
| `app` | 5 | 0 | 5 | 100% |

Every failure on record here was found by a person or a later run rather than by a gate. The perf, assets and mockup-parity gates have not run on this data (a library has no screens); the test-users gate ran on 198 `library` records and caught 0, and on 3 `app` records (insufficient data, n=3).

## 3. Misses (`misses.jsonl`)

- **48 logged**: 2 open, 46 resolved, 0 won't-fix. Found by: owner 20, production 17, agent review 9, own smoke 2. Found by a person: 77% (reported beside the escape rate above, never merged with it).
- **Classes:** unspecified gap 27 (56%), regression 10 (21%), spec contradiction 5 (10%), wrong behaviour 3 (6%), partial implementation 2 (4%), invented API 1 (2%).
- **Why it was missed** (35 of 48 assessed): missing checklist item 23 (66%), verify method too weak 12 (34%). 3 escapes carry no reason; complete them with `tf-emit.sh --amend <miss_id> why_missed <value>`.
- **Whose gap** (37 of 45 sorted): the app's spec 23 (62%), a weak check 13 (35%), the framework never said it 1 (3%). 3 misses predate the field.

**Attribution — linked records only (8 of 48; 40 excluded because no run record backs the phase they name).** By origin phase: build-phase 7, fix-issues 1. By origin model: claude-opus-5-5 6, claude-fable-5-1 2. This is observational, not causal: which model gets the harder work is not random.

**Discovery cost per miss:** 3,366 output tokens, 0.2 min (n=4 measured; 4 not measured; 40 predate the field). This run: REQ-RAG-161 cost 11.8k output tokens to find, REQ-RAG-162 599; REQ-RAG-163's window was not measurable (its mark was set after the investigation), nor was REQ-RAG-164's.

**Rework cost per miss (output tokens):**

| measured (`sole`) | apportioned (`shared`), equal division, not a measurement | unattributable |
|---|---|---|
| 69,957 per miss (n=6) | 44,506 per miss (n=30) | 13 records with no token window |

The morning run's three fixes are apportioned: 72.2k output tokens each (one third of the run). The second run's one fix, REQ-RAG-164, is measured on its own: 116.4k output tokens. No dollars are measured: Claude Code records carry no cost, and none is invented from a rate card.

## 4. Effort per phase (`runs.jsonl`)

Token figures cover only runs whose window was measured; fan-out only `tree`-scope runs. Phases differ in what they are, so a costlier phase is a fact about the phase, not a finding.

| phase | runs | wall clock | output tokens | share of output |
|---|---|---|---|---|
| build-phase | 22 | 16h03m | 3.3M | 32% |
| amend-docs | 19 | 4h37m | 2.5M | 24% |
| fix-issues | 21 | 4h06m | 941.0k | 9% |
| handoff-phase | 6 | 9h26m | 935.1k | 9% |
| triage-and-fix | 14 | 2h10m | 1.0M | 10% |
| day1-brownfield | 1 | 4h12m | 644.7k | 6% |
| verify-phase | 38 | 5h27m | 540.3k | 5% |
| other (metrics-report, devguide, log-miss, triage-issues, productguide, brainstorming, file-feedback, probes) | 18 | — | 0.5M | 5% |

Token windows: 74 `tree`, 55 `main`, 7 `none`, 3 absent. `triage-and-fix`: 14 runs, all on claude-opus-5-5; fan-out observed on 2 of 12 runs (2 spawns); the agents declared 7, the harness counted 2, and the counted figure is the right one. The second run spawned one sub-agent, to read Anthropic's pricing page.

**Poolable figures:** rework ratio 195% (fix-mode runs per build-phase run); median 2 rows per build-phase run; 11,487,555 tokens over 27 sessions; 13,265 tokens per Verified row; list price $1,157.66 over 129 priced runs (the published rate applied to measured tokens, a price, not a bill).

## 5. Estimate against actual

No estimates yet: run `*estimate-phase TechieRag` before a `*build-phase` to start this column.

## What is missing

- No phase has an estimate, so question 6 has nothing to compare.
- 40 of 48 misses cannot be attributed to a model, and 3 escapes lack a reason.
- The commit hook is installed on this clone (`commit_hook: true`).
