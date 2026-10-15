# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     and `tf-metrics.sh --phases .` (run 2026-10-15T14:19Z) and are not recomputed by hand.
     No combined first-pass rate, gate distribution, escape rate, miss rate, or
     cost-per-miss across live/backfilled, across project_type, across attribution
     confidence, or across cost attribution. -->

**Snapshot as of 2026-10-15** · project_type `library` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 101 live runs (1 more voided) | 2026-09-03 → 2026-10-15 |
| `gates.jsonl` | 657 (0 backfilled, 0 malformed) | 2026-09-03 → 2026-10-15 |
| `sessions.jsonl` | 18 (4 duplicate records merged) | 2026-09-03 → 2026-10-11 |
| `commits.jsonl` | 69 | 2025-12-30 → 2026-10-11 |
| `misses.jsonl` | 37 miss + 38 miss-fix | 2026-09-03 → 2026-10-15 |

**This repo is counted as two kinds of project.** It is a `library` now, but 4 gate records written
early on (2026-09-03) still say `app`. The streams are never rewritten, so those 4 records stay in
their own `app` row below. Read the two rows as two periods of the same project. They are never added
together.

**One run is left out of every figure.** A `build-phase` record started 2026-09-25T04:25:00Z was later
marked void because its start time was guessed, not measured. It is still on the stream; it is just not
counted.

**Since the last snapshot (2026-10-11T08:15Z).** Nine run records, four gate records, three misses and
four `miss-fix` records were added.

- **2026-10-11, after the last snapshot.** A `fix-issues` run (started 2026-10-11T09:03:46Z) reopened
  **REQ-RAG-130**, which the owner found accepting a call it should have refused
  (MISS-TechieRag-20261011-06, `regression`). Its escape record says the REQ had been `Verified`. The
  fix needed two attempts: the first `miss-fix` ends in `Needs re-verify`, the second in `Verified`,
  both in the same run. The owner then logged MISS-TechieRag-20261011-07 against **REQ-RAG-132**
  (`wrong-behaviour`, docs and agent guides not updated); it was closed `Verified` inside the
  `log-miss` run, with no token window. This miss has no `gate:"escaped"` record.
- **2026-10-15, this run.** A `triage-and-fix` run (started 2026-10-15T13:52:57Z) added **REQ-RAG-134**,
  found by `owner` (Lekhak feedback TR-RAG-007), logged as MISS-TechieRag-20261015-01
  (`unspecified-gap`) and closed `Verified` in the same run.

Run records added: 3 `fix-issues`, 3 `verify-phase`, 2 `triage-and-fix`, and the `metrics-report` run
that wrote the last snapshot.

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

The records added since the last snapshot all carry the right REQ id.

---

## 1. First-pass rate

*How many requirements reached `Verified` on their first try.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | library | 112 | 100 | **89%** |
| **Live** | app (older records) | 2 | 0 | `insufficient data (n=2)` |

There are no backfilled records, and no REQ has backfilled history, so no REQ is left out of the live
rate.

Down from 90% (100 of 111) on 2026-10-11. The one newly scored REQ is REQ-RAG-134. Its first gate
record is a `FAIL` on attempt 1 with gate `escaped`, so it does not count as a first-time pass.
REQ-RAG-130 was already scored as a failure, so its reopening on 2026-10-11 does not move this figure.
REQ-RAG-128 is still counted as a first-time pass only because its escape row was written against
REQ-FN-001 (see the note at the top).

---

## 2. Gate catch distribution

*When something failed, which check caught it.*

### Live · `library` — 21 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | 0% |
| acceptance | 0 | 0% |
| render (§4a data-render) | 0 | 0% |
| visual (§4b visual-truth) | 0 | 0% |
| standards | 0 | 0% |
| **escaped** — no gate caught it | 21 | **100%** |

### Live · `app` (older records) — 2 failures

| Gate | Caught | Share |
|---|---|---|
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Every failure on record, in both rows, was found **after** all the checks had passed it. No check has
yet caught a failure in this repo. The library row grew from 19 to 21: REQ-RAG-130 (2026-10-11, its
second escape record) and REQ-RAG-134 (2026-10-15). The three newer checks (`perf` since 2026-08-10;
`assets` and `mockup-parity` since 2026-08-31) have run on 0 records in either row. This is a library
with no screens, so those checks do not apply.

---

## 3. Escape rate

*Of the failures on record, how many reached the owner or production instead of being caught by a
check.*

| Provenance | project_type | Failures on record | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | library | 21 | 21 | **100%** |
| **Live** | app (older records) | 2 | 2 | `insufficient data (n=2)` |

The tool's denominator is failure records, not distinct REQs: REQ-RAG-130 now has two `escaped`
records (2026-10-11T07:53Z and 2026-10-11T09:03Z), so it appears twice in the 21.

An escape is a `gate:"escaped"` record: a person found the defect after every check had passed it.
**Nine of the twenty-one library escapes had a `prior_verdict` of `Verified`:** REQ-RAG-069 and
REQ-RAG-070 (2026-10-01), REQ-FN-006, REQ-RAG-116 and REQ-RAG-119 (2026-10-06), REQ-RAG-122
(2026-10-07), REQ-RAG-082 and REQ-RAG-098 (2026-10-08), and REQ-RAG-130 (2026-10-11). The verifier had
signed those off. That is still the strongest signal on this page. REQ-RAG-130 is the first REQ to
escape twice: once as missing work, then — after it was built and `Verified` the same morning — as a
regression. REQ-RAG-134 is a new row with no earlier verdict (`prior_verdict` is `null`), so it is
work nobody had written down, not a sign-off that was wrong. Read this beside §1: 100 of 112 library
REQs passed first time, and every failure on record was found by a person, not by a check.

---

## 4. Throughput and rework — poolable

*These can be compared across project types and provenance, so they are pooled on purpose.*

| Metric | Value |
|---|---|
| Runs total | 101 (`verify-phase`=26, `fix-issues`=18, `build-phase`=16, `amend-docs`=13, `triage-and-fix`=8, `handoff-phase`=4, `metrics-report`=4, `log-miss`=3, `devguide`=2, `day1-brownfield`=1, `facilitate-brainstorming-session`=1, `file-feedback`=1, `probe-run`=1, `productguide`=1, `record-probe`=1, `triage-issues`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | 212% |
| Batch size — median REQs per `build-phase` run | 2 |
| REQ throughput — median REQs/hour | 25.98 |
| Sessions / total tokens | 18 / 8,188,951 |
| Tokens per `Verified` REQ | 12,916.3 |
| Commit cadence | 2.23 commits per active day (69 commits over 31 active days) |

**Rework keeps growing.** The ratio went from 181% to 212%. There were still no new `build-phase`
runs; every new run was a fix, a verify or a metrics report, and every new fix answered either a
consumer's feedback file (Lekhak) or the owner directly.

**No dollar cost is shown here.** Every run on record used Claude Code. Its transcripts hold token
counts but no dollar cost (`cost_usd` is `null`), and this project runs on a subscription. Turning
tokens into dollars with a price list would be a guess dressed up as a measurement, so the page shows
tokens and stops there. No OpenCode run (the one harness that records real dollars) exists in this repo.

The commit hook is installed on this clone (`commit_hook: true`), so the commit count is not low for a
tooling reason. `commits.jsonl` is always one commit behind by design. 0 duplicate commits were merged;
4 duplicate session records were merged, which is a normal union merge and needs no action.

The session token total (8,188,951, from `sessions.jsonl`) and the run output total in §6 (8,580,257,
from `runs.jsonl`) come from different streams that measure different things. They are not meant to
agree and are never added together.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 37 (1 open, 36 resolved, 0 wont-fix; 38 fix records) |
| Design-miss share (`unspecified-gap`) | 46% |
| Found by a human (`owner` / `production`) | 73% |

*This 73% sits **beside** the escape rate in §3 and is never merged with it. The two come from
different records and use different definitions.*

**Who found them:** `owner` 19 · `agent-review` 8 · `production` 8 · `self-smoke` 2.

`owner` went from 16 to 19 (MISS-TechieRag-20261011-06, MISS-TechieRag-20261011-07,
MISS-TechieRag-20261015-01). The one open miss is still MISS-TechieRag-20260924-06 (REQ-RAG-016); it
has no `miss-fix` record. There are 38 fix records for 37 misses because MISS-TechieRag-20261011-06
has two (attempt 1 `Needs re-verify`, attempt 2 `Verified`). No `miss-amend` records exist (0 applied,
0 orphaned), and no fix is orphaned.

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `unspecified-gap` | 17 | 46% |
| `regression` | 10 | 27% |
| `spec-contradiction` | 5 | 14% |
| `wrong-behaviour` | 3 | 8% |
| `hallucinated-api` | 1 | 3% |
| `partial-implementation` | 1 | 3% |

Since 2026-10-11: one `unspecified-gap` (REQ-RAG-134), one `regression` (REQ-RAG-130), one
`wrong-behaviour` (REQ-RAG-132).

**Why it was missed** — *which practice failed* (26 of 37 misses assessed)

| Practice | n | Share |
|---|---|---|
| `missing-checklist-item` | 14 | 54% |
| `insufficient-verify-method` | 12 | 46% |

0 misses predate this field, so all 37 could have carried it; 11 were not assessed (the field is
optional). **2 escapes could have carried it and did not.** One is MISS-TechieRag-20260924-01
(REQ-RAG-076); the other is MISS-TechieRag-20261011-07 (REQ-RAG-132), the only miss added since the
last snapshot with no `why_missed`. Those are the most useful records to finish:
`bash .tfcore/utils/tf-emit.sh --amend <miss_id> why_missed <value>` (SCHEMA §5.5.7).
Never edit `misses.jsonl` by hand.

The two practices are close to level. `missing-checklist-item` points at holes in the spec;
`insufficient-verify-method` points at checks too weak for the defects that actually occur. No miss
has yet been put down to `instruction-ignored`.

**Whose gap it was** (`sort`) — 27 of 34 eligible misses sorted

| Sort | n | Share |
|---|---|---|
| `spec` — the app's spec did not say it | 14 | 52% |
| `weak-check` — a check existed and was too weak | 12 | 44% |
| `unsaid` — the framework never said it | 1 | 4% |

3 misses predate the `sort` field (added 2026-09-07) and are outside this count. They can be sorted
with `bash .tfcore/utils/tf-emit.sh --amend <miss_id> sort <spec|unsaid|weak-check|ignored>`.

MISS-TechieRag-20261011-07 is the first `unsaid` miss in this repo: a gap the framework itself never
told the agent about. None so far were rules that were written and ignored.

### 5a. Attribution — `linked` records only

**7 of 37 misses are attributed; 30 are left out** because they name a phase that no `runs.jsonl`
record backs, so the model that produced them is unknown.

| By | Counts (n=7) |
|---|---|
| Origin phase | `build-phase`=7 |
| Origin agent | `general-purpose`=5, `techierag`=2 |
| Origin model | `claude-opus-5-5`=5, `claude-fable-5-1`=2 |

No new linked miss. MISS-TechieRag-20261011-06 and -07 name `build-phase` / `flow-master` and
MISS-TechieRag-20261015-01 names `day1-greenfield` / `analyst`, all three as `inferred`, so all three
are among the 30 left out.

**These counts show what happened, not why.** Which model gets the hard work is not random, so a model
at the top of this list may be doing the hardest building rather than the worst. With 7 records this is
a question to investigate, not a ranking to route on. No per-phase, per-agent or per-model miss *rate*
is printed.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this miss) | 5 | **60,666.0** (n=5) |
| Apportioned (`shared:n` — divided equally, **not a measurement**) | 21 | 39,060.4 (n=21) |
| Unattributable (`none` — no usable token window) | 12 | — |

**This is the first time the measured figure clears the n≥3 bar, and it needs two notes.**

- **Five records, four misses.** The three new `sole` records are MISS-TechieRag-20261015-01
  (REQ-RAG-134) and both fix records of MISS-TechieRag-20261011-06 (REQ-RAG-130). Those two carry the
  same run window (4,682 output tokens), and the tool counts each as its own record, so that one
  repair is in the mean twice.
- **Two of the five were relabelled by the tool.** REQ-RAG-128 (stored `shared:15`) and REQ-RAG-129
  (stored `shared:16`) count as `sole` because the tool recounts how many misses each fix run closed,
  and each of those runs closed exactly one. The stored labels came from the runs' touched REQs, not
  from the misses they fixed.

0 sole records and 0 apportioned records lacked a token count.

The MISS-TechieRag-20261015-01 `miss-fix` names `fix_cmd` `fix-issues`, although the run that wrote it
is a `triage-and-fix` run — the same pattern as the four 2026-10-11 apportioned records.

12 fixes have no usable token window (most were done inside longer runs; the newest is
MISS-TechieRag-20261011-07, closed inside `log-miss`). They count as misses but cannot be costed.

**Dollars.** None measured. Claude Code records `cost_usd: null` permanently, and no rate card is
applied here. Tokens are the honest figure.

---

## 6. Effort per phase — time, tokens, model, fan-out

Based on **101 live run records** (the voided one is excluded). Token-window coverage: `tree 59` ·
`main 34` · `none 5` · `absent 3`. Wall clock is known for 96 of 101 runs.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `build-phase` | 16 | 13h52m / 16m25s | 2,466,029 | 29% | 16 of 16 runs |
| `amend-docs` | 13 | 4h03m / 8m23s | 2,325,791 | 27% | 13 of 13 runs |
| `handoff-phase` | 4 | 45m04s / 8m25s | 899,325 | 10% | 4 of 4 runs |
| `fix-issues` | 18 | 3h06m / 2m40s | 875,065 | 10% | 16 of 18 runs |
| `day1-brownfield` | 1 | 4h12m (one run) | 644,710 | 8% | 1 of 1 runs |
| `triage-and-fix` | 8 | 59m01s / 2m53s | 459,631 | 5% | 8 of 8 runs |
| `verify-phase` | 26 | 4h22m / 3m29s (n=25, 1 derived) | 452,860 | 5% | 24 of 26 runs |
| `triage-issues` | 1 | 7m47s (one run) | 143,801 | 2% | 1 of 1 runs |
| `metrics-report` | 4 | 10m26s / 2m42s | 115,255 | 1% | 4 of 4 runs |
| `productguide` | 1 | 5m52s (one run) | 79,139 | 1% | 1 of 1 runs |
| `facilitate-brainstorming-session` | 1 | 52m11s (one run) | 59,723 | 1% | 1 of 1 runs |
| `devguide` | 2 | 7m09s (n=1) | 37,964 | 0% | 1 of 2 runs |
| `file-feedback` | 1 | 28m57s (one run) | 16,941 | 0% | 1 of 1 runs |
| `log-miss` | 3 | 1m06s / 33s (n=2, 1 derived) | 4,023 | 0% | 2 of 3 runs |
| `probe-run` | 1 | — (no time recorded) | — | — | 0 of 1 runs |
| `record-probe` | 1 | — (no time recorded) | — | — | 0 of 1 runs |

Runs with no token window (`fix-issues` 2, `verify-phase` 2, `devguide` 1, `log-miss` 1, `probe-run` 1,
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
| `fix-issues` | `claude-opus-5-5` | 657,652 | 75% | 15 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 25% | 1 |
| `day1-brownfield` | `claude-fable-5-1` | 586,678 | 91% | 1 |
| `day1-brownfield` | `claude-opus-5-5` | 58,032 | 9% | 1 |
| `triage-and-fix` | `claude-opus-5-5` | 459,631 | 100% | 8 |
| `verify-phase` | `claude-opus-5-5` | 401,101 | 89% | 20 |
| `verify-phase` | `claude-fable-5-1` | 51,759 | 11% | 4 |
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `metrics-report` | `claude-opus-5-5` | 115,255 | 100% | 4 |
| `productguide` | `claude-fable-5-1` | 79,139 | 100% | 1 |
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
| `verify-phase` | 15 of 26 | 5 / 0 / 5 | 1 | 199,380 | 53% | declared 0, measured 5 |
| `day1-brownfield` | 1 of 1 | 4 / 4 / 4 | 1 | 87,104 | 14% | declared 1, measured 4 |
| `metrics-report` | 3 of 4 | 3 / 1 / 1 | 3 | 43,701 | 50% | declared 0, measured 3 |
| `productguide` | 1 of 1 | 2 / 2 / 2 | 1 | 35,128 | 44% | declared 2, measured 2 (agree) |
| `fix-issues` | 9 of 18 | 4 / 0 / 3 | 2 | 35,217 | 7% | declared 13 (4 `general-purpose`, 5 `none`, plus prose*), measured 4 |
| `triage-and-fix` | 2 of 8 | 2 / 1 / 1 | 2 | 16,901 | 20% | declared 5 (2 `general-purpose`, 3 `none`), measured 2 |
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
Of the nine runs added since the last snapshot, five are `tree` scope (the 2026-10-11 `metrics-report`
run, three 2026-10-11 `fix-issues` runs and one 2026-10-11 `verify-phase` run). Only the
`metrics-report` run spawned a subagent. The three 2026-10-15 runs (two `triage-and-fix`, one
`verify-phase`) were `main` scope and the 2026-10-11T09:08:01Z `verify-phase` run had no window, so
those four add nothing to these figures.

**Declared vs measured.** `subagents` is what a task wrote about itself; `subagent_runs` is counted from
the harness's own records. Where they differ, **the measured one is right**. Tasks under-report: in
`build-phase`, `handoff-phase` and `day1-brownfield` more subagents ran than were declared, and
`verify-phase` (5) and `metrics-report` (3) ran subagents they never declared.

\* The `fix-issues` and `triage-and-fix` declarations also hold `none` and prose, not agent kinds
(`none (fixed inline)`, and a note split by the tool into several entries). The tool counts each of
those as a declared subagent, which is why the declared count is higher than the measured one there.
The field takes agent kinds, not a description of the work.

---

## 7. What is missing

- First-pass rate, gate catch and escape rate for the older `app` records — `insufficient data (n=2)`; needs ≥3 supporting records. New records all carry `library`, so this row will not grow.
- Gate catch beyond "escaped" — no check has caught a failure yet, so there is nothing to compare between checks.
- The right REQ id on two records — MISS-TechieRag-20261009-01 and its `escaped` gate record name REQ-FN-001; the fix names REQ-RAG-128 (TechieFlow TF-005). The tool does not flag this. The streams are not edited, so the first-pass count (REQ-RAG-128 counted as a first-time pass) and the escape list (REQ-FN-001 instead of REQ-RAG-128) carry it.
- Escape rate per distinct REQ — the tool's denominator is failure records; REQ-RAG-130 is in it twice. No distinct-REQ figure is printed.
- Miss attribution — 7 of 37 misses are `linked`; 30 name a phase no run record backs. Counts are shown; no rate is.
- Measured (`sole`) rework cost — printed for the first time (n=5), but the five records cover four misses: MISS-TechieRag-20261011-06's one repair window is counted twice.
- `why_missed` — assessed on 26 of 37 misses; 2 escapes (MISS-TechieRag-20260924-01, MISS-TechieRag-20261011-07) are missing it and should be completed with `--amend`.
- `sort` — on 27 of 34 eligible misses; 3 predate the field.
- `probe-run` and `record-probe` runs — no wall clock and no token window; one `devguide` run likewise has no window. Two `fix-issues`, one `log-miss` and two `verify-phase` runs also have no token window. All are excluded from §6 token figures.
- `sessions.jsonl` and `commits.jsonl` end on 2026-10-11 — no session or commit record has been written since; this run's work is not yet committed.
- Dollars — no measured source (all runs are Claude Code; no OpenCode runs). The tool also prints a list price worked out from a public price list; this report's rule keeps any price-list figure off the page, so it is not shown.
