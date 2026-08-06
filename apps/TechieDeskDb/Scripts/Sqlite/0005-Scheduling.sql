-- 0005-Scheduling (SQLite) — REQ-FN-042 / REQ-FN-028 / REQ-FN-020 / REQ-UI-046 (BRD-139, BRD-93,
-- BRD-65, BRD-140).
--
-- Numbered 0005, not 0004: two other migrations were added concurrently and both landed on 0003
-- (AgentRegistry, EventLogCorrelation). DbUp applies in name order and journals by name, so the
-- duplicate pair is harmless, but 0004 is the number one of them would naturally move to. Taking
-- 0005 leaves that resolution room. Every statement below is IF NOT EXISTS, so re-applying this
-- script against a database that already has these tables is a no-op rather than an error.
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, FK = Fk{Table}{Ref}, unique = Uc{Table}{Column}, index = IX{Table}{Column}.
--
-- WHY THREE TABLES AND NOT A JOB DATABASE. BRD-139 rules out a job server or job database, and this
-- is not one: there is no queue, no lease, no worker registry and no listening port. "Schedule" is
-- the user's saved intent, "ScheduleRun" is what happened, and "ScheduleRunItem" is the per-item
-- detail BRD-65 requires. A run that reports "412 of 500 ingested" without naming the 88 that failed
-- and why is indistinguishable from silent data loss, which is exactly the defect BRD-65 exists to
-- prevent — so the per-item reasons get a table rather than a log line.
--
-- WHY THE PLAIN-LANGUAGE TEXT IS STORED, NOT DERIVED. BRD-140 forbids cron appearing in any grid,
-- list or notification. "ScheduleText" is the human sentence ("Every weekday at 07:00") and it is
-- persisted, not re-derived at render time, because it is the text the user CONFIRMED. If a later
-- release improves the describer, an existing schedule must keep displaying the words its owner
-- agreed to, not a new phrasing of them.
--
-- "SourceInstruction" keeps what the user actually typed. It is the only way to answer "why does
-- this automation exist" a year later, and it is what a re-interpretation would be diffed against.
--
-- NULLABLE ScheduleId ON ScheduleRun IS DELIBERATE. REQ-FN-020 connector runs are started by hand
-- from the connector screen as well as by a schedule; both are the same background job with the same
-- progress and the same per-item results, so they share one history table. A hand-started run simply
-- has no schedule behind it. ON DELETE SET NULL keeps the history when a schedule is deleted —
-- deleting an automation must not erase the record of what it did.

CREATE TABLE IF NOT EXISTS "Schedule" (
    "ScheduleId"        INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name"              TEXT    NOT NULL,
    "JobKind"           TEXT    NOT NULL,
    "JobPayload"        TEXT    NULL,
    "ActionSummary"     TEXT    NOT NULL,
    "CronExpression"    TEXT    NOT NULL,
    "TimeZoneId"        TEXT    NOT NULL,
    "ScheduleText"      TEXT    NOT NULL,
    "SourceInstruction" TEXT    NULL,
    "IsEnabled"         INTEGER NOT NULL DEFAULT 1,
    "CatchUpMissedRuns" INTEGER NOT NULL DEFAULT 1,
    "NotifyOnFailure"   INTEGER NOT NULL DEFAULT 1,
    "LastRunUtc"        TEXT    NULL,
    "NextRunUtc"        TEXT    NULL,
    "CreatedUtc"        TEXT    NOT NULL,
    "UpdatedUtc"        TEXT    NOT NULL,
    CONSTRAINT "UcScheduleName" UNIQUE ("Name")
);

CREATE INDEX IF NOT EXISTS "IXScheduleNextRunUtc" ON "Schedule" ("NextRunUtc");

CREATE TABLE IF NOT EXISTS "ScheduleRun" (
    "ScheduleRunId"  INTEGER PRIMARY KEY AUTOINCREMENT,
    "ScheduleId"     INTEGER NULL,
    "JobName"        TEXT    NOT NULL,
    "JobKind"        TEXT    NOT NULL,
    "TriggerKind"    TEXT    NOT NULL,
    "StartedUtc"     TEXT    NOT NULL,
    "CompletedUtc"   TEXT    NULL,
    "Outcome"        TEXT    NOT NULL,
    "ItemsProcessed" INTEGER NOT NULL DEFAULT 0,
    "ItemsFailed"    INTEGER NOT NULL DEFAULT 0,
    "ItemsSkipped"   INTEGER NOT NULL DEFAULT 0,
    "Detail"         TEXT    NULL,
    "FailureReason"  TEXT    NULL,
    CONSTRAINT "FkScheduleRunSchedule" FOREIGN KEY ("ScheduleId")
        REFERENCES "Schedule" ("ScheduleId") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS "IXScheduleRunStartedUtc" ON "ScheduleRun" ("StartedUtc");
CREATE INDEX IF NOT EXISTS "IXScheduleRunScheduleId" ON "ScheduleRun" ("ScheduleId");

CREATE TABLE IF NOT EXISTS "ScheduleRunItem" (
    "ScheduleRunItemId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ScheduleRunId"     INTEGER NOT NULL,
    "ItemId"            TEXT    NOT NULL,
    "ItemName"          TEXT    NOT NULL,
    "Status"            TEXT    NOT NULL,
    "Reason"            TEXT    NULL,
    "RecordedUtc"       TEXT    NOT NULL,
    CONSTRAINT "FkScheduleRunItemScheduleRun" FOREIGN KEY ("ScheduleRunId")
        REFERENCES "ScheduleRun" ("ScheduleRunId") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IXScheduleRunItemScheduleRunId" ON "ScheduleRunItem" ("ScheduleRunId");
