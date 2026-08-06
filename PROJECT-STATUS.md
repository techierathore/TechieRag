---
project: TechieRag
stack: .NET 10 class library (NuGet) + TechieRag.Embedded (ONNX BGE-M3) + TechieRag.Telemetry (opt-in OTel) + TechieDesk desktop app (MAUI Blazor Hybrid — macOS + Windows, TrBlazeUI, apps/TechieDesk)
last_updated: 2026-08-05
current_phase: Build — 106 open; stale-vector banner FIXED and seen live — RE-INGEST REQUIRED
last_verified_build: PASS
last_verified_date: 2026-08-05
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet) + TechieDesk product app. **This file is the dashboard only** —
per-REQ evidence lives in `docs/TechieDesk-Checklist.md` (Requirements Status table); library defects in
the per-library feedback files.

## Where I am

A `*build-phase` scoped by you to the **open TechieRag (`REQ-RAG-*`) cluster**, plus `REQ-FN-053`, then
extended on your approval to **download the reranker model and finish BRD-106**.
Build **0 errors**, **2,308 tests pass** (was 2,225) — **+83 new, 0 failed**.

**BRD-106 is complete: the local ONNX cross-encoder ranks, live, for the first time** — 7/7 scoring
tests, including cross-lingual.

Getting there was the story. That component had **never executed**, and running it uncovered **two
defects that made it impossible to run at all** — and then a **third, in the BGE-M3 embedder**, which
you approved fixing. Cross-lingual retrieval went from **0.3536 (losing to 0.3642)** to **0.7182**.

🔶 **That fix means your stored vectors are stale. Re-ingest everything — do not top up.**

Your environment answer became work in its own right: **verification endpoints are now declared in
config, not discovered by probing localhost.**

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieDesk
```

**You granted the consent, it immediately found a defect, and the defect is now fixed and seen.**
The stale-vector banner renders live in Hindi — *"5 of 5 documents here were indexed with a different
embedding model"*. Root cause was mine: `EmbeddingSignature` has an `unknown` default and I had given
the override to the embedded provider **alone**, while this install runs **Ollama** — so the feature
looked finished and did nothing. All 8 remaining providers now publish one, guarded reflectively.

Two things still need **you**, not a build:

1. 🔶 **Re-ingest.** Clear each workspace's documents and add them again — everything, not a top-up.
   Until then retrieval is reading vectors written by the old tokenizer. **The document library now
   tells you**: a warning names how many documents are stale, and says so more loudly if you stop
   half way. It clears itself when the last one is done.
2. **The endpoints.** `.tfcore/core-config.yaml → runtimeVerification.services` is shipped with every
   key commented out. Fill in what this Mac really offers and the 44 `Needs re-verify` rows stop being
   blocked on a guess — `docs/VERIFICATION-ENDPOINTS.md` says where and how.

## Open requirements

- **TechieDesk: 106 open** of 179 — **73 terminal** (55 `Verified` + 18 `N/A`). **0 `FAIL`.** Open: 44
  `Implemented`, 44 `Needs re-verify`, 9 `Blocked`, 4 `PARTIAL`, 2 `Planned`, 2 `Not Started`,
  1 `In Progress`.
- **Graded on a LIVE SCREEN 2026-08-04 (second verify run)** — 0 promoted, 1 downgraded, 5 held:
  - `REQ-RAG-052` **95% → `Needs re-verify` 80%** 🔴. The banner is not on screen. Library half
    untouched and still passing; the consumer is the failure.
  - `REQ-UI-059` held 95%, but **clause 2 is now proven on screen** — legacy English rows render
    verbatim amid Devanagari, each confirmed `ContentJson NULL` in the live store.
  - `REQ-UI-058` and `REQ-RAG-051` held — both need a live **blocked tool call**, which `REQ-FN-053`
    still prevents. The screen was available; the event was not.
  - `REQ-RAG-044` and `REQ-RAG-049` held — Postgres and IMAP still undeclared.
- **Built after the first verify run** — the localization loop the library has been waiting on:
  - `REQ-UI-058` `Planned 0%` → **`Implemented 90%`**. The library's 24 flow codes finally have a
    reader; the mapping is **exhaustive by reflection**, so a future code cannot ship as English.
  - `REQ-UI-059` `Planned 0%` → **`Implemented 95%`**. **Both halves.** The chat transcript stores
    codes, not frozen sentences, and legacy rows render verbatim **permanently**; then the
    "model-facing" argument was settled by splitting the audience — `SkillOutcome` carries invariant
    English for the model and a code for the person, across all six refusal sites.
- **Graded by the verifier 2026-08-04** — 1 promoted, 4 held, 0 failed:
  - `REQ-RAG-025` → **`Verified 100%`**. 29/29 tests, **0 skipped**, including 9 live scoring runs
    against the staged 2.28 GB model. Owns no screen, so §4a/§4b are exempt by shape.
  - `REQ-RAG-051` 90% → **95%** (held). 2 of 3 acceptance clauses proven; the third says the trace row
    *renders* blocked, and no screen could be driven.
  - `REQ-RAG-052` 100% → **95%** (LOWERED). Library half proven live (0.7182 cross-lingual, 6 tests
    through a real store); the banner a person actually reads has never been seen.
  - `REQ-RAG-044` (85%) and `REQ-RAG-049` (90%) held — their own acceptance names a real Postgres and
    a real IMAP mailbox, and neither is declared.
- **Built earlier the same day** (all to `Implemented` — the verifier's grades above supersede):
  - `REQ-RAG-051` `Planned 0%` → **`Implemented 90%`**. `ForFlow` returned a bare string, so a blocked
    sub-flow got `IsSuccess` defaulting to `true` and the trace painted it green. The delegate now
    carries the failure. **The old test named this defect without asserting the flag** — that is how it
    survived a release; it does now.
  - `REQ-RAG-049` `Blocked 75%` → **`Implemented 90%`**. TR-RAG-037 fixed: mbox item identity no longer
    embeds the archive's file name, so renaming it stops re-ingesting everything. IMAP is untouched.
  - `REQ-RAG-044` `PARTIAL 70%` → **`Implemented 85%`**. Its remark was **stale** — written 2026-07-29
    against the Chroma/Milvus/Pinecone/Weaviate list you WITHDREW on 2026-07-31. Corrected, and
    `PgVectorStore` went from **zero tests to 7**.
  - `REQ-RAG-025` `Needs re-verify 75%` → **`Implemented 95%`** — **BRD-106 complete.** Zero tests → 14
    hermetic + 7 live scoring + 4 URL guards. Two blocking defects fixed on the way (**TR-RAG-045**: the
    download 404'd, omitted the 2.27 GB weights file, and had a completeness check that was false by
    construction; **TR-RAG-044**: the tokenizer offset). Model staged at
    `~/.cache/techierag-models/bge-reranker-v2-m3` (2.28 GB, byte-exact).
  - `REQ-FN-053` `Planned 0%` → **`In Progress 40%`**. Read the blocker; this one is not what it looked
    like.

## Known blockers

- 🟢 **`REQ-RAG-052` — BANNER FIXED AND SEEN (2026-08-05).** Was: not rendering at all. `/workspace/Default/documents`
  lists 5 documents embedded by the old tokenizer and now says so, in Hindi, live.
  - **Root cause was mine.** `EmbeddingSignature` carries an `unknown` DEFAULT (so external
    implementers keep compiling) and I had overridden it on `EmbeddedEmbeddingProvider` **alone**.
    This install runs **Ollama** (`EmbeddingSource` ordinal 2), so the signature was `unknown`,
    `IsDeterminable` false, banner suppressed **silently**. All 8 remaining providers now publish one.
  - ⚠ **Three hypotheses were disproven first, and one of my own fixes was INERT** — it passed its
    mutation test unchanged, proving it fixed nothing, and was reverted rather than shipped. The
    answer came from instrumenting the running page: `matched=5 signature=unknown`.
  - **Guarded**: a reflective sweep fails when any provider inherits the default — mutation-tested.

- 🟢 **`REQ-UI-059` — BOTH HALVES BUILT; clause 2 now proven ON SCREEN.**
  Product-authored messages now persist a **code + arguments** and render per reader; rows written
  before codes existed print verbatim **permanently** (deleting that branch would blank out real
  conversations). A source-level guard, **mutation-tested RED then GREEN**, stops a future write
  localizing at write time again.
  - ✅ **Half two done too.** The reasoning that kept those sentences English was right — do not
    translate what the model reads — but the premise was wrong: the UI renders that text **verbatim
    to a person**. `SkillOutcome` now carries both audiences, and the English is rendered from the
    same resource entry the code names, so they cannot drift. **The library was not changed**: the
    app owns its tool handler now, because `ToolRegistry`'s delegate had nowhere to put a code.
  - ✅ **Seen at last.** Five legacy English rows render verbatim amid complete Devanagari, each
    confirmed in the live store as `ContentJson NULL` dated 2026-07-30 — English there is the policy
    working, not the defect returning. The `ContentJson` column now exists in your real database, so
    the additive migration is proven outside its fixture.
  - ⚠ The remaining 5%: **14 transcript rows, 0 coded**. No message has been written since the fix,
    so a NEW coded row rendering in Hindi has still not been observed.
- 🔶 **`REQ-RAG-052` — FIXED, and it leaves you one job: RE-INGEST THE WHOLE CORPUS.**
  `EmbeddedEmbeddingProvider` was feeding **raw SentencePiece ids** to an XLM-RoBERTa graph — no
  fairseq `+1` shift, and no `<s>`/`</s>` wrapper at all. Now both. Cross-lingual similarity went
  from **0.3536, losing to an irrelevant passage at 0.3642**, to **0.7182**. English always looked
  fine, which is exactly how it hid: a *consistent* id shift preserves lexical-overlap signal and
  destroys semantics.
  - **Vectors embedded before 2026-08-04 are in a different space from ones embedded after.** Cosine
    similarity between the two is meaningless. **Do not top up** — a partial re-ingest mixes both and
    degrades retrieval with nothing in the logs.
  - ✅ **A stale corpus is now DETECTED, not trusted.** Vectors carry a
    `{provider}/{model}/r{revision}` stamp; the document library shows a localized warning naming how
    many documents are affected, and a louder one if the corpus is MIXED. **The revision is what
    matters** — this defect changed neither provider nor model. An unstamped document counts as
    stale, and a provider with no signature reports "cannot determine" rather than a clean result.
    18 tests, 6 of them end-to-end against a real store.
  - ⚠ It retroactively qualifies `REQ-RAG-003` / `REQ-RAG-004`'s "runtime-confirmed" remarks — the
    embedder ran, but not correctly, until today. Full write-up: **TR-RAG-044**.
- 🔴 **`REQ-FN-053` — I could NOT reproduce it, and the negative result is the finding.** Driven
  through the real `FlowRunner` with a prompt answered LATE from another thread — what a dialog
  actually does — a Tool-node run **completes on both answers**, trace past `NodeStarted`. So
  `EgressGate` + `FlowRunner` + `GuardedToolHandler` are **not** the fault, and the suspect list moves
  up: the Razor/BlazorWebView dispatcher, or the tool itself — **both reported tool names (`web_fetch`,
  `sql-query`) are skills `REQ-RAG-022` records as shipping no provider.** Needs the running Catalyst
  head; I cannot drive it from here.
  - ⚠ **Why the suite never caught it:** every existing test on this REQ answered the prompt
    **synchronously**, so the `TaskCompletionSource` was already complete and the resume path never
    ran. A deferred-answer double now covers it. Worth remembering as a pattern, not a one-off.
  - ✅ **A real defect did fall out of the hunt:** the flows screen called `RunAsync` with **no
    cancellation token at all**, while a chat turn runs under the agent's time limit — so any stall
    hung forever with no exception and a button stuck on "running". The service now derives the same
    limit and renders a timeout reason. **That bounds the hang; it does not close the row.**
- 🔴 **`REQ-RAG-051` is the FOURTH lying-surface defect** (`ConfirmEgress`, Save & apply, thread
  export, now `ForFlow` returning `IsSuccess = true` for a blocked run so the trace renders green).
  Four instances makes it a systemic pattern, not incidental.
- 🔴 **THE ENVIRONMENT IS STILL THE CEILING — but it is now a CONFIGURATION question, not a search.**
  Per your direction this pass, the gates no longer probe `:11434` / `:1234` / the default Docker
  socket and then write *"no LLM provider is reachable on this host"* into a REQ. Endpoints are
  **declared** in `.tfcore/core-config.yaml → runtimeVerification.services`; the rule is in
  `_smoke-test-policy.md` (unset = honest `⚠ STATIC-ONLY`, set-but-down = a REAL failure, secrets by
  env-var name only since that file is committed). **Every key ships commented out** — I did not
  enable the historical `192.168.1.13:1234` / `192.168.1.14:5101` values, because an endpoint nobody
  confirmed is a guess with extra steps. **Filling that block in is the single highest-leverage thing
  you can do**: it is what unblocks the 44 `Needs re-verify` rows. How: `docs/VERIFICATION-ENDPOINTS.md`.
  - The first consumer is already wired: `LivePgVectorStoreTests` (5 tests) runs the moment
    `TechieRagTestPostgres` is set, and **skips visibly** until then.
- ⚠ **`REQ-UI-053` now has a measured answer, and it is worse than "open question":** **0 of 23**
  sidebar identifiers reach the macOS AX tree. The sweep runs *entirely* on the resource-key label
  fallback, which makes `REQ-NFR-014`'s no-English-selector guard load-bearing today.
- ⚠ **`REQ-NFR-015`: `/register` and `/setup` have never been swept by anything** — arrival markers
  exist and look deliberate, but the driver loop iterates `SIDEBAR` and neither screen has a row.
- ⚠ **The licence banner has still never been seen on screen**; **read-aloud in Hindi is still
  unverified on a real Mac**; **~2,900 Hindi keys remain agent-produced**.
- 🔴 **`REQ-FN-043`** Apple signing identity; **`REQ-FN-035`** Windows platform sources;
  **`REQ-NFR-001`** PAT still untracked-and-unrotated. All owner-only.

## Library feedback summary

- **TrBlazeUI — 28 entries.** TR-024 closed-with-workaround; TR-039, TR-038 open; TR-008 corrected.
- **TechieRag — 45 entries.** TR-RAG-042/043 open. **Closed this pass: TR-RAG-037** (mbox identity
  embedded the archive's file name), **TR-RAG-044** (raw SentencePiece ids into an XLM-RoBERTa graph —
  fixed in BOTH the reranker and the embedder, plus the version stamp that now detects a stale corpus)
  and **TR-RAG-045** (the reranker's first-run download could never succeed: 404 URL, missing
  external-data file, and a completeness check that was false by construction).
- `Models.AgentStep` / `ToolResult` **now carry a code slot** — `FailureMessage` was lifted from
  `FlowStep` to the base step, closing the gap this summary previously listed as open.

## Standards compliance

- Build **0 errors**; **2,282 tests pass** (1,552 app + 730 lib), 36 skipped.
- Of the 36 skipped, **5 are the new `LivePgVectorStoreTests`** and they skip with a reason naming the
  env var that turns them on — a visible gap, not a silent one.
- Service-layer English ratchet **330 → 189**, and the derivation is recorded in the constant itself:
  **118 real conversions**, plus **23 that were never debt** — the counter matched `logger.Log…` but
  not `logger?.Log…`, so log templates were being counted as prose. The instrument was fixed, not the
  code. Nobody should later read 141 as 141 conversions.
- Harness guards 8/8; markup localization 100%; greps clean (no `a`/`v`/`obj` prefixes, no underscore
  fields, no underscored test names).
- ⚠ Pre-existing: `MauiProgram.cs` `SCREAMING_SNAKE_CASE` env var; ~14 CS1591 in `TechieRagManager.cs`.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-05 | **`*build-phase` — REQ-RAG-052 banner (owner-scoped), NO verify-phase run** | ✅ **2,308 pass / 0 fail.** 🟢 **The verifier's RENDER-FAIL is closed and PROVEN ON SCREEN**: the stale-corpus banner renders live in Hindi — *"5 of 5 documents here were indexed with a different embedding model"* (content nodes 148 → 151, 0 render defects, both widths). 🔴 **Root cause was MINE, and it silently disabled the feature for most installs**: `EmbeddingSignature` carries an `unknown` DEFAULT so external implementers keep compiling (REQ-NFR-007), and I had given the override to `EmbeddedEmbeddingProvider` **alone** — while this install runs **Ollama** (`EmbeddingSource` ordinal 2; the enum is `Onnx=0, Embedded=1, Ollama=2`). Signature `unknown` → `IsDeterminable` false → banner suppressed with no error anywhere. All 8 remaining built-in providers now publish `Signature(Name, ModelName)`. ⚠ **Three hypotheses were disproven before the right one, and one of my own fixes was INERT** — an explicit-interface-implementation change passed its mutation test *unchanged*, proving it fixed nothing; it was reverted rather than shipped as a fake fix. The answer came from **instrumenting the running page** and reading the app's own log: `wsDocs=5 catalog=14 matched=5 signature=unknown` — the list and lookups were right all along. **Guard**: `ProviderSignatureCoverageTests` reflects over every `IEmbeddingProvider` and fails when one inherits the default — RED when Ollama's override is removed, GREEN restored. Probes removed; harness torn down; app language restored to `en`. ⚠ **`REQ-FN-053` was in the confirmed scope and was NOT started** — said plainly rather than quietly dropped | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | **`*verify` ×6 (owner-invoked, SECOND run — consent granted)** | ✅ **2,306 pass / 0 fail**, harness guards 8/8. 🟢 **THE CONSENT GRANT PAID FOR ITSELF IMMEDIATELY.** `WebDriverAgentMac` came up on :10100 and screens were driven for the first time this session — 3 swept in **Hindi** at 1600 and the 1024 floor, 0 render defects. 🔴 **And the first screen graded produced a defect no test could have**: `REQ-RAG-052`'s stale-vector banner **is not on screen**, on a library of 5 documents provably embedded by the OLD tokenizer (`Metadata '{}'`, unstamped ⇒ stale). Confirmed NOT a stale build — bundle assemblies 20:09, process started 21:54, `DocsStaleEmbeddingsTitle` present in the shipped assembly, checked by symbol presence rather than the `.app` directory mtime that misled me earlier today. **Downgraded 95% → `Needs re-verify` 80%**; the library half is untouched and still passes. ✅ **`REQ-UI-059` clause 2 PROVEN ON SCREEN** — five legacy English rows render verbatim amid complete Devanagari, each verified in the live store as `ContentJson NULL` dated 2026-07-30, so that English is the policy working rather than the defect returning; and the `ContentJson` column now exists in the owner's real database, proving the additive `ALTER` outside its fixture. ⚠ **0 promoted.** `REQ-UI-058` and `REQ-RAG-051` need a live **blocked tool call** that `REQ-FN-053` still prevents — the screen was finally available, the event was not; `REQ-RAG-044`/`-049` still have no declared Postgres or IMAP; `REQ-UI-059` has 14 transcript rows and **0 coded**, so the new path has never produced a row here. ⚠ The one overlap was matched to the **documented phantom**, not re-filed. ⚠ **Not done**: `TechieDesk-DevGuide-Workspace.html` was not re-rendered (§6b) — it had already been stale since 2026-07-29, and the `.md` carries this run's observations | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | **`*build-phase` — REQ-UI-058 + REQ-UI-059 (owner-scoped), NO verify-phase run** | ✅ **2,306 pass / 0 fail** (+24). **The library's localization codes finally have a reader**: `FlowMessageText` maps all **24** `FlowMessageCodes` to `AppStrings`, `AgentTrace` prefers `AgentStep.FailureMessage` over raw English in all four failure arms, and `WorkspaceFlows` resolves `BlockMessage`/`FailureMessage`. Required widening the trace entry with `DetailArguments` — the detail line resolved with **no arguments**, so a coded refusal had nowhere to put its guardrail id and fell back to English regardless. **Nested substitution included**: the guardrail's own reason is localized too, not just the framing. **Exhaustive by reflection** — a library upgrade that adds a code fails the build rather than shipping English. 🔴 **`REQ-UI-059` half one built**: the chat transcript persists **code + arguments**, legacy rows render verbatim **permanently** (that IS the migration stance — nothing deleted or rewritten), and `TrMessage` gained a `ContentJson` column with an additive `ALTER` proven against an older-database fixture, because `CREATE TABLE IF NOT EXISTS` does nothing to a table that already exists. The clause-4 guard was **mutation-tested**: reintroducing the original defect turns it RED, restoring turns it GREEN. ✅ **Half two built on a follow-up instruction**: the "model-facing, out of scope" judgement was settled by splitting the audience rather than arguing it — `SkillOutcome` carries invariant English for the model and a code for the person, with the English rendered from the SAME resource entry the code names so the two cannot drift. 10 reasons coded across six sites, 20 resx entries. **The library was not changed**: `ToolRegistry`'s delegate returns a bare string, so the app now owns its tool handler. Two boundaries kept: skill DATA carries no code, and an operator's own reason passes through untouched. ⚠ **Nothing observed on screen** — Catalyst consent still ungranted, so both rows are capped at `Implemented` | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | **`*verify REQ-RAG-025,044,049,051,052` (owner-invoked)** | ✅ **2,282 pass / 0 fail**, harness guards 8/8. **1 promoted: `REQ-RAG-025` → `Verified`** — BRD-106's local ONNX cross-encoder ranked **live** for the first time, 9 live tests **explicitly confirmed as executed, not skipped** (a gated suite that skips is indistinguishable from a pass in a summary line, which is how this row's earlier `Verified` was overstated and demoted on 2026-07-29). 🔴 **NO SCREEN WAS DRIVEN**: `WebDriverAgentMac` failed with *"Timed out while enabling automation mode"* — the macOS UI-testing consent gate, an OS privacy grant only the owner can give. §3b escalation was exhausted first (Appium up, WDA launched, bundle `lsregister`-ed, project harness used rather than hand-rolled — `drv.py` correctly surfaced the crash instead of inventing verdicts). ⚠ **4 rows held, none failed, and each for a stated reason**: `REQ-RAG-051` and `REQ-RAG-052` make **on-screen** claims nobody could observe; `REQ-RAG-044` and `REQ-RAG-049` name a real Postgres and a real IMAP mailbox that are not declared (5 Postgres tests skipped visibly). 🔻 **`REQ-RAG-052` LOWERED 100% → 95%** — its library half is proven live (0.7182 cross-lingual, 6 tests through a real `SqliteVecStore`) but the banner a person actually reads has never been seen; 100% asserted nothing was left, and something is. ⚠ **My own error**: I reported the Catalyst bundle STALE and rebuilt it on that basis — the *instrument* was wrong, not the bundle (`grep -a` cannot read a compiled `.resources` stream; it also called `DocsLoadErrorTitle` absent, which certainly exists). Caught by validating the check against keys known to pre-date the change; the bundle had been current all along and the claim was withdrawn | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | **`*build-phase` — RAG cluster (owner-scoped), NO verify-phase run** | ✅ **2,282 tests pass, 0 errors** (+57 new). **BRD-106 complete — the local ONNX cross-encoder ranked for the first time**, 7/7 live scoring tests. 🔴 **It had never been able to run**: the download URL 404'd (BAAI ships no ONNX export), the 2.27 GB external-weights file was missing from the download list, and `IsModelDownloaded()` gated on a size that an external-data export can never reach — three faults, none visible to a hermetic test (**TR-RAG-045**). 🔴 **Then it ranked English correctly and put the WORST passage first for a Hindi query** — raw SentencePiece ids into an XLM-RoBERTa graph (**TR-RAG-044**). A *consistent* off-by-one preserves lexical overlap and destroys semantics, which is why every same-language test had always passed. 🔴 **The same defect was in the BGE-M3 EMBEDDER** — 0.3536 vs 0.3642 with the wrong passage winning, now 0.7182 — so **every stored vector is stale and the corpus must be re-ingested** (`REQ-RAG-052`), and a `{provider}/{model}/r{revision}` stamp now DETECTS it, surfaced in the document library. 🔴 **`REQ-RAG-044`'s row was measured against an acceptance the owner had already WITHDRAWN** on 2026-07-31 and is corrected. ⚠ **`REQ-FN-053` did NOT reproduce**: driven with a late, off-thread answer the Tool-node run completes both ways — the fault is above the service layer. Every prior test on it answered synchronously, so the resume path had never run. A real defect fell out anyway: the flows screen ran with **no cancellation token at all**. ⚠ **My own errors**: I never re-rendered this file's HTML, so the owner saw a two-day-old dashboard; and I left three figures in it stale until they asked | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (5 parallel clusters) → `*verify all` inline** | ✅ **2,225 tests pass, 0 errors, 23/23 screens clean in Hindi at both widths, menu bar MENU-OK in BOTH languages.** 🔴 **The headline find came from the §4b screenshot gate, not a test**: the primary chat screen renders English amid complete Devanagari because the strings were persisted into `TrMessage` at write time — the same class `REQ-UI-056` had just fixed elsewhere, but on real user history (`REQ-UI-059`). 🔴 **Two clusters' "model-facing, out of scope" judgment was disproven** by the running app rendering that text verbatim. 🔴 **`REQ-UI-057`'s recorded hypothesis was wrong**: not a fixed port and not class concurrency, but `Stop()`-then-`Close()` double prefix-unregistration re-binding the port on Unix — proven by stack trace, a deterministic harness, and errno forensics that also disproved the shared-collection fix this repo had proposed. 🔴 **`REQ-UI-056` unblocked on measured evidence**: the DB held 22 synthetic rows and **zero** `FailureReason` rows, and the proposed bare-key fix would have destroyed the numbers in `"2 ingested of 2 listed"` — so it ships as code+args. 🔴 **`REQ-UI-053` measured at 0/23.** ⚠ **My own errors**: I fanned five clusters at a single-session harness and it cost two clusters their live evidence; my first sweep analysis read non-existent top-level keys and reported a **vacuous** "0 findings" before I checked the schema | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (6 parallel clusters) → `*verify all` inline** | ✅ **2,180 tests pass, 0 errors, 23/23 screens clean in Hindi.** 🔴 Service-layer English 569 → 330 and the ratchet lowered to match. `CronDescriber` solved properly — Hindi reverses three joins, so it composes from whole localizable patterns with deliberately no English default overload. 🔴 **A live pre-existing defect found**: read-aloud spoke with no locale, so Hindi answers were **silently skipped by an English voice**. ⚠ **Three mutations came back GREEN and were reported** — including a `.resx` heredoc that silently never applied | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (2 clusters) → `*verify all` inline** | ✅ **2,073 tests pass.** 🔴 `REQ-UI-054` root cause proven and it was **not** the title collision hypothesised — `UIMenuBuilder` discards an entire menu that re-declares a system-owned key. 🔴 `REQ-UI-051` services now return invariant keys. ⚠ **Three recorded claims were wrong and were corrected** | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-01 | **`*build-phase` (4 clusters) → `*verify all` inline** | ✅ **2,055 tests pass, 23/23 screens clean in English AND Hindi.** 🔴 Markup localization **100%**, counter widened on purpose. 🔴 **`REQ-UI-053` DISPROVEN by probe.** ⚠ `/settings/backup` had shipped for days and had never been graded | [table](docs/TechieDesk-Checklist.md#requirements-status) |

## Deferred / future

- **`REQ-RAG-045/046`** (BRD-deferred by owner decision); **`REQ-RAG-044`**'s `PgVectorStore` test —
  needs Docker *installed*, not merely started.
- The `AgentToolHandler` / `EgressGate` audience split at the render seam (now `REQ-UI-059`).
- `Models.AgentStep` / `ToolResult` code carriers, so an Agent node's duplicate refusal row can
  localize.
- A `drv.py` revive-and-retry wrapper — three clusters independently rediscovered that WDA drops the
  session between nearly every command on this host.
- `run_sweep` sidebar selectors only work at `nav.DESK` (1600×1240); at 1440×980 the rail collapses
  and every selector misses with a confusing "link NOT FOUND".
