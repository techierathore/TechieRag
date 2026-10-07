# TechieRag — Checklist (phase 3)

| | |
|---|---|
| App | TechieRag |
| Size | Small |
| Phase | 3 of 3 |

## Goal

Fix the Sevak-reported defects still open on 2026-10-06: workspace pinning and testing, connector results on a cancel, replacing a re-ingested document, mail parse notes, duplicate tool definitions, flow step names and model use, and a stable per-app database folder when none is given. BRD-174 to BRD-184; each row names its Sevak feedback entry. This checklist is the whole work list of this phase.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-RAG-114 | Pinned-document retrieval shall run one filtered search over all of a workspace's pinned documents instead of one search per document *(Sevak feedback TR-RAG-010)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-114 PinnedDocumentsUseOneSearch` | [view](#d-req-rag-114) |
| REQ-RAG-115 | Pinned results shall follow the workspace's own rerank switch, and the API documentation shall state the rule *(Sevak feedback TR-RAG-011)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-115 PinnedResultsFollowWorkspaceRerankOn` | [view](#d-req-rag-115) |
| REQ-RAG-116 | A public `IWorkspaceManager` interface shall describe the workspace manager, `WorkspaceManager` shall implement it, and the library shall accept a stand-in through it, so a host can unit-test services built on workspaces *(Sevak feedback TR-RAG-013)* | Verified | 100% | 2026-10-06 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-116", me` | [view](#d-req-rag-116) |
| REQ-RAG-117 | A connector run that is cancelled or fails part-way shall still hand back the sync state and the per-item reasons gathered so far, so the next run resumes instead of starting over *(Sevak feedback TR-RAG-022, the part BRD-127 left open)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-117 FailedRunCarriesPartialOutcome` | [view](#d-req-rag-117) |
| REQ-RAG-118 | Each item result shall say Failed or Skipped as a typed value, byte sizes in reasons shall be formatted the same on every machine, and the metadata builder connector ingestion uses shall be public *(Sevak feedback TR-RAG-023)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-118 FetchFailureIsTypedFailed` | [view](#d-req-rag-118) |
| REQ-RAG-119 | Mail content nested past the parser's depth limit shall be reported in a new optional notes list on the parsed message, never dropped without notice; the list is an addition, so existing callers are unaffected *(Sevak feedback TR-RAG-035)* | Verified | 100% | 2026-10-06 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-119", me` | [view](#d-req-rag-119) |
| REQ-RAG-120 | Text ingestion shall take an optional caller key and replace the earlier document stored under that key; `IngestConnectorAsync` shall pass the connector's item id as the key *(Sevak feedback TR-RAG-026)* | Verified | 100% | 2026-10-06 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-120", me` | [view](#d-req-rag-120) |
| REQ-RAG-121 | Registering a tool name a second time shall replace its definition as well as its handler, so the tool list sent to the model holds each name once *(Sevak feedback TR-RAG-039)* | Verified | 100% | 2026-10-06 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-121", me` | [view](#d-req-rag-121) |
| REQ-RAG-122 | When an app gives no folder for a SQLite database, an existing `techierag.db` in the folder the app runs from shall be used, with a logged warning naming that folder. Otherwise the database shall be created in the per-user TechieRag folder the models use, under `data/<app name>/`. The location follows the models' override order (a folder set in code, then an environment variable, then the per-user default), and a public property returns it. Each app gets its own sub-folder, so two apps never share one database *(Sevak feedback TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them)* | Verified | 100% | 2026-10-06 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-122", me` | [view](#d-req-rag-122) |
| REQ-RAG-123 | `FlowNodeCatalog.CreateNode` shall take an optional step name, and its documentation shall say a host should set its own, localized name *(Sevak feedback TR-RAG-042)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-123 CreateNodeCarriesHostStepName` | [view](#d-req-rag-123) |
| REQ-RAG-124 | A flow definition shall say whether any of its steps calls a language model and list those steps *(Sevak feedback TR-RAG-043)* | Verified | 100% | 2026-10-06 verify: PASS — test `REQ-RAG-124 FlowWithoutModelStepsAnswersNo` | [view](#d-req-rag-124) |

**Status values:** `Not Started` · `In Progress` · `Implemented` · `Verified` · `Done (pre-existing)` · `Needs re-verify` · `PARTIAL` · `FAIL` · `Blocked` · `Owner-UAT` · `N/A`.

## RAG / AI requirements

<a id="d-req-rag-114"></a>
- **REQ-RAG-114** — Pinned-document retrieval shall run one filtered search over all of a workspace's pinned documents instead of one search per document *(Sevak feedback TR-RAG-010)*. *BRD:* BRD-174
  - *Acceptance:* When a developer asks a question in a workspace with five pinned documents, then one vector search covers all five.

<a id="d-req-rag-115"></a>
- **REQ-RAG-115** — Pinned results shall follow the workspace's own rerank switch, and the API documentation shall state the rule *(Sevak feedback TR-RAG-011)*. *BRD:* BRD-175
  - *Acceptance:* When a workspace turns reranking off while the library default has it on, then pinned results are not reranked.

<a id="d-req-rag-116"></a>
- **REQ-RAG-116** — A public `IWorkspaceManager` interface shall describe the workspace manager, `WorkspaceManager` shall implement it, and the library shall accept a stand-in through it, so a host can unit-test services built on workspaces *(Sevak feedback TR-RAG-013)*. *BRD:* BRD-176
  - *Acceptance:* When a developer passes a test double of `IWorkspaceManager` to a service, then the service runs without building a library client.

<a id="d-req-rag-117"></a>
- **REQ-RAG-117** — A connector run that is cancelled or fails part-way shall still hand back the sync state and the per-item reasons gathered so far, so the next run resumes instead of starting over *(Sevak feedback TR-RAG-022, the part BRD-127 left open)*. *BRD:* BRD-177
  - *Acceptance:* When a developer cancels a run after 3 of 10 items, then the sync state for those 3 items is returned.

<a id="d-req-rag-118"></a>
- **REQ-RAG-118** — Each item result shall say Failed or Skipped as a typed value, byte sizes in reasons shall be formatted the same on every machine, and the metadata builder connector ingestion uses shall be public *(Sevak feedback TR-RAG-023)*. *BRD:* BRD-178
  - *Acceptance:* When an item is skipped for size on a German-language machine, then its outcome reads Skipped and its size has no thousands separator.

<a id="d-req-rag-119"></a>
- **REQ-RAG-119** — Mail content nested past the parser's depth limit shall be reported in a new optional notes list on the parsed message, never dropped without notice; the list is an addition, so existing callers are unaffected *(Sevak feedback TR-RAG-035)*. *BRD:* BRD-180
  - *Acceptance:* When a developer parses a message nested past the depth limit, then the parsed message carries a note naming the skipped part.

<a id="d-req-rag-120"></a>
- **REQ-RAG-120** — Text ingestion shall take an optional caller key and replace the earlier document stored under that key; `IngestConnectorAsync` shall pass the connector's item id as the key *(Sevak feedback TR-RAG-026)*. *BRD:* BRD-179
  - *Acceptance:* When a developer ingests text twice under the same key, then the document list holds one document with the second text.

<a id="d-req-rag-121"></a>
- **REQ-RAG-121** — Registering a tool name a second time shall replace its definition as well as its handler, so the tool list sent to the model holds each name once *(Sevak feedback TR-RAG-039)*. *BRD:* BRD-181
  - *Acceptance:* When a developer registers the same tool name twice, then the tool list sent to the model contains that name once.

<a id="d-req-rag-122"></a>
- **REQ-RAG-122** — When an app gives no folder for a SQLite database, an existing `techierag.db` in the folder the app runs from shall be used, with a logged warning naming that folder. Otherwise the database shall be created in the per-user TechieRag folder the models use, under `data/<app name>/`. The location follows the models' override order (a folder set in code, then an environment variable, then the per-user default), and a public property returns it. Each app gets its own sub-folder, so two apps never share one database *(Sevak feedback TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them)*. *BRD:* BRD-182
  - *Acceptance:* When a new app builds the library naming no database folder, then the database is created under the per-user TechieRag data folder for that app.

<a id="d-req-rag-123"></a>
- **REQ-RAG-123** — `FlowNodeCatalog.CreateNode` shall take an optional step name, and its documentation shall say a host should set its own, localized name *(Sevak feedback TR-RAG-042)*. *BRD:* BRD-183
  - *Acceptance:* When a developer creates a node with a step name, then the node carries that name instead of the English one.

<a id="d-req-rag-124"></a>
- **REQ-RAG-124** — A flow definition shall say whether any of its steps calls a language model and list those steps *(Sevak feedback TR-RAG-043)*. *BRD:* BRD-184
  - *Acceptance:* When a developer asks a flow with one model step whether it uses a model, then it answers yes and lists that step.
