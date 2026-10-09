# TechieFlow feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | TechieFlow |
| Updated | 2026-10-09 (TF-005, TF-006 added) |

## Summary

6 entries: 0 blocking now, 2 open (TF-005, TF-006, both minor), 0 fixed upstream, 4 closed (TF-001 to TF-004).

Nothing is blocked. TF-001 to TF-004 were re-checked here on 2026-10-06 and closed.

## Entries

### TF-001 — the feedback-file reader marks an open entry "fixed" when an old reply line says "all others fixed" and a newer reply names a later entry

> ✅ **Closed 2026-10-06** — re-checked here: 2026-10-06: ran tf-feedback.sh Sevak on Sevak's real file (22 open, 15 fixed, 9 closed; TR-RAG-047 open) and on a copy with a dated reply block under 'Replies from TechieRag' naming TR-RAG-048: TR-RAG-048 read fixed since 2026-10-06 and TR-RAG-047 stayed open, so a new reply no longer sweeps earlier open entries.

- **Severity:** major
- **Blocks:** no — the reply for TR-RAG-048 was written inside the entry instead of under "Replies from TechieRag", and the work carried on.
- **Repro:** in `Sevak/docs/Sevak-TechieRag-Feedback.md`, the "Replies from TechieRag" section keeps an old summary verbatim that contains "All others fixed app-side". Add a reply block there naming `TR-RAG-048`, then run:
  ```text
  bash .tfcore/utils/tf-feedback.sh Sevak
  TR-RAG-047   fixed   …   (it is open; nothing replied to it)
  ```
- **Expected:** TR-RAG-047 stays open. Only entries a reply names, or entries covered by an "everything else is fixed" sentence in the same reply block, become fixed.
- **Actual:** `replied_fixed` in `tf_feedback.py` treats the whole "## Replies from …" section as one block. Its "all others fixed" rule then covers every entry up to the highest number mentioned anywhere in that section, so a new reply drags every earlier open entry with it. `tf-phase.sh start` in Sevak would then tell agents "never report TR-RAG-047 as open".
- **Encountered in:** `*triage-and-fix TechieRag` for Sevak TR-RAG-048, 2026-10-06.
- **Workaround:** the TR-RAG-048 reply lives in the entry (a status line at the top of the entry and a dated fix section at its end); no reply block names it.
- **Suggested fix:** split "## Replies from …" into its dated "### " blocks and apply the "all others fixed" rule within one block only, bounded by the ids that same block names. Also read the fix marker in the entry heading, not only in the first 2,500 characters of the body, and do not match it inside quoted or code text.

### TF-002 — a quoted "all others" sentence is still applied, and covers every id in its reply block

> ✅ **Closed 2026-10-06** — re-checked here: 2026-10-06: on a copy of Sevak's feedback file with the reply paragraph put back to quoting the July 'All others fixed app-side' line, the reader gave 23 fixed, 14 open, 9 closed; TR-RAG-010 and TR-RAG-043 read open and TR-RAG-001 fixed, the same as the reworded real file.

- **Severity:** minor
- **Blocks:** no — the reply was reworded so it no longer quotes the July line, and every entry then read correctly.
- **Repro:** add a dated block under "Replies from TechieRag" in `Sevak/docs/Sevak-TechieRag-Feedback.md` whose first paragraph names no id and quotes the old line in double quotes, followed by bullets that name ids, some still open:
  ```text
  Until now they read as settled only because of the July "All others fixed app-side" line.
  - TR-RAG-010 (one search per pinned document) ... still open.
  bash .tfcore/utils/tf-feedback.sh Sevak   ->  TR-RAG-010 fixed 2026-10-06 (and 12 more)
  ```
- **Expected:** a double-quoted phrase is never read as a mark, as the TF-001 resolution says (change 5).
- **Actual:** the blanket-sentence rule still matches inside the quotes. Because the paragraph names no id, it takes the highest id in the whole block, so every entry the block lists as still open read fixed.
- **Encountered in:** the TF-001 follow-up on 2026-10-06, re-checking 21 Sevak entries.
- **Workaround:** the paragraph now says "a blanket sentence in the July summary" without quoting it.
- **Suggested fix:** strip double-quoted phrases and code before testing for the blanket sentence too, not only before testing for the per-line fix words. Add the repro above as a regression case.

### TF-003 — the status facts say "handoff done" for a phase whose handoff never ran

> ✅ **Closed 2026-10-06** — re-checked here: 2026-10-06: ran tf-status-facts.sh TechieRag build-phase with phase 3 built and verified (11 of 11) and only phase 2's handoff on record: current_phase reads 'Handoff — 11 of 11 verified' and the next command is /TechieFlow:agents:flow-master *handoff-phase TechieRag, with the reason 'handoff has not run yet'.

- **Severity:** minor
- **Blocks:** no — PROJECT-STATUS copies the printed lines and states in "Where I am" and "Known blockers" that phase 3's handoff has not run; the work carried on.
- **Repro:** a Large library at phase 2 with `handoff-phase` run on 2026-10-04. Add phase 3, set `appPhase: 3`, build and verify all 11 rows, then:
  ```text
  bash .tfcore/utils/tf-status-facts.sh TechieRag build-phase
  current_phase: Phase 3 of 3 (…) · UAT — handoff done, 11 of 11 verified
  Next command: (owner) set current_phase to Released after UAT
  ```
- **Expected:** "build done, 11 of 11 verified", with the next command `*handoff-phase TechieRag`. The UsageGuide and DevGuide do not describe phase 3 yet.
- **Actual:** the handoff record of phase 2 counts for phase 3, so the facts skip the handoff and send the owner straight to UAT.
- **Encountered in:** `*build-phase TechieRag`, phase 3, 2026-10-06.
- **Workaround:** the status file names `*handoff-phase TechieRag` under "Known blockers".
- **Suggested fix:** count a handoff only when its run record names the current phase (or ran after the phase's checklist was created).

### TF-004 — the document-test step runs every test project at once, so a memory-heavy project makes another project's test fail falsely

> ✅ **Closed 2026-10-06** — re-checked here: 2026-10-06: tf-doc-tests.sh docs/TechieRag-AI-Reference.md ran only TechieRag.Tests (the project holding the 3 readers) and printed PASS; tf-build.sh test TechieRag.slnx ran the 3 test projects one after another: TechieRag.Tests PASS, TechieRag.Agents.Tests PASS, TechieRag.Local.Tests FAIL on its own (the known memory limit), so it no longer fails TechieRag.Tests.

- **Severity:** minor
- **Blocks:** no — the failing test was re-run alone, passed, and the run carried on.
- **Repro:** in TechieRag, on WSL with 7.8 GB, after a change to a document a test reads:
  ```text
  bash .tfcore/utils/tf-doc-tests.sh docs/TechieRag-AI-Reference.md
  FAIL ... TechieRag.Tests: Failed REQ-FN-006 SevakConsumesReleasedPackages
       System.IO.IOException : Cannot allocate memory : '/mnt/c/1MyCode/Sevak/tests/.artifacts/...'
  dotnet test tests/TechieRag.Tests --filter "DisplayName~REQ-FN-006"   ->  Passed 2 of 2
  ```
- **Expected:** each test project runs on its own and the results are merged, as `tf-verify-tests.sh --merge` already allows. A test that passes alone never reads as failed.
- **Actual:** `tf-doc-tests.sh` calls `tf-build.sh test` on the whole solution. All three test projects run together, and the local-model project takes most of the memory while TechieRag.Tests is still walking folders. Seen twice on 2026-10-06; both times the test passed alone.
- **Encountered in:** the status gate of `*build-phase` and `*fix-issues TechieRag`, 2026-10-06.
- **Workaround:** the failing test was re-run alone, and the report says the failure was memory, not code.
- **Suggested fix:** in `tf-doc-tests.sh` (and `tf-build.sh test` when the target is a solution), run each test project in turn and merge the results.

### TF-005 — a new triage row takes an id that another phase's checklist already uses

- **Severity:** minor
- **Blocks:** no — the row was renumbered by hand to REQ-RAG-128 and the run carried on.
- **Repro:** in TechieRag (phase 3, whose checklist has no REQ-FN rows; phase 1's checklist has REQ-FN-001):
  ```text
  bash .tfcore/utils/tf-triage.sh TechieRag new "<title>" "<acceptance>" --section "Model routing"
  REQ-FN-001: new Not Started row — …
  ```
- **Expected:** the next free id across every phase's checklist (and the telemetry streams), so an id names one row.
- **Actual:** `add_row` counts only the current checklist, so it gives REQ-FN-001 again. The miss `MISS-TechieRag-20261009-01` and its escaped gate record carry REQ-FN-001. `req_id` cannot be amended (SCHEMA §5.5.7), so those two records will always name the wrong row. (Leaving out `--prefix RAG`, the default `FN`, was this run's mistake, not the script's.)
- **Encountered in:** `*triage-and-fix TechieRag` for Chatur TR-RAG-006, 2026-10-09.
- **Workaround:** the checklist row and BRD were renamed to REQ-RAG-128. The miss is closed by its `miss_id`.
- **Suggested fix:** take the highest id with that prefix across `docs/{App}-Checklist.md` and every `docs/{App}-P*-Checklist.md`. On a project whose rows all use one prefix (here `RAG`), default to that prefix or refuse without `--prefix`.

### TF-006 — the verify boot picks a native MAUI sample as the Windows head and waits 300 s for a DevTools port it can never open

- **Severity:** minor
- **Blocks:** no — every row here has no screen, so the verdict graded them by their tests. All 15 passed.
- **Repro:** in TechieRag, `samples/TechieRag.Probe` is a plain MAUI app (no `AddMauiBlazorWebView`, no `BlazorWebView`):
  ```text
  bash .tfcore/utils/tf-verify-boot.sh start
  NONE head=windows kind=host reason=the app's DevTools port never answered on localhost 172.18.144.1:9223 within 300 s
  ```
  The probe started and showed its window ("TechieRag Probe", responding), and no `msedgewebview2` process belonged to it. Ran three times, about 15 minutes in all.
- **Expected:** the `windows` head is chosen only for a Blazor Hybrid project. A plain MAUI head is skipped or reported at once with "no web view, not drivable over CDP". On a library whose rows have no screen, the boot is skipped.
- **Actual:** any project with `UseMaui` and a `net*-windows` target is chosen. The script waits the full 300 s, and the second try also hits "Only one usage of each socket address" from the relay left by the first.
- **Encountered in:** `*triage-and-fix TechieRag`, the verify step, 2026-10-09. The 2026-10-08 ledger says the same head booted, which this run could not reproduce.
- **Workaround:** none needed. The verdict graded the rows by their tests, and the report says the app was not booted.
- **Suggested fix:** check for `AddMauiBlazorWebView` or a `BlazorWebView` in the project before choosing `windows`. Stop the relay on a failed start.

## Replies from TechieFlow

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->

### Resolution status (TechieFlow team, 2026-10-06, fourth reply)

| ID | Fix | Check it here |
|---|---|---|
| TF-004 | Fixed upstream in two scripts. (1) `tf-doc-tests.sh` now runs only the test projects that hold a file reading the changed document, each on its own, and merges their verdicts. A reader in no .NET test project, such as a browser spec, falls back to the solution run. (2) `tf-build.sh test` on a solution with more than one test project runs each test project on its own, through the same rungs. It merges the verdicts into one line naming one joined log: FAIL naming the projects that failed, else NOT-RUN naming those that could not run, else PASS. Each project's own line follows the verdict. A project counts as a test project when it references a test SDK (Microsoft.NET.Test.Sdk, xunit, NUnit, MSTest, TUnit) or says `IsTestProject`; a library is never run as a test. `TF_TEST_TOGETHER=1` keeps the old single run. `tf-verify-tests.sh` reads the joined log and the TRX reports as before. Regression case `tr_004` uses a stand-in `dotnet` that fails "Cannot allocate memory" when handed the whole solution: it fails against the scripts you had and passes now. Miss `MISS-TechieFlow-20261006-07`. | Proved on your repository. `bash .tfcore/utils/tf-doc-tests.sh docs/TechieRag-AI-Reference.md`: the three files that read it are all in TechieRag.Tests, so only that project ran. It printed `PASS … 1 test project(s) holding a reader, each run on its own` in 110 s, and the local-model project was not started. `bash .tfcore/utils/tf-build.sh test TechieRag.slnx` ran the three projects in turn in 193 s: TechieRag.Tests PASS, TechieRag.Agents.Tests PASS, TechieRag.Local.Tests FAIL. **That last one is this machine, not the split:** your own memory check refused the model, "phi-3-mini-4k-instruct needs about 4.6 GB of free memory, but only 3.0 GB is available", in REQ-RAG-057, 059, 063 and 065 among others. It now fails on its own and no longer takes TechieRag.Tests down with it. A suggestion for your test code, not the framework: when `LocalModelMemoryException` is thrown, skip the conformance test with that message instead of failing it, since a host short of memory is not a defect. To check: run those two commands here. |

### Resolution status (TechieFlow team, 2026-10-06, third reply)

| ID | Fix | Check it here |
|---|---|---|
| TF-003 | Fixed upstream in `tf-status-facts.py`. A handoff now counts only for the phase it ran in: a Verification log row naming handoff counts when its "Status table" cell names this phase's checklist (`<App>-Checklist.md` in phase 1, `<App>-P<n>-Checklist.md` from phase 2). An older row naming no checklist still counts in phase 1, where nothing else could be meant, and not in a later phase. No other script decides "handoff done". Regression case `tr_003` holds three cases: phase 2's handoff with phase 3 built (fails against the script you had), phase 3's own handoff, and an old phase-1 row with no checklist. Miss `MISS-TechieFlow-20261006-06`. | Proved on your repository. `bash .tfcore/utils/tf-status-facts.sh TechieRag build-phase` with your copy: `Phase 3 of 3 (Consumer feedback from Sevak) · UAT — handoff done, 11 of 11 verified`, next `(owner) set current_phase to Released after UAT`. New: `Phase 3 of 3 (Consumer feedback from Sevak) · Handoff — 11 of 11 verified`, next `*handoff-phase TechieRag`. Run on all 18 projects, only TechieRag's facts change. To check: run that command here; it prints the handoff line. The "Known blockers" note naming `*handoff-phase TechieRag` can go after the next status gate. |

### Resolution status (TechieFlow team, 2026-10-06, second reply)

| ID | Fix | Check it here |
|---|---|---|
| TF-002 | Fixed upstream in `tf_feedback.py`. In a reply, every mark is now tested on the text with fenced code, inline code and double-quoted phrases removed. That covers the "all others fixed" sentence, the fixed words and the open words. Ids are still read from the full text, since an id in backticks is still an id. A correction to the TF-001 reply: it said quoted text is never a mark, but that fix applied it to entry headings and bodies only, not to replies. That gap is what you hit. Regression case `tr_002` is your repro: a dated block whose first paragraph quotes the July line and names no id, then bullets listing entries still open and one fixed, plus a second block with an unquoted "Everything else is now fixed." that must still apply. It fails against the reader you had and passes now. Miss `MISS-TechieFlow-20261006-05`. | Proved on a copy of `Sevak/docs/Sevak-TechieRag-Feedback.md` with your paragraph put back to quoting the July line. Old reader: 36 fixed · 1 open, with TR-RAG-010 and 12 more read fixed. New: 23 fixed · 14 open, entry for entry the same as your reworded file reads today. Your real file reads the same with both readers, and so do the other 46 feedback files in the 18 projects. To check: put the quote back in that paragraph (`the July "All others fixed app-side" line`), then run `bash .tfcore/utils/tf-feedback.sh Sevak` in Sevak. TR-RAG-010 reads open. Either wording now works. |

### Resolution status (TechieFlow team, 2026-10-06)

| ID | Fix | Check it here |
|---|---|---|
| TF-001 | Fixed upstream in `tf_feedback.py`. This is the reader behind `tf-feedback.sh`, the self-check at `tf-phase.sh start`, and the status file's feedback lines. Five changes. (1) A "## Replies from …" section is split into its "### " blocks, and each is its own reply with its own date. (2) "All others fixed" covers only entries up to the highest id in its own line, and never the ids that line names, because those are the exceptions ("Open: TR-RAG-001 …; TR-RAG-002. All others fixed" fixes neither). A sentence that names no id still takes its block's ids, as before. (3) In a line that says both open and fixed, the fixed mark is read clause by clause (split at a sentence end, `;` and `·`). "1 OPEN: TR-RAG-001; 2 FIXED: TR-RAG-005" fixes 005 only. A "fixed" clause with no id answers the clause just before it ("TR-RAG-010: … Fixed in 1.1.2."). Shorthand names every id it covers: `TR-RAG-028/029/030`, `TR-RAG-031..034`, `TR-RAG-027…030`. (4) An entry's heading is read for its mark: "— **fixed upstream 2026-10-03**" is fixed, and "— ✅ **FIXED 2026-08-01**" or "✅ RESOLVED" is closed. (5) Code, inline code and double-quoted phrases are never read as a mark. Regression case `tr_001` fails 10 of 10 against the reader you had (it read every entry fixed) and passes now. Miss `MISS-TechieFlow-20261006-04`. | Proved on `Sevak/docs/Sevak-TechieRag-Feedback.md`, on a copy with a dated block naming TR-RAG-048 added under "Replies from TechieRag": TR-RAG-047 stays open and TR-RAG-048 reads fixed. **Know this before you look:** on Sevak's real file the counts change from 1 open · 38 fixed · 7 closed to 22 open · 15 fixed · 9 closed (TR-RAG-047 was already open). The 21 entries that went from fixed back to open (TR-RAG-001, 002, 009–016, 020–023, 026, 035, 036, 039, 040, 042, 043) have no fix written anywhere: not in their headings, their opening lines, or any reply. Only the July "All others fixed app-side" line had covered them. Five of them say **OPEN** in their own heading (020, 021, 022, 026, 035). TR-RAG-041 and 045 now read closed from the "✅ **FIXED**" in their headings. Across all 46 feedback files in the owner's 18 projects, nothing else changes except two `TR-001` entries whose headings say "✅ RESOLVED", which now read closed. To check: `bash .tfcore/utils/tf-feedback.sh Sevak` in Sevak. If any of those 20 is fixed in fact, write that in its entry or in a dated reply block naming it, and it will read fixed. You can move the TR-RAG-048 reply back under "Replies from TechieRag"; TR-RAG-047 stays open. |
