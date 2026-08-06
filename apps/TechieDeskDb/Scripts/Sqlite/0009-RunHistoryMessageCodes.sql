-- 0009-RunHistoryMessageCodes (SQLite) — REQ-UI-056 (BRD-91), amending 0005-Scheduling.
--
-- WHAT THIS IS. Three nullable companion columns so a run-history sentence can be STORED as a
-- resource code plus the values its holes take, and RENDERED in whatever language the reader has
-- selected, however long after the run happened.
--
-- WHY A COMPANION COLUMN AND NOT A REPLACEMENT. "Detail", "FailureReason" and "Reason" hold English
-- sentences written by every release up to this one. Those rows exist on installed machines, they
-- are the user's run history, and nothing can retroactively make them translatable. Dropping or
-- rewriting the columns would erase that history; making the new columns NOT NULL would refuse to
-- migrate a database that has any of it. So the pair is: the coded column when the row has one, and
-- the text column when it does not — TechieDesk.Services.Scheduling.JobMessage.Render is the single
-- place that decides, and its null-code branch is documented as PERMANENT rather than transitional.
--
-- WHY A CODE PLUS ARGUMENTS AND NOT A BARE KEY. The persisted sentences are parameterized:
-- "2 ingested of 2 listed", "Added to workspace 09ed1034-…", "Unchanged since the previous run
-- (version …)". A bare key such as "AutoRunItemIngested" cannot reproduce the numbers or the names,
-- so the unit that is stored has to carry both. The JSON is an ARRAY of segments, each
-- {"code":…,"args":[…]}, because three of these sentences are composed from more than one clause
-- ("3 processed · 1 failed") and enumerating every combination would multiply the codes a translator
-- has to be shown. Arguments are strings formatted invariantly at capture time, so a stored row
-- means the same thing whoever later reads it.
--
-- WHY THE ENGLISH IS STILL WRITTEN. A new row gets BOTH: the codes here, and the English rendering
-- in the pre-existing text column. The scheduler helper host logs the detail line, support reads
-- these rows in a database browser, and a code retired in a future release would otherwise leave a
-- blank. It also keeps the fallback above a live path on every install rather than a branch only old
-- data ever takes.
--
-- WHY JSON RATHER THAN A CHILD TABLE. This is display text for one row, read only with that row,
-- never queried across rows and never joined — the same judgement 0008-Flow.sql records for
-- DefinitionJson. A ScheduleRunItemSegment table would add a join to every run-details dialog to
-- store on average one segment.
--
-- Naming per docs/TechieRag-Coding-Standards.md; the "Json" suffix follows the DefinitionJson
-- precedent in 0008-Flow.sql and is honest about what the column holds.
--
-- NOT IDEMPOTENT BY CONSTRUCTION, AND THAT IS SQLITE, NOT CARELESSNESS. SQLite has no
-- "ALTER TABLE … ADD COLUMN IF NOT EXISTS". DbUp journals applied scripts by NAME in
-- SchemaVersions, so this file runs exactly once against any given database; re-running it by hand
-- is what would fail, and the earlier scripts' IF NOT EXISTS guards protect against a different
-- hazard (CREATE statements duplicated across the concurrently-numbered 0003 pair).

ALTER TABLE "ScheduleRun" ADD COLUMN "DetailJson" TEXT NULL;

ALTER TABLE "ScheduleRun" ADD COLUMN "FailureReasonJson" TEXT NULL;

ALTER TABLE "ScheduleRunItem" ADD COLUMN "ReasonJson" TEXT NULL;
