# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     and `tf-metrics.sh --phases .` (run 2026-10-06) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-10-06** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 62 live runs (1 more voided) | 2026-09-03 → 2026-10-06 |
| `gates.jsonl` | 585 (0 backfilled, 0 malformed) | 2026-09-03 → 2026-10-06 |
| `sessions.jsonl` | 14 (2 duplicate records merged) | 2026-09-03 → 2026-10-04 |
| `commits.jsonl` | 51 | 2025-12-30 → 2026-10-04 |
| `misses.jsonl` | 20 miss + 20 miss-fix | 2026-09-03 → 2026-10-06 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

**Since the last snapshot (2026-10-01).** The latest work on record is the 2026-10-06 fix of Sevak
feedback TR-RAG-048. The streams record it as a `fix-issues` run (started 2026-10-06T10:29:09Z), two
`verify-phase` re-checks and two short closing `fix-issues` runs. It is **not** recorded under the
command name `triage-and-fix`; the one `triage-and-fix` run on the stream is from 2026-10-01. The
2026-10-06 fix logged two escapes: **REQ-FN-071** (new row, found by `production`) and **REQ-FN-006** (was `Verified`,
demoted). Both have a `miss-fix` record ending in `Verified`. These appear in §2, §3, §5 and §6 below.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 91 | 88 | **97%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

Up from 85 scored / 83 first-pass on 2026-10-01. REQ-FN-071's first gate record is a `FAIL` (attempt 1,
`escaped`), so it is one of the three library REQs that did not pass first time.

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 5 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 5 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The library row grew from 3 to 5 with the two escapes of 2026-10-06
(REQ-FN-071, REQ-FN-006). The three newer checks (`perf` since 2026-08-10; `assets` and
`mockup-parity` since 2026-08-31) have run on 0 records in either row. This is a library with no
screens, so those checks do not apply. The tool does not break failure classes out for these rows.

---

## 3. Escape rate

*Of the requirements that had any failure, how many reached the owner or production instead of being
caught by a check.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 5 | 5 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

An escape is a `gate:"escaped"` record: a person found the defect after every check had passed it.
Three of the five library escapes had a `prior_verdict` of `Verified` when they escaped (REQ-RAG-069,
REQ-RAG-070 on 2026-10-01; REQ-FN-006 on 2026-10-06). That is the strongest signal on this page: the
verifier had signed those off. Read this beside §1: 88 of 91 library REQs passed first time, and every
failure on record was found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 62 (`verify-phase`=16, `build-phase`=15, `amend-docs`=10, `fix-issues`=6, `handoff-phase`=3, `log-miss`=3, `day1-brownfield`=1, `devguide`=1, `facilitate-brainstorming-session`=1, `metrics-report`=1, `probe-run`=1, `productguide`=1, `record-probe`=1, `triage-and-fix`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 100% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 29.16 |
| Sessions / total tokens | 14 / 6,728,882 |
| Tokens per `Verified` REQ | 11,641.7 |
| Commit cadence | 2.04 commits per active day (51 commits over 25 active days) |

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits were merged;
2 duplicate session records were merged, which is a normal union merge and needs no action.

The session token total (6,728,882, from `sessions.jsonl`) and the run output total in §6 (7,200,227,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 20 (1 open, 19 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 35% |
| Found by a human (`owner` / `production`) | 50% |

*This 50% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `owner` 9 · `agent-review` 8 · `self-smoke` 2 · `production` 1.

The `production` entry is new: MISS-TechieRag-20261006-01 on REQ-FN-071 (Sevak TR-RAG-048) is the first
miss in this repo found in a released package rather than by the owner or an agent. The one open miss
is MISS-TechieRag-20260924-06 (REQ-RAG-016); it has no `miss-fix` record.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 7 | 35% |
| `spec-contradiction` | 5 | 25% |
| `regression` | 4 | 20% |
| `wrong-behaviour` | 2 | 10% |
| `hallucinated-api` | 1 | 5% |
| `partial-implementation` | 1 | 5% |

The 2026-10-06 run added one `unspecified-gap` (REQ-FN-071) and one `regression` (REQ-FN-006).

**Why it was missed** — *which practice failed* (10 of 20 misses assessed)

| Practice | n | Share |
|---|---|---|
| `insufficient-verify-method` | 6 | 60% |
| `missing-checklist-item` | 4 | 40% |

0 misses predate this field, so all 20 could have carried it; 10 were simply not assessed (the field is
optional). **1 escape could have carried it and did not:** MISS-TechieRag-20260924-01 (REQ-RAG-076).
That is the most useful record to finish:
`bash .tfcore/utils/tf-emit.sh --amend MISS-TechieRag-20260924-01 why_missed <value>` (SCHEMA §5.5.7).
Never edit `misses.jsonl` by hand.

**Whose gap it was** (`sort`) — 10 of 17 misses sorted

| Sort | n | Share |
|---|---|---|
| `weak-check` — a check existed and was too weak | 6 | 60% |
| `spec` — the app's spec did not say it | 4 | 40% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

Both tables now lean toward the **checks**: `insufficient-verify-method` and `weak-check` lead. Both
2026-10-06 misses were sorted `weak-check`. None so far were rules that were written and ignored.

### 5a. Attribution — `linked` records only

**3 of 20 misses are attributed; 17 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts (n=3) |
|---|---|
| Origin phase | `build-phase`=3 |
| Origin agent | `techierag`=2, `general-purpose`=1 |
| Origin model | `claude-opus-5-5`=2, `claude-fable-5-1`=1 |

The third linked miss is the 2026-10-06 one on REQ-FN-006, traced to the `build-phase` run started
2026-09-24T14:58:03Z. REQ-FN-071's miss names `day1-greenfield`, which no run record backs, so it is one
of the 17 left out.

**These counts show what happened, not why.** Which model gets the hard work is not random, so a model
at the top of this list may be doing the hardest building rather than the worst. With 3 records this is
a question to investigate, not a ranking to route on. No per-phase, per-agent or per-model miss *rate*
is printed.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this REQ) | 0 | `insufficient data (n=0)` |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 9 | 48,933.3 |
| Unattributable (`none` — no usable token window) | 11 | — |

No fix run so far repaired exactly one miss, so there is no measured cost per miss. The apportioned
figure is a run's tokens split evenly across the misses it fixed. That is arithmetic, not a
measurement. 0 apportioned records lacked a token count.

The 2026-10-06 fix run added 3 of the 9 apportioned records, each labelled `shared:2`. REQ-FN-006 has
two of them (fix attempt 1 ended `Needs re-verify`, attempt 2 ended `Verified`), both from the same run
window. The tool averages per fix record, so that run's share appears twice in the 48,933.3 figure.

11 fixes have no usable token window (most were done inside longer runs). They count as misses but
cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **62 live run records** (the voided one is excluded). Token-window coverage: `tree 40` ·
`main 17` · `none 2` · `absent 3`. Wall clock is known for 57 of 62 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 15 | 13h27m / 15m56s | 2,333,174 | 32% | 15 of 15 runs |
| `amend-docs` | 10 | 2h12m / 9m08s | 2,219,625 | 31% | 10 of 10 runs |
| `handoff-phase` | 3 | 37m41s / 9m28s | 839,327 | 12% | 3 of 3 runs |
| `day1-brownfield` | 1 | 4h12m (one run) | 644,710 | 9% | 1 of 1 runs |
| `fix-issues` | 6 | 42m09s / 5m06s | 417,999 | 6% | 6 of 6 runs |
| `verify-phase` | 16 | 2h32m / 1m52s | 415,672 | 6% | 15 of 16 runs |
| `triage-issues` | 1 | 7m47s (one run) | 143,801 | 2% | 1 of 1 runs |
| `productguide` | 1 | 5m52s (one run) | 79,139 | 1% | 1 of 1 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s (one run) | 59,723 | 1% | 1 of 1 runs |
| `metrics-report` | 1 | 1m55s (one run) | 25,789 | 0% | 1 of 1 runs |
| `triage-and-fix` | 1 | 2m47s (one run) | 17,245 | 0% | 1 of 1 runs |
| `log-miss` | 3 | 1m06s / 33s (n=2) | 4,023 | 0% | 2 of 3 runs |
| `devguide` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `probe-run` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `record-probe` | 1 | — (no time recorded) | — | — | 0 of 1 runs |

Runs with no token window (`devguide` 1, `probe-run` 1, `record-probe` 1, `log-miss` 1, `verify-phase`
1) are left out of the token columns, never counted as zero. The `verify-phase` one is the 2026-10-06
re-check of REQ-FN-006 (`tokens_scope: none`). Phases with fewer than 3 runs show a single run's
figures, not a trend.

`build-phase` split by mode: **build** 7 runs, 11h34m, 1.7M out · **fix** 8 runs, 1h53m, 623.7k out.

The 2026-10-06 TR-RAG-048 fix is inside the `fix-issues` row: its main run took 15m31s and 51,882 output
tokens on `claude-opus-5-5` (one `main`-scope window, so fan-out was not observed).

A phase that runs another phase inline (for example the verifier chained inside a fix run) has an
overlapping token window. The tool adds up by `cmd` and does not remove overlaps, so the "% of all
output" column can count some output twice.

`build-phase` costing more than `log-miss` says what those phases *are*, not how well either ran.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `build-phase` | `claude-opus-5-5` | 1,840,819 | 79% | 11 |
| `build-phase` | `claude-fable-5-1` | 492,355 | 21% | 4 |
| `amend-docs` | `claude-fable-5-1` | 2,015,304 | 91% | 5 |
| `amend-docs` | `claude-opus-5-5` | 204,321 | 9% | 5 |
| `handoff-phase` | `claude-fable-5-1` | 711,212 | 85% | 1 |
| `handoff-phase` | `claude-opus-5-5` | 128,115 | 15% | 2 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 52% | 1 |
| `fix-issues` | `claude-opus-5-5` | 200,586 | 48% | 5 |
| `verify-phase` | `claude-opus-5-5` | 363,913 | 88% | 11 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 12% | 4 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
| `facilitate-brainstorming-session` | `claude-fable-5-1` | 59,723 | 100% | 1 |
| `metrics-report` | `claude-opus-5-5` | 25,789 | 100% | 1 |
| `triage-and-fix` | `claude-opus-5-5` | 17,245 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 90% | 1 |
| `log-miss` | `claude-opus-5-5` | 421 | 10% | 1 |

`build-phase` and `day1-brownfield` each show one run with a `<synthetic>` model entry and 0 output
tokens; it adds nothing and is left out of the table. Every run since 2026-10-01 that recorded a model used `claude-opus-5-5`.

**This shows what happened, not why.** Which model gets which phase is not random, so a cost
difference between models here says at least as much about *the work they were given* as about the
models. Routing was observed, never enforced: 0 runs were on their planned tier; 18 drifted and 44 had
no tier to compare against.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share | Declared vs measured |
|---|---|---|---|---|---|---|
| `build-phase` | 13 of 15 | 18 / 1 / 7 | 7 | 1,407,683 | 69% | declared 10, measured 18 |
| `handoff-phase` | 2 of 3 | 9 / 4.5 / 8 | 2 | 370,979 | 48% | declared 5, measured 9 |
| `verify-phase` | 11 of 16 | 5 / 0 / 5 | 1 | 199,380 | 54% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `metrics-report` | 1 of 1 | 1 / 1 / 1 | 1 | 11,889 | 46% | declared 0, measured 1 |
| `fix-issues` | 1 of 6 | 1 / 1 / 1 | 1 | 4,233 | 2% | declared 5*, measured 1 |
| `triage-and-fix` | 1 of 1 | 1 / 1 / 1 | 1 | 1,398 | 8% | declared 1, measured 1 (agree) |
| `amend-docs` | 7 of 10 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `log-miss` | 2 of 3 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `triage-issues` | 0 of 1 | — | — | — | — | — |
| `facilitate-brainstorming-session` | 0 of 1 | — | — | — | — | — |
| `devguide` | 0 of 1 | — | — | — | — | declared 1, not observed |
| `probe-run` | 0 of 1 | — | — | — | — | — |
| `record-probe` | 0 of 1 | — | — | — | — | — |

**Read the "Runs observed" column first.** Fan-out can only be seen on a `tree`-scope run. On a
`main`-scope run nobody looked at the subagents, so `0` there means *not looked*, not *none ran*. Those
runs are outside every figure in this table: 22 runs in all, every one because it was not `tree` scope;
0 because they predate the field.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in
`build-phase`, `handoff-phase` and `day1-brownfield` more subagents ran than were declared, and
`verify-phase` (5) and `metrics-report` (1) ran subagents they never declared.

\* The `fix-issues` "declared 5" is not five subagent kinds. Two runs typed prose into the field
(`none`, and on 2026-10-06 a note that the fix was done inline, split by the tool into three entries).
The only real kind declared is `general-purpose`=1. That is itself a self-reporting finding: the field
takes agent kinds, not a description of the work.

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- Miss attribution — 3 of 20 misses are `linked`; 17 name a phase no run record backs (REQ-FN-071's among them). Counts are shown; no rate is.
- Measured (`sole`) rework cost — `insufficient data (n=0)`; every costed fix shared its run with other misses.
- `why_missed` — assessed on 10 of 20 misses; 1 escape (MISS-TechieRag-20260924-01) is missing it and should be completed with `--amend`.
- `sort` — on 10 of 17 eligible misses; 3 predate the field.
- `devguide`, `probe-run`, `record-probe` runs — no wall clock and no token window; excluded from §6 time and token figures. One `log-miss` and one `verify-phase` run also have no token window.
- The 2026-10-06 TR-RAG-048 fix ran on `main`-scope windows only, so its subagent use (if any) was not observed.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
