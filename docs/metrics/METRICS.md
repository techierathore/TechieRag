# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     (run 2026-10-07T17:44Z) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-10-07** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 75 live runs (1 more voided) | 2026-09-03 → 2026-10-07 |
| `gates.jsonl` | 604 (0 backfilled, 0 malformed) | 2026-09-03 → 2026-10-07 |
| `sessions.jsonl` | 15 (3 duplicate records merged) | 2026-09-03 → 2026-10-07 |
| `commits.jsonl` | 53 | 2025-12-30 → 2026-10-07 |
| `misses.jsonl` | 24 miss + 24 miss-fix | 2026-09-03 → 2026-10-07 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

**Since the last snapshot (2026-10-06).** Two more fixes of Sevak feedback are on the streams.
On 2026-10-06 evening a `fix-issues` run re-opened **REQ-RAG-119** and **REQ-RAG-116**, both `Verified`
before (found by `owner`). On 2026-10-07 this `triage-and-fix` run took **REQ-RAG-122** (Sevak TR-RAG-049,
was `Verified`, demoted) and **REQ-RAG-125** (Sevak TR-RAG-047, a new row). Both were found by
`production`. All four have a `miss-fix` record ending in `Verified`. The 2026-10-07 run's own
`triage-and-fix` record is written by the status gate after this report, so it is not counted below yet;
its chained `verify-phase` and `fix-issues` records are.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 103 | 99 | **96%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

Up from 91 scored / 88 first-pass on 2026-10-06. REQ-RAG-125's first gate record is a `FAIL` (attempt 1,
`escaped`), so it is one of the four library REQs that did not pass first time. REQ-RAG-116, REQ-RAG-119
and REQ-RAG-122 did pass first time; their escapes came on a later attempt and count in §2 and §3.

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 9 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 9 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The library row grew from 5 to 9 (REQ-RAG-116 and REQ-RAG-119 on
2026-10-06; REQ-RAG-122 and REQ-RAG-125 on 2026-10-07). The three newer checks (`perf` since 2026-08-10;
`assets` and `mockup-parity` since 2026-08-31) have run on 0 records in either row. This is a library
with no screens, so those checks do not apply.

---

## 3. Escape rate

*Of the requirements that had any failure, how many reached the owner or production instead of being
caught by a check.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 9 | 9 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

An escape is a `gate:"escaped"` record: a person found the defect after every check had passed it.
**Six of the nine library escapes had a `prior_verdict` of `Verified`:** REQ-RAG-069 and REQ-RAG-070
(2026-10-01), REQ-FN-006 (2026-10-06), REQ-RAG-116 and REQ-RAG-119 (2026-10-06), and REQ-RAG-122
(2026-10-07). That is the strongest signal on this page: the verifier had signed those off. REQ-RAG-122's
case is typical. Its test proved the default database location and never compiled the call shape an
existing consumer used. The new public-overload test added in this run is a check aimed at exactly that
kind of gap. Read this beside §1: 99 of 103 library REQs passed first time, and every failure on record
was found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 75 (`verify-phase`=19, `build-phase`=16, `amend-docs`=11, `fix-issues`=11, `handoff-phase`=4, `log-miss`=3, `triage-and-fix`=2, `day1-brownfield`=1, `devguide`=1, `facilitate-brainstorming-session`=1, `file-feedback`=1, `metrics-report`=1, `probe-run`=1, `productguide`=1, `record-probe`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 131% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 27.9 |
| Sessions / total tokens | 15 / 7,245,209 |
| Tokens per `Verified` REQ | 12,217.9 |
| Commit cadence | 2.04 commits per active day (53 commits over 26 active days) |

**Rework passed build work this week.** The ratio went from 100% to 131%: there are now more fix-mode
runs (`fix-issues`, `triage-and-fix`, `build-phase` in fix mode) than `build-phase` runs. Every one of
the recent fix runs answered a consumer's feedback file (Sevak).

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits were merged;
3 duplicate session records were merged, which is a normal union merge and needs no action.

The session token total (7,245,209, from `sessions.jsonl`) and the run output total in §6 (7,799,254,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 24 (1 open, 23 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 33% |
| Found by a human (`owner` / `production`) | 58% |

*This 58% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `owner` 11 · `agent-review` 8 · `production` 3 · `self-smoke` 2.

The `production` count went from 1 to 3 with MISS-TechieRag-20261007-01 (REQ-RAG-122, Sevak TR-RAG-049)
and MISS-TechieRag-20261007-02 (REQ-RAG-125, Sevak TR-RAG-047). All three `production` misses came from
one consumer, Sevak, moving to a new package version. The one open miss is still
MISS-TechieRag-20260924-06 (REQ-RAG-016); it has no `miss-fix` record.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 8 | 33% |
| `regression` | 7 | 29% |
| `spec-contradiction` | 5 | 21% |
| `wrong-behaviour` | 2 | 8% |
| `hallucinated-api` | 1 | 4% |
| `partial-implementation` | 1 | 4% |

Since 2026-10-06: three `regression` (REQ-RAG-116, REQ-RAG-119, REQ-RAG-122) and one `unspecified-gap`
(REQ-RAG-125). `regression` rose from 20% to 29%.

**Why it was missed** — *which practice failed* (14 of 24 misses assessed)

| Practice | n | Share |
|---|---|---|
| `insufficient-verify-method` | 9 | 64% |
| `missing-checklist-item` | 5 | 36% |

0 misses predate this field, so all 24 could have carried it; 10 were simply not assessed (the field is
optional). **1 escape could have carried it and did not:** MISS-TechieRag-20260924-01 (REQ-RAG-076).
That is the most useful record to finish:
`bash .tfcore/utils/tf-emit.sh --amend MISS-TechieRag-20260924-01 why_missed <value>` (SCHEMA §5.5.7).
Never edit `misses.jsonl` by hand.

**Whose gap it was** (`sort`) — 14 of 21 misses sorted

| Sort | n | Share |
|---|---|---|
| `weak-check` — a check existed and was too weak | 9 | 64% |
| `spec` — the app's spec did not say it | 5 | 36% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

Both tables still lean toward the **checks**: `insufficient-verify-method` and `weak-check` lead. Three of
the four newest misses were sorted `weak-check`; REQ-RAG-125 is `spec` (no row covered it). None so far
were rules that were written and ignored.

### 5a. Attribution — `linked` records only

**6 of 24 misses are attributed; 18 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts (n=6) |
|---|---|
| Origin phase | `build-phase`=6 |
| Origin agent | `general-purpose`=4, `techierag`=2 |
| Origin model | `claude-opus-5-5`=5, `claude-fable-5-1`=1 |

The three new linked misses are REQ-RAG-116, REQ-RAG-119 and REQ-RAG-122. REQ-RAG-125's miss names
`day1-greenfield` (`inferred`), which no run record backs, so it is one of the 18 left out.

**These counts show what happened, not why.** Which model gets the hard work is not random, so a model
at the top of this list may be doing the hardest building rather than the worst. With 6 records this is
a question to investigate, not a ranking to route on. No per-phase, per-agent or per-model miss *rate*
is printed.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this REQ) | 0 | `insufficient data (n=0)` |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 13 | 44,039.8 |
| Unattributable (`none` — no usable token window) | 11 | — |

No fix run so far repaired exactly one miss, so there is no measured cost per miss. The apportioned
figure is a run's tokens split evenly across the misses it fixed. That is arithmetic, not a
measurement. 0 apportioned records lacked a token count.

This run added 2 of the 13 apportioned records (REQ-RAG-122 and REQ-RAG-125, each `shared:2`, 98,641
output tokens on the run window). The 2026-10-06 evening fix added 2 more (REQ-RAG-116 and REQ-RAG-119,
each `shared:2`, 33,477).

11 fixes have no usable token window (most were done inside longer runs). They count as misses but
cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **75 live run records** (the voided one is excluded). Token-window coverage: `tree 50` ·
`main 19` · `none 3` · `absent 3`. Wall clock is known for 70 of 75 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 16 | 13h52m / 16m25s | 2,466,029 | 32% | 16 of 16 runs |
| `amend-docs` | 11 | 3h47m / 11m43s | 2,287,601 | 29% | 11 of 11 runs |
| `handoff-phase` | 4 | 45m04s / 8m25s | 899,325 | 12% | 4 of 4 runs |
| `fix-issues` | 11 | 2h28m / 10m05s | 663,964 | 9% | 10 of 11 runs |
| `day1-brownfield` | 1 | 4h12m (one run) | 644,710 | 8% | 1 of 1 runs |
| `verify-phase` | 19 | 2h59m / 1m49s (n=18) | 421,629 | 5% | 18 of 19 runs |
| `triage-issues` | 1 | 7m47s (one run) | 143,801 | 2% | 1 of 1 runs |
| `triage-and-fix` | 2 | 5m46s / 2m53s | 86,580 | 1% | 2 of 2 runs |
| `productguide` | 1 | 5m52s (one run) | 79,139 | 1% | 1 of 1 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s (one run) | 59,723 | 1% | 1 of 1 runs |
| `metrics-report` | 1 | 1m55s (one run) | 25,789 | 0% | 1 of 1 runs |
| `file-feedback` | 1 | 28m57s (one run) | 16,941 | 0% | 1 of 1 runs |
| `log-miss` | 3 | 1m06s / 33s (n=2) | 4,023 | 0% | 2 of 3 runs |
| `devguide` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `probe-run` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `record-probe` | 1 | — (no time recorded) | — | — | 0 of 1 runs |

Runs with no token window (`devguide` 1, `probe-run` 1, `record-probe` 1, `log-miss` 1, `fix-issues` 1,
`verify-phase` 1) are left out of the token columns, never counted as zero. Phases with fewer than 3 runs
show a single run's figures, not a trend.

`build-phase` split by mode: **build** 8 runs, 11h58m, 1.8M out · **fix** 8 runs, 1h53m, 623.7k out.

A phase that runs another phase inline (for example the verifier chained inside a fix run) has an
overlapping token window. The tool adds up by `cmd` and does not remove overlaps, so the "% of all
output" column can count some output twice.

`build-phase` costing more than `log-miss` says what those phases *are*, not how well either ran.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `build-phase` | `claude-opus-5-5` | 1,973,674 | 80% | 12 |
| `build-phase` | `claude-fable-5-1` | 492,355 | 20% | 4 |
| `amend-docs` | `claude-fable-5-1` | 2,015,304 | 88% | 5 |
| `amend-docs` | `claude-opus-5-5` | 272,297 | 12% | 6 |
| `handoff-phase` | `claude-fable-5-1` | 711,212 | 79% | 1 |
| `handoff-phase` | `claude-opus-5-5` | 188,113 | 21% | 3 |
| `fix-issues` | `claude-opus-5-5` | 446,551 | 67% | 9 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 33% | 1 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `verify-phase` | `claude-opus-5-5` | 369,870 | 88% | 14 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 12% | 4 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `triage-and-fix` | `claude-opus-5-5` | 86,580 | 100% | 2 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
| `facilitate-brainstorming-session` | `claude-fable-5-1` | 59,723 | 100% | 1 |
| `metrics-report` | `claude-opus-5-5` | 25,789 | 100% | 1 |
| `file-feedback` | `claude-opus-5-5` | 16,941 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 90% | 1 |
| `log-miss` | `claude-opus-5-5` | 421 | 10% | 1 |

`build-phase` and `day1-brownfield` each show one run with a `<synthetic>` model entry and 0 output
tokens; it adds nothing and is left out of the table.

**This shows what happened, not why.** Which model gets which phase is not random, so a cost
difference between models here says at least as much about *the work they were given* as about the
models. Routing was observed, never enforced: no run on record was on a planned tier.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share | Declared vs measured |
|---|---|---|---|---|---|---|
| `build-phase` | 14 of 16 | 19 / 1 / 7 | 8 | 1,521,699 | 70% | declared 11, measured 19 |
| `handoff-phase` | 3 of 4 | 10 / 1 / 8 | 3 | 398,955 | 48% | declared 6, measured 10 |
| `verify-phase` | 13 of 19 | 5 / 0 / 5 | 1 | 199,380 | 53% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `fix-issues` | 4 of 11 | 4 / 0.5 / 3 | 2 | 35,217 | 10% | declared 4 `general-purpose` + prose*, measured 4 |
| `triage-and-fix` | 2 of 2 | 2 / 1 / 1 | 2 | 16,901 | 20% | declared 2, measured 2 (agree) |
| `metrics-report` | 1 of 1 | 1 / 1 / 1 | 1 | 11,889 | 46% | declared 0, measured 1 |
| `amend-docs` | 8 of 11 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `log-miss` | 2 of 3 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `file-feedback` | 1 of 1 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `triage-issues` | 0 of 1 | — | — | — | — | — |
| `facilitate-brainstorming-session` | 0 of 1 | — | — | — | — | — |
| `devguide` | 0 of 1 | — | — | — | — | declared 1, not observed |
| `probe-run` | 0 of 1 | — | — | — | — | — |
| `record-probe` | 0 of 1 | — | — | — | — | — |

**Read the "Runs observed" column first.** Fan-out can only be seen on a `tree`-scope run. On a
`main`-scope run nobody looked at the subagents, so `0` there means *not looked*, not *none ran*. Every
unobserved run in this table is unobserved because it was not `tree` scope; none predate the field.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in
`build-phase`, `handoff-phase` and `day1-brownfield` more subagents ran than were declared, and
`verify-phase` (5) and `metrics-report` (1) ran subagents they never declared.

\* The `fix-issues` declarations also hold prose, not agent kinds (`none`, `none (fixed inline)`, and a
note split by the tool into several entries). The field takes agent kinds, not a description of the
work. This run declared `none` for its fix. The build was done in the main thread.

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- Miss attribution — 6 of 24 misses are `linked`; 18 name a phase no run record backs (REQ-RAG-125's among them). Counts are shown; no rate is.
- Measured (`sole`) rework cost — `insufficient data (n=0)`; every costed fix shared its run with other misses.
- `why_missed` — assessed on 14 of 24 misses; 1 escape (MISS-TechieRag-20260924-01) is missing it and should be completed with `--amend`.
- `sort` — on 14 of 21 eligible misses; 3 predate the field.
- `devguide`, `probe-run`, `record-probe` runs — no wall clock and no token window; excluded from §6 time and token figures. One `log-miss`, one `fix-issues` and one `verify-phase` run also have no token window.
- This `triage-and-fix` run's own run record — written by the status gate after this report; it appears in the next snapshot.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
