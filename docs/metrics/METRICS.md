# TechieRag — Development Metrics

<!-- Written by .tfcore/tasks/metrics-report.md (`*metrics`). Regenerated on demand,
     never hand-edited. Source: docs/metrics/*.jsonl (append-only) — schema at
     .tfcore/telemetry/SCHEMA.md. Figures come from `tf-metrics.sh --report . --json`
     and are not recomputed by hand. No combined first-pass rate, gate distribution,
     escape rate, miss rate, or cost-per-miss across live/backfilled, across
     project_type, across attribution confidence, or across cost attribution. -->

**Snapshot as of 2026-09-03** · project_type `app` · schema v1

| Stream | Records | Span |
|---|---|---|
| `runs.jsonl` | 5 | 2026-09-03 → 2026-09-03 |
| `gates.jsonl` | 4 (0 backfilled) | 2026-09-03 → 2026-09-03 |
| `sessions.jsonl` | 0 | — |
| `commits.jsonl` | 30 | 16 active days |
| `misses.jsonl` | 3 miss + 3 miss-fix | 2026-09-03 → 2026-09-03 |

Every non-commit record on this page was written on **one day, by one owner-reported UAT cycle**
(`*triage-issues` → `*log-miss` → `*fix-issues` with the verifier chained → `*log-miss`). That is the whole
live history of this repo's telemetry so far; read every figure below with that n in mind.

---

## 1. First-pass rate

*What fraction of REQs reach `Verified` on attempt 1.*

| Provenance | project_type | REQs scored | First-pass | Rate |
|---|---|---|---|---|
| **Live** | app | 2 | 0 | `insufficient data (n=2)` |

No backfilled rows exist; no REQ carries backfilled history, so nothing is excluded from the live rate.
Both scored REQs (`REQ-FN-003`, `REQ-FN-004`) reached `Verified` on **attempt 2** — their attempt 1 is the
`escaped` record written when the owner found the defect, so by construction neither could pass first time.

---

## 2. Gate catch distribution

*Of all failures, which gate caught them.*

### Live · `app` — 2 failures

| Gate | Caught | Share |
|---|---|---|
| build | 0 | — |
| acceptance | 0 | — |
| render (§4a data-render) | 0 | — |
| assets (§4a2) | 0 | — |
| visual (§4b visual-truth) | 0 | — |
| mockup-parity (§4b2) | 0 | — |
| perf (§4c) | 0 | — |
| standards | 0 | — |
| **escaped** — no gate caught it | 2 | `insufficient data (n=2)` |

Late-added gates (`perf` since 2026-08-10; `assets`, `mockup-parity` since 2026-08-31) ran on 0 records —
both graded REQs are library/CI requirements with no screen, so those gates did not apply. **Failure
classes:** `other` ×2 (a workflow that never derived its version; documentation that contradicted the shipped
distribution — neither fits a screen-failure class, and the closed vocabulary has no CI/docs class).

---

## 3. Escape rate

*What fraction of defective REQs reached UAT/production instead of a gate.*

| Provenance | project_type | REQs with any failure | Escaped to UAT/prod | Rate |
|---|---|---|---|---|
| **Live** | app | 2 | 2 | `insufficient data (n=2)` |

Both failures on record are escapes, and one of them (`REQ-FN-003`) escaped from a `prior_verdict` of
**`Verified`** — the strongest signal this page can carry. Its 2026-07-02 `Verified` was a static YAML parse
of the *previous* workflow; the 2026-08-09 public pipeline was added afterwards and never re-graded against
the REQ's acceptance (BRD-61: version from tag).

---

## 4. Throughput and rework — poolable

| Metric | Value |
|---|---|
| Runs total | 5 (`triage-issues`=1, `log-miss`=2, `fix-issues`=1, `verify-phase`=1) |
| Rework ratio (fix-mode ÷ build-phase runs) | `insufficient data (n=0 build-phase runs)` |
| Batch size — median REQs per `build-phase` run | — (no build-phase runs recorded) |
| REQ throughput — median REQs/hour | 24.93 (2 REQs per run over runs of 0–16 min; a one-cycle figure, not a trend) |
| Sessions / total tokens | 0 / — (`sessions.jsonl` is empty — the SessionEnd hook has not fired for this session yet) |
| Tokens per `Verified` REQ | — (not computed by the tool while sessions are empty) |
| Commit cadence | 1.88 commits/active day over 16 active days (30 commits) |

**Cost in USD is not reported here.** Every run on record is Claude Code; its transcripts carry token counts
but no per-message dollar cost, and pricing tokens from a rate card would be an estimate presented as a
measurement. Tokens are the honest figure. The commit hook is installed on this clone (`commit_hook: true`),
so the commit count is not understated for a tooling reason; `commits.jsonl` lags reality by one commit by
design. 0 duplicates were collapsed.

---

## 5. Misses — what was missed, who missed it, what the fix cost

| Metric | Value |
|---|---|
| Misses logged | 3 (0 open, 3 resolved, 0 wont-fix) |
| Design-miss share (`unspecified-gap`) | 33% (1 of 3) |
| Found by a human (`owner` / `production`) | 67% (2 of 3; the third was `agent-review` during the fix walk) |

*Reported **beside** the escape rate in §3, never merged with it.*

**Miss classes** — *what* was missed

| Class | n | Share |
|---|---|---|
| `regression` (REQ-FN-003 — the public pipeline lost tag-driven versioning) | 1 | 33% |
| `unspecified-gap` (no REQ ever owned consumer-facing install docs → REQ-FN-004) | 1 | 33% |
| `hallucinated-api` (README samples used `SearchResult` members that do not exist) | 1 | 33% |

**Why it was missed** — *which practice failed* (3 of 3 misses assessed)

| Practice | n | Share |
|---|---|---|
| `insufficient-verify-method` | 2 | 67% |
| `missing-checklist-item` | 1 | 33% |

0 misses predate the field; 0 escapes lack it. Two of three say the **verification** was the weak side: a
workflow "verified" by parsing its YAML, and README samples that were never compiled. One says the **spec**
had a hole: BRD-59/60/61 covered the pipeline and never the reader.

### 5a. Attribution — `linked` records only

**0 of 3 misses are attributed; 3 are excluded.** Two name an origin phase (`build-phase`,
`day1-brownfield`) that no `runs.jsonl` record backs — this repo's run stream was empty until today, so the
emitter marked them `inferred` and nulled the model. The third names no origin at all (`unknown`): the
README samples predate the framework's record of this repo.

| By | Counts |
|---|---|
| Origin phase | — (0 linked) |
| Origin agent | — (0 linked) |
| Origin model | — (0 linked) |

No per-phase, per-agent or per-model miss rate can be printed from this stream yet, and none is.

### 5b. Rework cost — measured and apportioned never combine

| | Fix records | Tokens out per miss |
|---|---|---|
| **Measured** (`sole` — the run fixed only this REQ) | 0 | — |
| Apportioned (`shared:2` — divided equally, **not a measurement**) | 3 | 72,471 |
| Unattributable (`none` — no usable token window) | 0 | — |

All three misses were closed by the **one** `fix-issues` run (started 2026-09-03T16:34:13Z), whose window
measured **217,413 output tokens** on `claude-fable-5-1` (`tokens_scope: tree`, 4,233 of them in the one
docs sub-agent). The emitter derived `shared:2` from that run's `reqs_touched` (two REQs), so the
apportioned figure is that window ÷ 3 closed misses — arithmetic over one run, not three measurements.

**Dollars.** No measured dollars. Claude Code carries `cost_usd: null` permanently — no cost source exists,
and pricing tokens from a rate card here would be an estimate presented as a measurement.

**Discovery cost, for the same cycle** (not a miss-stream figure — it is the `triage-issues` run's own
window, §6): finding the two root causes measured **143,801 output tokens** over 467 s wall clock, in a
`main`-scope window (no sub-agents ran).

---

## 6. Effort per phase — time, tokens, model, fan-out

Aggregated over **5 live run records**. Token-window coverage: `tree 3` · `main 1` · `none/absent 1`.

| Phase (`cmd`) | Runs | Wall clock (total / median) | Tokens out | % of all output | Tokens measured on |
|---|---|---|---|---|---|
| `triage-issues` | 1 | 7 m 47 s / 7 m 47 s | 143,801 | 36% | 1 of 1 runs |
| `fix-issues` | 1 | 16 m 17 s / 16 m 17 s | 217,413 | 54% | 1 of 1 runs |
| `verify-phase` | 1 | 3 m 29 s / 3 m 29 s | 39,564 | 10% | 1 of 1 runs |
| `log-miss` | 2 | 1 m 03 s / 1 m 03 s (n=1) | 3,602 | 1% | 1 of 2 runs |

⚠ **The `verify-phase` window sits inside the `fix-issues` window.** The verifier was chained inline by
`*fix-issues` (its §5), so its 39,564 tokens are *also* counted in the fix run's 217,413. The tool sums by
`cmd` and does not de-duplicate overlapping windows; the "% of all output" column therefore adds to more
than the distinct output actually produced. The distinct total is ≈ 364,816 output tokens, not 404,380.

The first `log-miss` run had a sub-minute window and no token record (`tokens_unmeasured_n: 1`); it is
excluded from the token columns, never averaged in as zero.

### 6a. Which model did the work

| Phase | Model | Output tokens | Share of the phase | Runs |
|---|---|---|---|---|
| `triage-issues` | `claude-fable-5-1` | 143,801 | 100% | 1 |
| `fix-issues` | `claude-fable-5-1` | 217,413 | 100% | 1 |
| `verify-phase` | `claude-fable-5-1` | 39,564 | 100% | 1 |
| `log-miss` | `claude-fable-5-1` | 3,602 | 100% | 1 (of 2) |

**This ranking is observational, not causal.** One model did every run today; there is no comparison to
draw, and `fix-issues` costing more than `log-miss` is a fact about what those phases *are*.

### 6b. Subagent fan-out — measured, on its own denominator

| Phase | Runs observed | Spawns (total / median / max) | Runs that fanned out | Output tokens in subagents | Subagent share |
|---|---|---|---|---|---|
| `fix-issues` | 1 of 1 | 1 / 1 / 1 | 1 | 4,233 | 2% |
| `verify-phase` | 1 of 1 | 0 / 0 / 0 | 0 | 0 | 0% |
| `log-miss` | 1 of 2 | 0 / 0 / 0 | 0 | 0 | 0% |
| `triage-issues` | 0 of 1 | — | — | — | — |

`triage-issues` and one `log-miss` run are `main`-scope windows — **not observed**, not "none ran" —
and are outside every figure in this table (excluded: 2, both for not being `tree` scope; 0 predate the
field). **Declared vs measured agree:** `fix-issues` declared `["general-purpose"]` and the harness store
measured 1 subagent run.

---

## 7. What is missing

- First-pass rate, gate catch distribution, escape rate — `insufficient data (n=2)`; needs ≥3 supporting records.
- Rework ratio — `insufficient data (n=0 build-phase runs)`; no build-phase run has been recorded in this repo's live stream.
- Miss attribution by phase / agent / model — 0 of 3 `linked`; the run stream had no history for the origins to resolve against. This will stay true for any miss whose origin predates 2026-09-03.
- Measured (`sole`) rework cost — 0 records; the only fix run touched two REQs, so every closed miss is apportioned.
- `sessions.jsonl` — empty; the SessionEnd hook writes it when the session ends, so the session count and total-token rows are blank for the session that produced everything above.
- Dollars — no source on Claude Code; not estimated.
