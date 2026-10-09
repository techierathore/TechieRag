# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     and `tf-metrics.sh --phases .` (run 2026-10-09T05:11Z) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-10-09** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 85 live runs (1 more voided) | 2026-09-03 → 2026-10-09 |
| `gates.jsonl` | 628 (0 backfilled, 0 malformed) | 2026-09-03 → 2026-10-09 |
| `sessions.jsonl` | 16 (3 duplicate records merged) | 2026-09-03 → 2026-10-08 |
| `commits.jsonl` | 54 | 2025-12-30 → 2026-10-07 |
| `misses.jsonl` | 29 miss + 29 miss-fix | 2026-09-03 → 2026-10-09 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

**Since the last snapshot (2026-10-07).** Two more fix runs are on the streams.
On 2026-10-08 a `fix-issues` run took **REQ-RAG-082** and **REQ-RAG-098** (both `Verified` before,
demoted) and two new rows, **REQ-RAG-126** and **REQ-RAG-127**. All four were found by `production`
(Sevak). On 2026-10-09 a `triage-and-fix` run (started 2026-10-09T03:58:42Z) added **REQ-RAG-128**
(Chatur feedback TR-RAG-006), found by `owner`. All five have a `miss-fix` record ending in `Verified`.

**One record pair disagrees on its REQ id — read §1, §3 and §5 with this in mind.** Today's miss,
MISS-TechieRag-20261009-01, was written with `req_id` **REQ-FN-001**, and the matching `escaped` gate
record (2026-10-09T04:04:26Z) also names **REQ-FN-001**. The work was REQ-RAG-128: the `miss-fix`
that closed it names **REQ-RAG-128**. The wrong id comes from a framework defect (TechieFlow TF-005),
not from the project. The tool pairs a fix with its miss by `miss_id`, so it does **not** report this
as an orphan (`orphan_fixes` = 0) and it does not flag the mismatch either. The streams are never
edited, so the effects stay on the page and are named where they land:

- §1 counts REQ-RAG-128 as passing first time (its only gate record is an attempt-1 `Verified`).
- §2 and §3 count one escape against REQ-FN-001, a requirement that was `Verified` on 2026-10-01 and
  was not the one at fault.
- §5 counts the miss under `req_class` `FN`.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 106 | 100 | **94%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

Down from 96% (99 of 103) on 2026-10-07. The three newly scored REQs are REQ-RAG-126 and REQ-RAG-127
(first gate record is a `FAIL`, attempt 1, `escaped`) and REQ-RAG-128 (attempt-1 `Verified`). REQ-RAG-128
is counted as a first-time pass only because its escape row was written against REQ-FN-001 (see the
note at the top). REQ-RAG-082 and REQ-RAG-098 passed first time long ago; their escapes came on
attempt 8 and count in §2 and §3.

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 14 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 14 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The library row grew from 9 to 14 (REQ-RAG-082, REQ-RAG-098,
REQ-RAG-126 and REQ-RAG-127 on 2026-10-08; one more on 2026-10-09 recorded against REQ-FN-001 that
belongs to REQ-RAG-128). The three newer checks (`perf` since 2026-08-10; `assets` and `mockup-parity`
since 2026-08-31) have run on 0 records in either row. This is a library with no screens, so those
checks do not apply.

---

## 3. Escape rate

*Of the requirements that had any failure, how many reached the owner or production instead of being
caught by a check.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 14 | 14 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

An escape is a `gate:"escaped"` record: a person found the defect after every check had passed it.
**Eight of the fourteen library escapes had a `prior_verdict` of `Verified`:** REQ-RAG-069 and
REQ-RAG-070 (2026-10-01), REQ-FN-006, REQ-RAG-116 and REQ-RAG-119 (2026-10-06), REQ-RAG-122
(2026-10-07), and REQ-RAG-082 and REQ-RAG-098 (2026-10-08). The verifier had signed those off. That is
still the strongest signal on this page. Both new ones are `weak-check` misses: a row said something
that no test held in place. Read this beside §1: 100 of 106 library REQs passed first time, and every
failure on record was found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 85 (`verify-phase`=21, `build-phase`=16, `amend-docs`=13, `fix-issues`=13, `handoff-phase`=4, `triage-and-fix`=4, `log-miss`=3, `devguide`=2, `metrics-report`=2, `day1-brownfield`=1, `facilitate-brainstorming-session`=1, `file-feedback`=1, `probe-run`=1, `productguide`=1, `record-probe`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 156% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 25.98 |
| Sessions / total tokens | 16 / 7,629,652 |
| Tokens per `Verified` REQ | 12,466.8 |
| Commit cadence | 2.0 commits per active day (54 commits over 27 active days) |

**Rework keeps growing.** The ratio went from 131% to 156%. There were no new `build-phase` runs this
period; every new run was a fix, a verify or a docs follow-up, and every fix answered a consumer's
feedback file (Sevak, then Chatur).

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits were merged;
3 duplicate session records were merged, which is a normal union merge and needs no action.

The session token total (7,629,652, from `sessions.jsonl`) and the run output total in §6 (8,136,622,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 29 (1 open, 28 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 38% |
| Found by a human (`owner` / `production`) | 66% |

*This 66% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `owner` 12 · `agent-review` 8 · `production` 7 · `self-smoke` 2.

`production` went from 3 to 7 with the four Sevak misses of 2026-10-08 (REQ-RAG-082, REQ-RAG-098,
REQ-RAG-126, REQ-RAG-127). `owner` went from 11 to 12 with MISS-TechieRag-20261009-01 (recorded as
REQ-FN-001; the work was REQ-RAG-128). The one open miss is still MISS-TechieRag-20260924-06
(REQ-RAG-016); it has no `miss-fix` record. No `miss-amend` records exist (0 applied, 0 orphaned), and
no fix is orphaned.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 11 | 38% |
| `regression` | 9 | 31% |
| `spec-contradiction` | 5 | 17% |
| `wrong-behaviour` | 2 | 7% |
| `hallucinated-api` | 1 | 3% |
| `partial-implementation` | 1 | 3% |

Since 2026-10-07: three `unspecified-gap` (REQ-RAG-126, REQ-RAG-127, and today's miss) and two
`regression` (REQ-RAG-082, REQ-RAG-098).

**Why it was missed** — *which practice failed* (19 of 29 misses assessed)

| Practice | n | Share |
|---|---|---|
| `insufficient-verify-method` | 11 | 58% |
| `missing-checklist-item` | 8 | 42% |

0 misses predate this field, so all 29 could have carried it; 10 were simply not assessed (the field is
optional). **1 escape could have carried it and did not:** MISS-TechieRag-20260924-01 (REQ-RAG-076).
That is the most useful record to finish:
`bash .tfcore/utils/tf-emit.sh --amend MISS-TechieRag-20260924-01 why_missed <value>` (SCHEMA §5.5.7).
Never edit `misses.jsonl` by hand.

**Whose gap it was** (`sort`) — 19 of 26 eligible misses sorted

| Sort | n | Share |
|---|---|---|
| `weak-check` — a check existed and was too weak | 11 | 58% |
| `spec` — the app's spec did not say it | 8 | 42% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

All five new misses carry both fields: the three new requirements are `spec` /
`missing-checklist-item`, the two regressions are `weak-check` / `insufficient-verify-method`. The gap
between the two kinds is narrowing. None so far were rules that were written and ignored.

### 5a. Attribution — `linked` records only

**7 of 29 misses are attributed; 22 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts (n=7) |
|---|---|
| Origin phase | `build-phase`=7 |
| Origin agent | `general-purpose`=5, `techierag`=2 |
| Origin model | `claude-opus-5-5`=5, `claude-fable-5-1`=2 |

The one new linked miss is REQ-RAG-082. REQ-RAG-098 names `build-phase` but is `inferred`, and the
three new-requirement misses (REQ-RAG-126, REQ-RAG-127, today's) name `day1-greenfield` (`inferred`),
which no run record backs; all four are among the 22 left out.

**These counts show what happened, not why.** Which model gets the hard work is not random, so a model
at the top of this list may be doing the hardest building rather than the worst. With 7 records this is
a question to investigate, not a ranking to route on. No per-phase, per-agent or per-model miss *rate*
is printed.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this miss) | 1 | `insufficient data (n=1)` |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 17 | 39,565.1 |
| Unattributable (`none` — no usable token window) | 11 | — |

**The first `sole` record is today's fix, and it is stored as `shared:15`.** The tool does not trust
the stored label. It recounts how many misses each fix run closed, and the 2026-10-09 run closed
exactly one (MISS-TechieRag-20261009-01), so it counts as `sole`. The stored `shared:15` came from the
run's 15 touched REQs, not from the misses it fixed. One measured record is not enough for a figure.
0 sole records and 0 apportioned records lacked a token count.

The 2026-10-08 fix added 4 apportioned records (REQ-RAG-082, REQ-RAG-098, REQ-RAG-126, REQ-RAG-127,
each `shared:4`, 100,089 output tokens on the run window).

11 fixes have no usable token window (most were done inside longer runs). They count as misses but
cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **85 live run records** (the voided one is excluded). Token-window coverage: `tree 50` ·
`main 28` · `none 4` · `absent 3`. Wall clock is known for 80 of 85 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 16 | 13h52m / 16m25s | 2,466,029 | 30% | 16 of 16 runs |
| `amend-docs` | 13 | 4h03m / 8m23s | 2,325,791 | 29% | 13 of 13 runs |
| `handoff-phase` | 4 | 45m04s / 8m25s | 899,325 | 11% | 4 of 4 runs |
| `fix-issues` | 13 | 2h51m / 10m05s | 764,053 | 9% | 11 of 13 runs |
| `day1-brownfield` | 1 | 4h12m (one run) | 644,710 | 8% | 1 of 1 runs |
| `verify-phase` | 21 | 3h43m / 2m40s (n=20) | 445,392 | 5% | 20 of 21 runs |
| `triage-and-fix` | 4 | 38m13s / 2m53s | 196,813 | 2% | 4 of 4 runs |
| `triage-issues` | 1 | 7m47s (one run) | 143,801 | 2% | 1 of 1 runs |
| `productguide` | 1 | 5m52s (one run) | 79,139 | 1% | 1 of 1 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s (one run) | 59,723 | 1% | 1 of 1 runs |
| `metrics-report` | 2 | 5m02s / 2m31s | 52,918 | 1% | 2 of 2 runs |
| `devguide` | 2 | 7m09s (n=1) | 37,964 | 0% | 1 of 2 runs |
| `file-feedback` | 1 | 28m57s (one run) | 16,941 | 0% | 1 of 1 runs |
| `log-miss` | 3 | 1m06s / 33s (n=2) | 4,023 | 0% | 2 of 3 runs |
| `probe-run` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `record-probe` | 1 | — (no time recorded) | — | — | 0 of 1 runs |

Runs with no token window (`fix-issues` 2, `devguide` 1, `log-miss` 1, `verify-phase` 1, `probe-run` 1,
`record-probe` 1) are left out of the token columns, never counted as zero. Phases with fewer than 3
measured runs show a single run's figures, not a trend; the tool prints their per-run output as
`insufficient data`.

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
| `amend-docs` | `claude-fable-5-1` | 2,015,304 | 87% | 5 |
| `amend-docs` | `claude-opus-5-5` | 310,487 | 13% | 8 |
| `handoff-phase` | `claude-fable-5-1` | 711,212 | 79% | 1 |
| `handoff-phase` | `claude-opus-5-5` | 188,113 | 21% | 3 |
| `fix-issues` | `claude-opus-5-5` | 546,640 | 72% | 10 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 28% | 1 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `verify-phase` | `claude-opus-5-5` | 393,633 | 88% | 16 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 12% | 4 |
| `triage-and-fix` | `claude-opus-5-5` | 196,813 | 100% | 4 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
| `facilitate-brainstorming-session` | `claude-fable-5-1` | 59,723 | 100% | 1 |
| `metrics-report` | `claude-opus-5-5` | 52,918 | 100% | 2 |
| `devguide` | `claude-opus-5-5` | 37,964 | 100% | 1 |
| `file-feedback` | `claude-opus-5-5` | 16,941 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 90% | 1 |
| `log-miss` | `claude-opus-5-5` | 421 | 10% | 1 |

`build-phase` and `day1-brownfield` each show one run with a `<synthetic>` model entry and 0 output
tokens; it adds nothing and is left out of the table. Every new run since the last
snapshot that names a model names `claude-opus-5-5`.

**This shows what happened, not why.** Which model gets which phase is not random, so a cost
difference between models here says at least as much about *the work they were given* as about the
models. Routing was observed, never enforced: no run on record was on a planned tier.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share | Declared vs measured |
|---|---|---|---|---|---|---|
| `build-phase` | 14 of 16 | 19 / 1 / 7 | 8 | 1,521,699 | 70% | declared 11, measured 19 |
| `handoff-phase` | 3 of 4 | 10 / 1 / 8 | 3 | 398,955 | 48% | declared 6, measured 10 |
| `verify-phase` | 13 of 21 | 5 / 0 / 5 | 1 | 199,380 | 53% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `fix-issues` | 4 of 13 | 4 / 0.5 / 3 | 2 | 35,217 | 10% | declared 4 `general-purpose` + prose*, measured 4 |
| `triage-and-fix` | 2 of 4 | 2 / 1 / 1 | 2 | 16,901 | 20% | declared 2 `general-purpose` + `none`, measured 2 |
| `metrics-report` | 1 of 2 | 1 / 1 / 1 | 1 | 11,889 | 46% | declared 0, measured 1 |
| `amend-docs` | 8 of 13 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `log-miss` | 2 of 3 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `file-feedback` | 1 of 1 | 0 / 0 / 0 | 0 | 0 | 0% | declared 0, measured 0 (agree) |
| `triage-issues` | 0 of 1 | — | — | — | — | — |
| `facilitate-brainstorming-session` | 0 of 1 | — | — | — | — | — |
| `devguide` | 0 of 2 | — | — | — | — | declared 1, not observed |
| `probe-run` | 0 of 1 | — | — | — | — | — |
| `record-probe` | 0 of 1 | — | — | — | — | — |

**Read the "Runs observed" column first.** Fan-out can only be seen on a `tree`-scope run. On a
`main`-scope run nobody looked at the subagents, so `0` there means *not looked*, not *none ran*. Every
unobserved run in this table is unobserved because it was not `tree` scope; none predate the field.
All ten runs added since the last snapshot are `main` or `none` scope (`tree` stayed at 50), so none
of them adds to the fan-out figures.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in
`build-phase`, `handoff-phase` and `day1-brownfield` more subagents ran than were declared, and
`verify-phase` (5) and `metrics-report` (1) ran subagents they never declared.

\* The `fix-issues` declarations also hold prose, not agent kinds (`none`, `none (fixed inline)`, and a
note split by the tool into several entries). The field takes agent kinds, not a description of the
work.

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- The right REQ id on two records — MISS-TechieRag-20261009-01 and its `escaped` gate record name REQ-FN-001; the fix names REQ-RAG-128 (TechieFlow TF-005). The tool does not flag this. The streams are not edited, so the first-pass count (REQ-RAG-128 counted as a first-time pass) and the escape list (REQ-FN-001 instead of REQ-RAG-128) carry it.
- Miss attribution — 7 of 29 misses are `linked`; 22 name a phase no run record backs. Counts are shown; no rate is.
- Measured (`sole`) rework cost — `insufficient data (n=1)`; every other costed fix shared its run with other misses.
- `why_missed` — assessed on 19 of 29 misses; 1 escape (MISS-TechieRag-20260924-01) is missing it and should be completed with `--amend`.
- `sort` — on 19 of 26 eligible misses; 3 predate the field.
- `probe-run` and `record-probe` runs — no wall clock and no token window; one `devguide` run likewise has no window. Two `fix-issues`, one `log-miss` and one `verify-phase` run also have no token window. All are excluded from §6 token figures.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
