# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report .` and
     `--report . --json` (run 2026-11-12, after the REQ-RAG-158 live verify and before the handoff) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-11-12** · schema v1 · refreshed for the handoff of the Sevak TR-RAG-061 to TR-RAG-063 fixes (1.1.10)

| Stream | Records |
|---|---|
| `runs.jsonl` | 131 live runs (2 more voided) |
| `gates.jsonl` | 889 (0 backfilled) |
| `sessions.jsonl` | 26 sessions (8 duplicate session ids collapsed) |
| `commits.jsonl` | 86 commits over 39 active days |
| `misses.jsonl` | 44 misses, 44 fix records |

**This repo is counted as two kinds of project.** `core-config.yaml` now says `app`; most records were written while it said `library`. The streams are never rewritten, so the two periods are reported in separate rows and never added together. TechieRag is a library; the `app` classification looks like a configuration slip worth checking in `core-config.yaml` (`metrics.project_type`).

**Two runs are left out of every figure**, each named void by a later record: a `build-phase` of 2026-09-25 (start time guessed) and a `fix-issues` of 2026-10-29 (superseded by its full record). Both stay on the stream.

## Since the last snapshot (2026-10-15)

- Phase 3 (Sevak's requests TR-RAG-055 to TR-RAG-058) was built, verified and handed off on 2026-11-08.
- **2026-11-11, this run.** `triage-and-fix` logged three escapes from Sevak's production report of 9 November on TechieRag 1.1.9: REQ-RAG-158 (TR-RAG-063), REQ-RAG-159 (TR-RAG-062), REQ-RAG-160 (TR-RAG-061), each a new row (`spec`: no row had covered it). The chained verify graded REQ-RAG-159 and REQ-RAG-160 `Verified`; REQ-RAG-158 stayed `Implemented` that day because its live test skipped (no Anthropic key).
- **2026-11-12.** A verify with no key changed nothing; once the owner added the key, the live call to `claude-sonnet-5-5` passed and REQ-RAG-158 became `Verified`. MISS-TechieRag-20261111-01 is closed with a fix record carrying no cost of its own (the repair was in the 11 November run). Phase 1 now has 27 of 27 rows Verified.

## 1. First-pass rate (live records only)

| project_type | records | REQs scored | first-pass rate |
|---|---|---|---|
| `library` (most of the history) | 879 | 133 | 89% |
| `app` (records since the classification changed) | 10 | 5 | 0% |

The two rows are periods of one project and are not combined. No backfilled records exist.

## 2. Gate catch distribution and escape rate (`gates.jsonl`)

| project_type | failures | caught by a gate | escaped | escape rate |
|---|---|---|---|---|
| `library` | 23 | 0 | 23 | 100% |
| `app` | 5 | 0 | 5 | 100% |

Every failure on record here was found by a person or a later run rather than by a gate. The perf, assets and mockup-parity gates have not run on this data (a library has no screens); the test-users gate ran on 193 `library` records and caught 0, and on 2 `app` records (insufficient data, n=2).

## 3. Misses (`misses.jsonl`)

- **44 logged**: 2 open, 42 resolved, 0 won't-fix. Found by: owner 20, production 13, agent review 9, own smoke 2. Found by a person: 75% (reported beside the escape rate above, never merged with it).
- **Classes:** unspecified gap 23 (52%), regression 10 (23%), spec contradiction 5 (11%), wrong behaviour 3 (7%), partial implementation 2 (5%), invented API 1 (2%).
- **Why it was missed** (31 of 44 assessed): missing checklist item 19 (61%), verify method too weak 12 (39%). 3 escapes carry no reason; complete them with `tf-emit.sh --amend <miss_id> why_missed <value>`.
- **Whose gap** (33 of 41 sorted): the app's spec 19 (58%), a weak check 13 (39%), the framework never said it 1 (3%). 3 misses predate the field.

**Attribution — linked records only (8 of 44; 36 excluded because no run record backs the phase they name).** By origin phase: build-phase 7, fix-issues 1. By origin model: claude-opus-5-5 6, claude-fable-5-1 2. This is observational, not causal: which model gets the harder work is not random.

**Discovery cost per miss:** insufficient data (n=2 measured; 2 not measured; 40 predate the field). This run: REQ-RAG-159 and REQ-RAG-160 cost 554 output tokens each to find; REQ-RAG-158's window was not measurable.

**Rework cost per miss (output tokens):**

| measured (`sole`) | apportioned (`shared`), equal division, not a measurement | unattributable |
|---|---|---|
| 60,666 per miss (n=5) | 40,713 per miss (n=26) | 13 records with no token window |

This run's three fixes are apportioned: 60.1k output tokens each (one third of the run). No dollars are measured: Claude Code records carry no cost, and none is invented from a rate card.

## 4. Effort per phase (`runs.jsonl`)

Token figures cover only runs whose window was measured; fan-out only `tree`-scope runs. Phases differ in what they are, so a costlier phase is a fact about the phase, not a finding.

| phase | runs | wall clock | output tokens | share of output |
|---|---|---|---|---|
| build-phase | 22 | 16h03m | 3.3M | 33% |
| amend-docs | 18 | 4h34m | 2.5M | 25% |
| fix-issues | 21 | 4h06m | 941.0k | 9% |
| handoff-phase | 5 | 47m20s | 922.1k | 9% |
| triage-and-fix | 10 | 1h16m | 668.2k | 7% |
| day1-brownfield | 1 | 4h12m | 644.7k | 6% |
| verify-phase | 36 | 5h19m | 538.5k | 5% |
| other (devguide, metrics-report, log-miss, triage-issues, productguide, brainstorming, file-feedback, probes) | 18 | — | 0.5M | 5% |

Token windows: 70 `tree`, 47 `main`, 7 `none`, 3 absent. `triage-and-fix`: 10 runs, all on claude-opus-5-5; fan-out observed on 2 of 10 runs (2 spawns); the agents declared 6, the harness counted 2, and the counted figure is the right one.

**Poolable figures:** rework ratio 177% (fix-mode runs per build-phase run); median 2 rows per build-phase run; 11,140,171 tokens over 26 sessions; 12,939 tokens per Verified row; list price $1,125.69 over 121 priced runs (the published rate applied to measured tokens, a price, not a bill).

## 5. Estimate against actual

No estimates yet: run `*estimate-phase TechieRag` before a `*build-phase` to start this column.

## What is missing

- No phase has an estimate, so question 6 has nothing to compare.
- 36 of 44 misses cannot be attributed to a model, and 3 escapes lack a reason.
- The commit hook is installed on this clone (`commit_hook: true`).
