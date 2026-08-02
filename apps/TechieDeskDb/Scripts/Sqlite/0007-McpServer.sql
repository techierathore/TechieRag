-- 0007-McpServer (SQLite) — REQ-RAG-023 (BRD-86), on the REQ-RAG-038 MCP client.
--
-- WHAT THIS IS. REQ-RAG-038 shipped a complete MCP client in the library — transports, handshake,
-- tool discovery, tool invocation, a trust policy — plus InMemoryMcpServerRegistry, which is
-- explicitly documented as process-lifetime storage for hosts that have nowhere else to put this.
-- A desktop application does have somewhere: here. Without this table an administrator re-types
-- every MCP server on every launch, which makes the feature unusable rather than merely limited.
--
-- WHY REGISTRATIONS ARE WORKSPACE-SCOPED. McpServerRegistration carries a WorkspaceId and the whole
-- registry contract is workspace-keyed, because an MCP server is a capability grant: the finance
-- workspace's ledger server must not become callable from the marketing workspace's agent merely
-- because both live in one install. WorkspaceId is therefore part of the uniqueness constraint, not
-- a filter applied after the fact, so two workspaces may each register a server called "search" and
-- neither can see the other's.
--
-- WHY THERE IS NO COLUMN FOR A HEADER OR ENVIRONMENT VALUE. McpServerConfig.Headers carries the
-- bearer token for an HTTP MCP endpoint and McpServerConfig.EnvironmentVariables carries the API key
-- a stdio server reads at start-up. Those are exactly the secrets REQ-FN-039 exists for, so their
-- VALUES live in the OS credential store (or, when this build cannot reach it, in the REQ-NFR-004b
-- Data-Protection sidecar) and never in this file. "SecretKeyNames" holds only the NAMES the
-- administrator configured — "Authorization", "GithubToken" — which is what lets the screen say
-- honestly "this server has an Authorization header whose value could not be recovered from the
-- credential store; re-enter it" instead of silently sending an unauthenticated request. A column
-- called Token, even "encrypted", would have made this database a credential store, and every
-- backup and every support bundle would carry it.
--
-- WHY ARGUMENTS IS A JSON ARRAY AND NOT ONE STRING. The library describes a stdio server as an
-- executable path plus a LIST of arguments precisely so there is no shell, no quoting to get wrong
-- and no argument injection through a value containing a space or a semicolon. Storing the list as
-- a single command line here would re-introduce exactly the split-on-spaces step the library
-- refuses to perform. Same reasoning for AllowedTools and SecretKeyNames.
--
-- WHY AdvertisedTools IS CACHED. "Show the tools a registered server advertises" must not mean
-- "contact every registered server every time the Agents screen is opened" — that is unsolicited
-- egress on a page load, which REQ-NFR-008 does not permit and no operator asked for. Discovery
-- happens when the administrator presses Test connection; what it found is kept here so the screen
-- can render the tool list, with the time it was observed, while contacting nothing.
--
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, unique = Uc{Table}{Column}, index = IX{Table}{Column}. Every statement is
-- IF NOT EXISTS, so re-applying against a database that already has the table is a no-op.

CREATE TABLE IF NOT EXISTS "WorkspaceMcpServer" (
    "WorkspaceMcpServerId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId"          TEXT    NOT NULL,
    "ServerName"           TEXT    NOT NULL,
    "Transport"            TEXT    NOT NULL,
    "Command"              TEXT    NULL,
    "Arguments"            TEXT    NOT NULL DEFAULT '[]',
    "WorkingDirectory"     TEXT    NULL,
    "Endpoint"             TEXT    NULL,
    "SecretKeyNames"       TEXT    NOT NULL DEFAULT '[]',
    "CredentialRef"        TEXT    NULL,
    "AllowedTools"         TEXT    NOT NULL DEFAULT '[]',
    "TimeoutSeconds"       INTEGER NOT NULL DEFAULT 60,
    "IsEnabled"            INTEGER NOT NULL DEFAULT 1,
    "AdvertisedTools"      TEXT    NULL,
    "LastCheckedUtc"       TEXT    NULL,
    "RegisteredUtc"        TEXT    NOT NULL,
    "UpdatedUtc"           TEXT    NOT NULL,
    CONSTRAINT "UcWorkspaceMcpServerServerName" UNIQUE ("WorkspaceId", "ServerName")
);

CREATE INDEX IF NOT EXISTS "IXWorkspaceMcpServerWorkspaceId"
    ON "WorkspaceMcpServer" ("WorkspaceId");
