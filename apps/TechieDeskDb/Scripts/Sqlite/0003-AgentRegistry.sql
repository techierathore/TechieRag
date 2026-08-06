-- 0003-AgentRegistry (SQLite) — REQ-UI-045 (named user-defined agents), REQ-RAG-021 (@handle
-- invocation) and REQ-RAG-022 (per-workspace skill toggles over the library's ITools).
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, unique = Uc{Table}{Column}, index = IX{Table}{Column}, FK = Fk{Table}{Ref}.
--
-- WHY THREE TABLES AND NOT ONE. The permission model has two independent levels and they must be
-- stored independently or the precedence rule cannot hold:
--
--   * "WorkspaceSkill"      — the workspace SKILL CATALOGUE. The outer boundary: what is permitted
--                             in this workspace at all (BRD-84). Absent row = the skill's shipped
--                             default, so a workspace that has never been touched still behaves.
--   * "WorkspaceAgent"      — a named agent: handle, instructions, model, knowledge scope and
--                             guardrails (BRD-138).
--   * "WorkspaceAgentSkill" — which catalogue skills THIS agent selects.
--
-- The set an agent may actually call is the INTERSECTION of the last two, computed at run time by
-- AgentSkillResolver — never a copy taken when the agent was saved. That is why the agent's
-- selection is stored as its own rows rather than being flattened into "WorkspaceAgent": revoking a
-- catalogue skill has to take effect for every agent on the next turn without rewriting any agent.
--
-- "UsesEveryEnabledSkill" is how the built-in @agent means "all enabled skills" — it follows the
-- catalogue as the catalogue changes, instead of being seeded with a snapshot of it that silently
-- goes stale the moment a new skill is enabled.
--
-- FOREIGN KEYS: SQLite does not enforce them unless PRAGMA foreign_keys=ON is set per connection,
-- which this app does not set. The constraint below is therefore documentation of intent, and
-- AgentRepository.DeleteAsync deletes the child rows EXPLICITLY rather than trusting a cascade that
-- would silently not fire.

CREATE TABLE IF NOT EXISTS "WorkspaceAgent" (
    "WorkspaceAgentId"      INTEGER PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId"           TEXT NOT NULL,
    "Handle"                TEXT NOT NULL,
    "DisplayName"           TEXT NOT NULL,
    "Description"           TEXT NULL,
    "Instructions"          TEXT NULL,
    "Model"                 TEXT NULL,
    "KnowledgeScope"        TEXT NOT NULL,
    "UsesEveryEnabledSkill" INTEGER NOT NULL DEFAULT 0,
    "RestrictToPinned"      INTEGER NOT NULL DEFAULT 0,
    "AllowGeneralKnowledge" INTEGER NOT NULL DEFAULT 0,
    "MaxToolCalls"          INTEGER NOT NULL DEFAULT 8,
    "TimeLimitSeconds"      INTEGER NOT NULL DEFAULT 90,
    "ShowTrace"             INTEGER NOT NULL DEFAULT 1,
    "ConfirmEgress"         INTEGER NOT NULL DEFAULT 1,
    "AllowFollowUp"         INTEGER NOT NULL DEFAULT 0,
    "IsBuiltIn"             INTEGER NOT NULL DEFAULT 0,
    "CreatedAt"             TEXT NOT NULL,
    "UpdatedAt"             TEXT NOT NULL,
    "LastUsedAt"            TEXT NULL,
    CONSTRAINT "UcWorkspaceAgentWorkspaceIdHandle" UNIQUE ("WorkspaceId", "Handle")
);

CREATE INDEX IF NOT EXISTS "IXWorkspaceAgentWorkspaceId" ON "WorkspaceAgent" ("WorkspaceId");

CREATE TABLE IF NOT EXISTS "WorkspaceAgentSkill" (
    "WorkspaceAgentSkillId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "WorkspaceAgentId"      INTEGER NOT NULL,
    "SkillName"             TEXT NOT NULL,
    CONSTRAINT "UcWorkspaceAgentSkillWorkspaceAgentIdSkillName" UNIQUE ("WorkspaceAgentId", "SkillName"),
    CONSTRAINT "FkWorkspaceAgentSkillWorkspaceAgent" FOREIGN KEY ("WorkspaceAgentId")
        REFERENCES "WorkspaceAgent" ("WorkspaceAgentId") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IXWorkspaceAgentSkillWorkspaceAgentId"
    ON "WorkspaceAgentSkill" ("WorkspaceAgentId");

CREATE TABLE IF NOT EXISTS "WorkspaceSkill" (
    "WorkspaceSkillId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId"      TEXT NOT NULL,
    "SkillName"        TEXT NOT NULL,
    "IsEnabled"        INTEGER NOT NULL,
    "UpdatedAt"        TEXT NOT NULL,
    CONSTRAINT "UcWorkspaceSkillWorkspaceIdSkillName" UNIQUE ("WorkspaceId", "SkillName")
);

CREATE INDEX IF NOT EXISTS "IXWorkspaceSkillWorkspaceId" ON "WorkspaceSkill" ("WorkspaceId");
