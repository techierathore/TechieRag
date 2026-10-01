# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     (run 2026-10-01) and are not recomputed by hand. No combined first-pass rate,
     gate distribution, escape rate, miss rate, or cost-per-miss across
     live/backfilled, across project_type, across attribution confidence, or across
     cost attribution. -->

**Snapshot as of 2026-10-01** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 39 live runs (1 more voided) | 2026-09-03 → 2026-10-01 |
| `gates.jsonl` | 407 (0 backfilled) | 2026-09-03 → 2026-10-01 |
| `sessions.jsonl` | 8 | 2026-09-03 → 2026-09-25 |
| `commits.jsonl` | 46 | 2025-12-30 → 2026-09-25 |
| `misses.jsonl` | 17 miss + 16 miss-fix | 2026-09-03 → 2026-10-01 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 85 | 83 | **98%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 3 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 3 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The three newer checks (`perf` since 2026-08-10; `assets` and
`mockup-parity` since 2026-08-31) have run on 0 records in either row. This is a library with no
screens, so those checks do not apply. The tool does not break failure classes out for these rows.

---

## 3. Escape rate

*Of the requirements that had any failure, how many reached the owner or production instead of being
caught by a check.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 3 | 3 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

An escape is a `gate:"escaped"` record written by `*triage-issues`: a person found the defect after
every check had passed it. Read this beside §1: 83 of 85 library REQs passed first time, and the few
that failed were all found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 39 (`build-phase`=13, `verify-phase`=9, `amend-docs`=6, `fix-issues`=3, `log-miss`=2, `day1-brownfield`=1, `devguide`=1, `facilitate-brainstorming-session`=1, `handoff-phase`=1, `productguide`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 77% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 34.29 |
| Sessions / total tokens | 8 / 5,680,817 |
| Tokens per `Verified` REQ | 14,131.4 |
| Commit cadence | 2.09 commits per active day (46 commits over 22 active days) |

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits or sessions
were merged.

The session token total (5,680,817, from `sessions.jsonl`) and the run output total in §6 (6,649,180,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 17 (1 open, 16 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 35% |
| Found by a human (`owner` / `production`) | 41% |

*This 41% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `agent-review` 8 · `owner` 7 · `self-smoke` 2.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 6 | 35% |
| `spec-contradiction` | 5 | 29% |
| `regression` | 3 | 18% |
| `hallucinated-api` | 1 | 6% |
| `partial-implementation` | 1 | 6% |
| `wrong-behaviour` | 1 | 6% |

**Why it was missed** — *which practice failed* (7 of 17 misses assessed)

| Practice | n | Share |
|---|---|---|
| `insufficient-verify-method` | 4 | 57% |
| `missing-checklist-item` | 3 | 43% |

0 misses predate this field, so all 17 could have carried it; 10 were simply not assessed (the field is
optional). **1 escape could have carried it and did not.** That is the most useful record to finish:
`bash .tfcore/utils/tf-emit.sh --amend <miss_id> why_missed <value>` (SCHEMA §5.5.7). Never edit
`misses.jsonl` by hand.

**Whose gap it was** (`sort`) — 7 of 14 misses sorted

| Sort | n | Share |
|---|---|---|
| `spec` — the app's spec did not say it | 4 | 57% |
| `weak-check` — a check existed and was too weak | 3 | 43% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

Both tables point the same way: roughly half the gaps were in the **spec** (nothing said it) and half in
the **checks** (a check existed but did not catch it). None so far were rules that were written and
ignored.

### 5a. Attribution — `linked` records only

**2 of 17 misses are attributed; 15 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts |
|---|---|
| Origin phase | `insufficient data (n=2)` |
| Origin agent | `insufficient data (n=2)` |
| Origin model | `insufficient data (n=2)` |

No per-phase, per-agent or per-model miss rate is printed. Even when there is enough data, such a rate
only shows what happened, not why: which model gets the hard work is not random.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this REQ) | 0 | `insufficient data (n=0)` |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 6 | 60,429.5 |
| Unattributable (`none` — no usable token window) | 10 | — |

No fix run so far repaired exactly one miss, so there is no measured cost per miss. The apportioned
figure is one run's tokens split evenly across the misses it fixed. That is arithmetic, not a
measurement. 10 fixes were done inside longer runs with no separate fix record; they count as misses
but cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **39 live run records** (the voided one is excluded). Token-window coverage: `tree 28` ·
`main 9` · `none 1` · `absent 1`. Wall clock is known for 37 of 39 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 13 | 13h07m / 16m55s | 2,232,998 | 34% | 13 of 13 runs |
| `amend-docs` | 6 | 1h12m / 9m05s | 2,048,712 | 31% | 6 of 6 runs |
| `handoff-phase` | 1 | 24m16s / 24m16s | 711,212 | 11% | 1 of 1 runs |
| `day1-brownfield` | 1 | 4h12m / 4h12m | 644,710 | 10% | 1 of 1 runs |
| `fix-issues` | 3 | 26m25s / 10m05s | 363,462 | 5% | 3 of 3 runs |
| `verify-phase` | 9 | 1h37m / 1m37s | 361,821 | 5% | 9 of 9 runs |
| `triage-issues` | 1 | 7m47s / 7m47s | 143,801 | 2% | 1 of 1 runs |
| `productguide` | 1 | 5m52s / 5m52s | 79,139 | 1% | 1 of 1 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s / 52m11s | 59,723 | 1% | 1 of 1 runs |
| `log-miss` | 2 | 1m03s / 1m03s (n=1) | 3,602 | 0% | 1 of 2 runs |
| `devguide` | 1 | — (no time recorded) | — | — | 0 of 1 runs |

Runs with no token window (`devguide` 1, `log-miss` 1) are left out of the token columns, never
counted as zero. Phases with fewer than 3 runs show a single run's figures, not a trend.

`build-phase` split by mode: **build** 6 runs, 11h18m, 1.6M out · **fix** 7 runs, 1h48m, 607.9k out.

A phase that runs another phase inline (for example the verifier chained inside a fix run) has an
overlapping token window. The tool adds up by `cmd` and does not remove overlaps, so the "% of all
output" column can count some output twice.

`build-phase` costing more than `log-miss` says what those phases *are*, not how well either ran.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `build-phase` | `claude-opus-5-5` | 1,740,643 | 78% | 9 |
| `build-phase` | `claude-fable-5-1` | 492,355 | 22% | 4 |
| `amend-docs` | `claude-fable-5-1` | 2,015,304 | 98% | 5 |
| `amend-docs` | `claude-opus-5-5` | 33,408 | 2% | 1 |
| `handoff-phase` | `claude-fable-5-1` | 711,212 | 100% | 1 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 60% | 1 |
| `fix-issues` | `claude-opus-5-5` | 146,049 | 40% | 2 |
| `verify-phase` | `claude-opus-5-5` | 310,062 | 86% | 5 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 14% | 4 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
| `facilitate-brainstorming-session` | `claude-fable-5-1` | 59,723 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 100% | 1 |

`build-phase` and `day1-brownfield` each show one run with a `<synthetic>` model entry and 0 output
tokens; it adds nothing and is left out of the table.

**This shows what happened, not why.** Which model gets which phase is not random, so a cost
difference between models here says at least as much about *the work they were given* as about the
models. Routing was observed, never enforced: 0 runs were on their planned tier; 18 drifted and 21 had
no tier to compare against.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share | Declared vs measured |
|---|---|---|---|---|---|---|
| `build-phase` | 11 of 13 | 17 / 1 / 7 | 6 | 1,336,859 | 69% | declared 9, measured 17 |
| `handoff-phase` | 1 of 1 | 8 / 8 / 8 | 1 | 351,609 | 49% | declared 4, measured 8 |
| `verify-phase` | 7 of 9 | 5 / 0 / 5 | 1 | 199,380 | 58% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `fix-issues` | 1 of 3 | 1 / 1 / 1 | 1 | 4,233 | 2% | declared 2, measured 1 |
| `amend-docs` | 5 of 6 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `log-miss` | 1 of 2 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `triage-issues` | 0 of 1 | — | — | — | — | — |
| `facilitate-brainstorming-session` | 0 of 1 | — | — | — | — | — |
| `devguide` | 0 of 1 | — | — | — | — | declared 1, not observed |

**Read the "Runs observed" column first.** Fan-out can only be seen on a `tree`-scope run. On a
`main`-scope run nobody looked at the subagents, so `0` there means *not looked*, not *none ran*. Those
runs are outside every figure in this table: 11 runs in all, every one because it was not `tree` scope;
0 because they predate the field.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in four
phases more subagents ran than were declared (by about 2× in `build-phase` and `handoff-phase`, and 5
undeclared in `verify-phase`).

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- Miss attribution by phase / agent / model — `insufficient data (n=2)`; 15 of 17 misses name a phase no run record backs.
- Measured (`sole`) rework cost — `insufficient data (n=0)`; every costed fix shared its run with other misses.
- `why_missed` — assessed on 7 of 17 misses; 1 escape is missing it and should be completed with `--amend`.
- `sort` — on 7 of 14 eligible misses; 3 predate the field.
- `devguide` run — no wall clock and no token window; excluded from §6.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
