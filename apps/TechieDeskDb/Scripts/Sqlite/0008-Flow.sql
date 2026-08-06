-- 0008-Flow (SQLite) — REQ-UI-040 (BRD-92), on the REQ-RAG-042 orchestration framework.
--
-- WHAT THIS IS. REQ-RAG-042 shipped the whole flow model in the library — nodes, edges, conditions,
-- handoffs, guardrails, a validator and a runner — and deliberately shipped NO storage: FlowSerializer
-- is documented as "the app owns persistence; the library owns the document format". This is the app's
-- half. Without it a flow lives only as long as the page that composed it, which is not a builder.
--
-- WHY ONE JSON COLUMN AND NOT A TABLE PER CONCEPT. FlowDefinition is an object graph — a list of
-- nodes, each with a kind-dependent set of optional properties, plus edges carrying optional
-- conditions, plus two uninterpreted metadata bags. Normalising it would mean five tables, a
-- migration every time the library adds a node property, and a reader in this codebase that has to
-- agree with FlowSerializer about what a flow IS. It would also make an exported flow and a stored
-- flow two different formats. So DefinitionJson holds FlowSerializer.ToJson output VERBATIM and is
-- the single source of truth; the library owns the shape and this table owns the row.
--
-- WHY Name AND Description ARE MIRRORED INTO COLUMNS ANYWAY. The list screen sorts and filters by
-- name. Doing that over the JSON blob would mean parsing every stored flow to paint one list, and
-- would make the ordering of a corrupt row undefined. The columns are a projection FOR THE LIST, not
-- a second source of truth: a save always rewrites both from the definition it just serialized.
--
-- WHY SchemaVersion IS A COLUMN. FlowSerializer refuses a document written by a NEWER library than
-- the one reading it, which is right — a flow that silently loses a node is a flow that quietly does
-- something else. But a list screen must be able to say "this flow was written by a newer version"
-- WITHOUT parsing every blob to find out. The column is that cheap check. The blob still carries its
-- own version and the parser still enforces it; this is a projection, exactly like Name.
--
-- WHY THERE IS NO FOREIGN KEY TO Workspace. Consistent with WorkspaceMcpServer and WorkspaceAgent:
-- workspace identity is a TEXT id owned by the workspace service, and SQLite foreign keys are OFF by
-- default in this connection string, so a declared constraint would document an enforcement that is
-- not happening. Scoping is enforced in every query — WorkspaceId is in the WHERE clause of every
-- read, update and delete, never a filter applied after the fact — so one workspace's flows cannot be
-- listed, edited, run or deleted from another.
--
-- WHY IsEnabled EXISTS SEPARATELY FROM "does it validate". A flow can be structurally perfect and
-- something the operator does not want runnable yet. Deleting it to achieve that loses the work; this
-- is the switch that does not.
--
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, index = IX{Table}{Column}. Every statement is IF NOT EXISTS, so re-applying against
-- a database that already has the table is a no-op.

CREATE TABLE IF NOT EXISTS "Flow" (
    "FlowId"         TEXT    NOT NULL PRIMARY KEY,
    "WorkspaceId"    TEXT    NOT NULL,
    "Name"           TEXT    NOT NULL,
    "Description"    TEXT    NULL,
    "DefinitionJson" TEXT    NOT NULL,
    "SchemaVersion"  INTEGER NOT NULL DEFAULT 1,
    "IsEnabled"      INTEGER NOT NULL DEFAULT 1,
    "CreatedAtUtc"   TEXT    NOT NULL,
    "UpdatedAtUtc"   TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS "IXFlowWorkspaceId"
    ON "Flow" ("WorkspaceId");
