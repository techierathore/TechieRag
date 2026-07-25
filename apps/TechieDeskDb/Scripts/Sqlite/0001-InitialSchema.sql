-- 0001-InitialSchema (SQLite) — TechieDesk P1 app-owned schema (BRD-104).
-- The TechieRag library self-manages its own Tr* tables; they are NOT created here.
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, unique = Uc{Table}{Column}, index = IX{Table}{Column}.

CREATE TABLE IF NOT EXISTS "WorkspaceAssignment" (
    "WorkspaceAssignmentId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId"           TEXT NOT NULL,
    "UserId"                TEXT NOT NULL,
    "RoleName"              TEXT NOT NULL,
    "CreatedAt"             TEXT NOT NULL,
    CONSTRAINT "UcWorkspaceAssignmentWorkspaceIdUserId" UNIQUE ("WorkspaceId", "UserId")
);

CREATE INDEX IF NOT EXISTS "IXWorkspaceAssignmentUserId" ON "WorkspaceAssignment" ("UserId");

CREATE TABLE IF NOT EXISTS "InstanceSetting" (
    "SettingKey"   TEXT NOT NULL,
    "SettingValue" TEXT NOT NULL,
    "UpdatedAt"    TEXT NOT NULL,
    CONSTRAINT "PkInstanceSetting" PRIMARY KEY ("SettingKey")
);

CREATE TABLE IF NOT EXISTS "EventLog" (
    "EventLogId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "OccurredAt" TEXT NOT NULL,
    "Category"   TEXT NOT NULL,
    "Actor"      TEXT NOT NULL,
    "EventName"  TEXT NOT NULL,
    "Detail"     TEXT NULL,
    "Source"     TEXT NULL
);

CREATE INDEX IF NOT EXISTS "IXEventLogOccurredAt" ON "EventLog" ("OccurredAt");
CREATE INDEX IF NOT EXISTS "IXEventLogCategory" ON "EventLog" ("Category");

CREATE TABLE IF NOT EXISTS "GdprRequest" (
    "GdprRequestId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "UserId"        TEXT NOT NULL,
    "RequestType"   TEXT NOT NULL,
    "Status"        TEXT NOT NULL,
    "RequestedAt"   TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS "IXGdprRequestUserId" ON "GdprRequest" ("UserId");

CREATE TABLE IF NOT EXISTS "LicenseCache" (
    "LicenseCacheId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "UserId"         TEXT NOT NULL,
    "PayloadJson"    TEXT NOT NULL,
    "ValidatedAt"    TEXT NOT NULL,
    CONSTRAINT "UcLicenseCacheUserId" UNIQUE ("UserId")
);
