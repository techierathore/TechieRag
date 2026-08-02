-- 0003-EventLogCorrelation (SQLite) — REQ-UI-026 / BRD-73.
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, unique = Uc{Table}{Column}, index = IX{Table}{Column}.
--
-- WHY: the event log grid only ever carried a one-line summary and there was no way to reach the
-- record behind it. The 2026-07-26 UI-design amendment gives every row a Details view whose third
-- tab is "Related events" — the other events belonging to the same job or operation. That view is
-- keyed on a correlation id, and the P1 schema in 0001 has no column to key it on, so one line of
-- audit data cannot be tied to the lines either side of it.
--
-- ADDITIVE AND NULLABLE: existing rows keep their meaning — a row written before this column
-- existed genuinely has no correlation, and the Details view says exactly that rather than
-- inventing a group for it. Nothing is rewritten and nothing is dropped.
--
-- The index carries the "Related events" lookup, which is an equality match on this column alone.

ALTER TABLE "EventLog" ADD COLUMN "CorrelationId" TEXT NULL;

CREATE INDEX IF NOT EXISTS "IXEventLogCorrelationId" ON "EventLog" ("CorrelationId");
