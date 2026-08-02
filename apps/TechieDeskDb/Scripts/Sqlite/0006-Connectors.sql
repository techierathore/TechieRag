-- 0006-Connectors (SQLite) — REQ-RAG-019 / REQ-RAG-020 (BRD-63, BRD-64), on the REQ-FN-020 job layer.
--
-- WHAT THIS IS. REQ-RAG-032 gave the library connectors (repository, Confluence) that take their
-- configuration as an options object and their credential as an in-memory string. REQ-FN-020 gave the
-- app a job that runs one. Neither side saves anything, so a connector could be run but never kept.
-- These three tables are the "kept" half: the saved connector, what its last run saw, and which
-- catalogue document each of its items became.
--
-- WHY THE CREDENTIAL IS NOT HERE. There is deliberately no token column. A GitHub PAT and a
-- Confluence API token are exactly the secrets REQ-FN-039 exists for, so the value lives in the OS
-- credential store (Keychain / Credential Manager) and "CredentialRef" holds only a NAME —
-- "secret:connector:<id>" — which is useless to anyone who copies this file. A column called
-- AccessToken, even "encrypted", would have made the database a credential store, and every backup,
-- every support bundle and every SELECT * would carry it.
--
-- WHY SETTINGS IS ONE JSON COLUMN AND NOT A COLUMN PER FIELD. A repository connector needs a project
-- path, a branch and two glob lists; a Confluence connector needs a base URL, a space key and a page
-- flag; the next connector will need something else again. One nullable column per field per source
-- type is a schema that grows with the catalogue and is mostly NULL. The shape that varies by type is
-- held as the connector cluster's own JSON, and the columns are only the things the APP queries:
-- which type it is, what it is called, and where its documents go.
--
-- WHY SYNC STATE IS A TABLE AND NOT A FILE. ConnectorSyncState is what makes the second run of a
-- 4,000-file repository cheap; the library states plainly that it does not persist it and that
-- whoever stores the connector's configuration is the right owner. Losing it on restart would mean
-- re-downloading and re-embedding an entire source on every launch, which is not a performance
-- regression — it is a rate-limit exhaustion.
--
-- WHY ConnectorItemDocument EXISTS. Ingestion has no upsert: IngestTextAsync always creates a NEW
-- catalogue document. Without a record of "item X is currently document Y", re-syncing three changed
-- files turned 9 catalogue documents into 12, and the user's search results filled with superseded
-- copies of the same file. This table is that record, and it is what lets the sink delete the
-- superseded document at the moment it writes the replacement.
--
-- Naming per docs/TechieRag-Coding-Standards.md: PascalCase, singular, no underscores,
-- PK = {Table}Id, FK = Fk{Table}{Ref}, unique = Uc{Table}{Column}, index = IX{Table}{Column}.
-- Every statement is IF NOT EXISTS, so re-applying against a database that already has these tables
-- is a no-op rather than an error.

CREATE TABLE IF NOT EXISTS "Connector" (
    "ConnectorId"   TEXT    NOT NULL PRIMARY KEY,
    "ConnectorType" TEXT    NOT NULL,
    "DisplayName"   TEXT    NOT NULL,
    "WorkspaceId"   TEXT    NULL,
    "Pinned"        INTEGER NOT NULL DEFAULT 0,
    "Settings"      TEXT    NOT NULL,
    "CredentialRef" TEXT    NULL,
    "CreatedUtc"    TEXT    NOT NULL,
    "UpdatedUtc"    TEXT    NOT NULL,
    CONSTRAINT "UcConnectorDisplayName" UNIQUE ("DisplayName")
);

CREATE INDEX IF NOT EXISTS "IXConnectorConnectorType" ON "Connector" ("ConnectorType");

CREATE TABLE IF NOT EXISTS "ConnectorSync" (
    "ConnectorId"  TEXT NOT NULL PRIMARY KEY,
    "LastRunUtc"   TEXT NULL,
    "ItemVersions" TEXT NOT NULL,
    "UpdatedUtc"   TEXT NOT NULL,
    CONSTRAINT "FkConnectorSyncConnector" FOREIGN KEY ("ConnectorId")
        REFERENCES "Connector" ("ConnectorId") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "ConnectorItemDocument" (
    "ConnectorItemDocumentId" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ConnectorId"             TEXT NOT NULL,
    "ItemId"                  TEXT NOT NULL,
    "DocumentId"              TEXT NOT NULL,
    "IngestedUtc"             TEXT NOT NULL,
    CONSTRAINT "UcConnectorItemDocumentItemId" UNIQUE ("ConnectorId", "ItemId"),
    CONSTRAINT "FkConnectorItemDocumentConnector" FOREIGN KEY ("ConnectorId")
        REFERENCES "Connector" ("ConnectorId") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IXConnectorItemDocumentConnectorId"
    ON "ConnectorItemDocument" ("ConnectorId");
