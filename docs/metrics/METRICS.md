# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     and `tf-metrics.sh --phases .` (run 2026-10-11T08:15Z) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-10-11** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 92 live runs (1 more voided) | 2026-09-03 → 2026-10-11 |
| `gates.jsonl` | 653 (0 backfilled, 0 malformed) | 2026-09-03 → 2026-10-11 |
| `sessions.jsonl` | 17 (4 duplicate records merged) | 2026-09-03 → 2026-10-11 |
| `commits.jsonl` | 68 | 2025-12-30 → 2026-10-11 |
| `misses.jsonl` | 34 miss + 34 miss-fix | 2026-09-03 → 2026-10-11 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

**Since the last snapshot (2026-10-09).** Two more fix runs are on the streams, both on 2026-10-11.
A `fix-issues` run (started 2026-10-11T05:17:26Z, followed by a verify pass and a hand-off) added
**REQ-RAG-129**, found by `production` (Sevak feedback TR-RAG-053). A `triage-and-fix` run (started
2026-10-11T07:51:22Z) added **REQ-RAG-130**, **REQ-RAG-131**, **REQ-RAG-132** and **REQ-RAG-133**, all
four found by `owner`. All five were logged as escaped misses, and all five have a `miss-fix` record
ending in `Verified` in the same run. Seven run records were added in all: 2 `fix-issues`,
2 `triage-and-fix`, 2 `verify-phase`, and the `metrics-report` run that wrote the last snapshot.

**One record pair still disagrees on its REQ id — read §1, §3 and §5 with this in mind.**
MISS-TechieRag-20261009-01 was written with `req_id` **REQ-FN-001**, and the matching `escaped` gate
record (2026-10-09T04:04:26Z) also names **REQ-FN-001**. The work was REQ-RAG-128: the `miss-fix`
that closed it names **REQ-RAG-128**. The wrong id comes from a framework defect (TechieFlow TF-005),
not from the project. The tool pairs a fix with its miss by `miss_id`, so it does **not** report this
as an orphan (`orphan_fixes` = 0) and it does not flag the mismatch either. The streams are never
edited, so the effects stay on the page:

- §1 counts REQ-RAG-128 as passing first time (its only gate record is an attempt-1 `Verified`).
- §2 and §3 count one escape against REQ-FN-001, a requirement that was `Verified` on 2026-10-01 and
  was not the one at fault.
- §5 counts the miss under `req_class` `FN`.

Today's five records all carry the right REQ id.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 111 | 100 | **90%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

Down from 94% (100 of 106) on 2026-10-09. The five newly scored REQs are REQ-RAG-129 to REQ-RAG-133.
Each one's first gate record is a `FAIL` on attempt 1 with gate `escaped`, so none of them counts as a
first-time pass. REQ-RAG-128 is still counted as a first-time pass only because its escape row was
written against REQ-FN-001 (see the note at the top).

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 19 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 19 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The library row grew from 14 to 19 (REQ-RAG-129 to REQ-RAG-133, all
on 2026-10-11). The three newer checks (`perf` since 2026-08-10; `assets` and `mockup-parity` since
2026-08-31) have run on 0 records in either row. This is a library with no screens, so those checks
do not apply.

---

## 3. Escape rate

*Of the requirements that had any failure, how many reached the owner or production instead of being
caught by a check.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 19 | 19 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

An escape is a `gate:"escaped"` record: a person found the defect after every check had passed it.
**Eight of the nineteen library escapes had a `prior_verdict` of `Verified`:** REQ-RAG-069 and
REQ-RAG-070 (2026-10-01), REQ-FN-006, REQ-RAG-116 and REQ-RAG-119 (2026-10-06), REQ-RAG-122
(2026-10-07), and REQ-RAG-082 and REQ-RAG-098 (2026-10-08). The verifier had signed those off. That is
still the strongest signal on this page. The five new escapes are different: they are new rows with no
earlier verdict (`prior_verdict` is `null`), so they are work nobody had written down, not sign-offs
that were wrong. Read this beside §1: 100 of 111 library REQs passed first time, and every failure on
record was found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 92 (`verify-phase`=23, `build-phase`=16, `fix-issues`=15, `amend-docs`=13, `triage-and-fix`=6, `handoff-phase`=4, `log-miss`=3, `metrics-report`=3, `devguide`=2, `day1-brownfield`=1, `facilitate-brainstorming-session`=1, `file-feedback`=1, `probe-run`=1, `productguide`=1, `record-probe`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 181% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 25.98 |
| Sessions / total tokens | 17 / 7,922,530 |
| Tokens per `Verified` REQ | 12,535.6 |
| Commit cadence | 2.19 commits per active day (68 commits over 31 active days) |

**Rework keeps growing.** The ratio went from 156% to 181%. There were still no new `build-phase`
runs; every new run was a fix, a verify or this report, and every new fix answered either a consumer's
feedback file (Sevak) or the owner directly.

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits were merged;
4 duplicate session records were merged, which is a normal union merge and needs no action.

The session token total (7,922,530, from `sessions.jsonl`) and the run output total in §6 (8,404,929,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 34 (1 open, 33 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 47% |
| Found by a human (`owner` / `production`) | 71% |

*This 71% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `owner` 16 · `agent-review` 8 · `production` 8 · `self-smoke` 2.

`production` went from 7 to 8 with REQ-RAG-129 (Sevak). `owner` went from 12 to 16 with REQ-RAG-130 to
REQ-RAG-133. The one open miss is still MISS-TechieRag-20260924-06 (REQ-RAG-016); it has no `miss-fix`
record. No `miss-amend` records exist (0 applied, 0 orphaned), and no fix is orphaned.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 16 | 47% |
| `regression` | 9 | 26% |
| `spec-contradiction` | 5 | 15% |
| `wrong-behaviour` | 2 | 6% |
| `hallucinated-api` | 1 | 3% |
| `partial-implementation` | 1 | 3% |

Since 2026-10-09: five `unspecified-gap` (REQ-RAG-129 to REQ-RAG-133). Missing requirements are now
the clear leader, ahead of regressions.

**Why it was missed** — *which practice failed* (24 of 34 misses assessed)

| Practice | n | Share |
|---|---|---|
| `missing-checklist-item` | 13 | 54% |
| `insufficient-verify-method` | 11 | 46% |

0 misses predate this field, so all 34 could have carried it; 10 were simply not assessed (the field is
optional). **1 escape could have carried it and did not:** MISS-TechieRag-20260924-01 (REQ-RAG-076).
That is the most useful record to finish:
`bash .tfcore/utils/tf-emit.sh --amend MISS-TechieRag-20260924-01 why_missed <value>` (SCHEMA §5.5.7).
Never edit `misses.jsonl` by hand.

**Whose gap it was** (`sort`) — 24 of 31 eligible misses sorted

| Sort | n | Share |
|---|---|---|
| `spec` — the app's spec did not say it | 12 | 50% |
| `weak-check` — a check existed and was too weak | 12 | 50% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

All five new misses carry both fields and all five say `missing-checklist-item`. Four are `spec`
(REQ-RAG-129 to REQ-RAG-132); REQ-RAG-133 is `weak-check`. The two kinds of gap are now level at 12
each. None so far were rules that were written and ignored.

### 5a. Attribution — `linked` records only

**7 of 34 misses are attributed; 27 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts (n=7) |
|---|---|
| Origin phase | `build-phase`=7 |
| Origin agent | `general-purpose`=5, `techierag`=2 |
| Origin model | `claude-opus-5-5`=5, `claude-fable-5-1`=2 |

No new linked miss. All five new misses name `day1-greenfield` (`inferred`), which no run record backs,
so all five are among the 27 left out.

**These counts show what happened, not why.** Which model gets the hard work is not random, so a model
at the top of this list may be doing the hardest building rather than the worst. With 7 records this is
a question to investigate, not a ranking to route on. No per-phase, per-agent or per-model miss *rate*
is printed.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this miss) | 2 | `insufficient data (n=2)` |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 21 | 39,060.4 |
| Unattributable (`none` — no usable token window) | 11 | — |

**The second `sole` record is REQ-RAG-129, and it is stored as `shared:16`.** The tool does not trust
the stored label. It recounts how many misses each fix run closed, and the 2026-10-11T05:17:26Z run
closed exactly one (MISS-TechieRag-20261011-01), so it counts as `sole`. The stored `shared:16` came
from the run's 16 touched REQs, not from the misses it fixed. The first `sole` record (REQ-RAG-128,
stored as `shared:15`) is the same case. Two measured records are still not enough for a figure.
0 sole records and 0 apportioned records lacked a token count.

The `triage-and-fix` run added 4 apportioned records (REQ-RAG-130 to REQ-RAG-133, each `shared:4`,
147,661 output tokens on the run window). Those four `miss-fix` records name `fix_cmd` `fix-issues`,
although the run that wrote them is a `triage-and-fix` run.

11 fixes have no usable token window (most were done inside longer runs). They count as misses but
cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **92 live run records** (the voided one is excluded). Token-window coverage: `tree 54` ·
`main 31` · `none 4` · `absent 3`. Wall clock is known for 87 of 92 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 16 | 13h52m / 16m25s | 2,466,029 | 29% | 16 of 16 runs |
| `amend-docs` | 13 | 4h03m / 8m23s | 2,325,791 | 28% | 13 of 13 runs |
| `handoff-phase` | 4 | 45m04s / 8m25s | 899,325 | 11% | 4 of 4 runs |
| `fix-issues` | 15 | 3h03m / 10m05s | 860,666 | 10% | 13 of 15 runs |
| `day1-brownfield` | 1 | 4h12m (one run) | 644,710 | 8% | 1 of 1 runs |
| `verify-phase` | 23 | 4h06m / 6m45s (n=22) | 450,388 | 5% | 22 of 23 runs |
| `triage-and-fix` | 6 | 49m19s / 2m53s | 346,093 | 4% | 6 of 6 runs |
| `triage-issues` | 1 | 7m47s (one run) | 143,801 | 2% | 1 of 1 runs |
| `productguide` | 1 | 5m52s (one run) | 79,139 | 1% | 1 of 1 runs |
| `metrics-report` | 3 | 8m08s / 3m06s | 70,336 | 1% | 3 of 3 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s (one run) | 59,723 | 1% | 1 of 1 runs |
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
| `fix-issues` | `claude-opus-5-5` | 643,253 | 75% | 12 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 25% | 1 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `verify-phase` | `claude-opus-5-5` | 398,629 | 89% | 18 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 11% | 4 |
| `triage-and-fix` | `claude-opus-5-5` | 346,093 | 100% | 6 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
| `metrics-report` | `claude-opus-5-5` | 70,336 | 100% | 3 |
| `facilitate-brainstorming-session` | `claude-fable-5-1` | 59,723 | 100% | 1 |
| `devguide` | `claude-opus-5-5` | 37,964 | 100% | 1 |
| `file-feedback` | `claude-opus-5-5` | 16,941 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 90% | 1 |
| `log-miss` | `claude-opus-5-5` | 421 | 10% | 1 |

`build-phase` and `day1-brownfield` each show one run with a `<synthetic>` model entry and 0 output
tokens; it adds nothing and is left out of the table. Every new run since the last snapshot names
`claude-opus-5-5`.

**This shows what happened, not why.** Which model gets which phase is not random, so a cost
difference between models here says at least as much about *the work they were given* as about the
models. Routing was observed, never enforced: no run on record was on a planned tier.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share | Declared vs measured |
|---|---|---|---|---|---|---|
| `build-phase` | 14 of 16 | 19 / 1 / 7 | 8 | 1,521,699 | 70% | declared 11, measured 19 |
| `handoff-phase` | 3 of 4 | 10 / 1 / 8 | 3 | 398,955 | 48% | declared 6, measured 10 |
| `verify-phase` | 14 of 23 | 5 / 0 / 5 | 1 | 199,380 | 53% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `fix-issues` | 6 of 15 | 4 / 0 / 3 | 2 | 35,217 | 8% | declared 12 (4 `general-purpose`, 4 `none`, plus prose*), measured 4 |
| `metrics-report` | 2 of 3 | 2 / 1 / 1 | 2 | 29,307 | 68% | declared 0, measured 2 |
| `triage-and-fix` | 2 of 6 | 2 / 1 / 1 | 2 | 16,901 | 20% | declared 4 (2 `general-purpose`, 2 `none`), measured 2 |
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
Of the seven runs added since the last snapshot, four are `tree` scope (the 2026-10-09 `metrics-report`
run, the two 2026-10-11 `fix-issues` runs and the first 2026-10-11 `verify-phase` run). Only the
`metrics-report` run spawned a subagent. The `triage-and-fix` run and its verify pass were `main`
scope, so they add nothing to these figures.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in
`build-phase`, `handoff-phase` and `day1-brownfield` more subagents ran than were declared, and
`verify-phase` (5) and `metrics-report` (2) ran subagents they never declared.

\* The `fix-issues` and `triage-and-fix` declarations also hold `none` and prose, not agent kinds
(`none (fixed inline)`, and a note split by the tool into several entries). The tool counts each of
those as a declared subagent, which is why the declared count is higher than the measured one there.
The field takes agent kinds, not a description of the work.

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- The right REQ id on two records — MISS-TechieRag-20261009-01 and its `escaped` gate record name REQ-FN-001; the fix names REQ-RAG-128 (TechieFlow TF-005). The tool does not flag this. The streams are not edited, so the first-pass count (REQ-RAG-128 counted as a first-time pass) and the escape list (REQ-FN-001 instead of REQ-RAG-128) carry it.
- Miss attribution — 7 of 34 misses are `linked`; 27 name a phase no run record backs. Counts are shown; no rate is.
- Measured (`sole`) rework cost — `insufficient data (n=2)`; every other costed fix shared its run with other misses.
- `why_missed` — assessed on 24 of 34 misses; 1 escape (MISS-TechieRag-20260924-01) is missing it and should be completed with `--amend`.
- `sort` — on 24 of 31 eligible misses; 3 predate the field.
- `probe-run` and `record-probe` runs — no wall clock and no token window; one `devguide` run likewise has no window. Two `fix-issues`, one `log-miss` and one `verify-phase` run also have no token window. All are excluded from §6 token figures.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
