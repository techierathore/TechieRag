-- 0002-DropWorkspaceAssignment (SQLite) — REQ-FN-041 / REQ-FN-008 (retired 2026-07-26).
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, unique = Uc{Table}{Column}, index = IX{Table}{Column}.
--
-- WHY: the 2026-07-26 desktop-only amendment made TechieDesk single-user — one install serves one
-- person, who is always the built-in Admin. User<->workspace membership has no meaning in that
-- model, so REQ-FN-008 was retired and REQ-FN-041 deleted the code that read this table
-- (WorkspaceAssignmentRepository, IWorkspaceAssignmentRepository, WorkspaceAssignment) along with
-- the role/capability/authz stack that decided who could see which workspace. WorkspaceService now
-- lists every workspace unconditionally, pinned by WorkspaceListingTests.
--
-- DESTRUCTIVE, DELIBERATELY: this drops rows rather than orphaning them. Any surviving row maps a
-- workspace to the single local owner, which is exactly what listing now assumes without asking, so
-- nothing readable is lost. Workspaces themselves are owned by the TechieRag library's own Tr*
-- tables and are NOT touched here — only the membership mapping goes.
--
-- The UNIQUE constraint "UcWorkspaceAssignmentWorkspaceIdUserId" is declared inline in 0001 and is
-- dropped with the table; only the standalone index needs its own statement. Both use IF EXISTS so
-- the script is idempotent against a database where 0001 never created them.

DROP INDEX IF EXISTS "IXWorkspaceAssignmentUserId";

DROP TABLE IF EXISTS "WorkspaceAssignment";
