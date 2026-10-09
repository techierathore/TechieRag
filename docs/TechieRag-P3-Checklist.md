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
| REQ-RAG-114 | Pinned-document retrieval shall run one filtered search over all of a workspace's pinned documents instead of one search per document *(Sevak feedback TR-RAG-010)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-114", me` | [view](#d-req-rag-114) |
| REQ-RAG-115 | Pinned results shall follow the workspace's own rerank switch, and the API documentation shall state the rule *(Sevak feedback TR-RAG-011)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-115", me` | [view](#d-req-rag-115) |
| REQ-RAG-116 | A public `IWorkspaceManager` interface shall describe the workspace manager, `WorkspaceManager` shall implement it, and the library shall accept a stand-in through it, so a host can unit-test services built on workspaces *(Sevak feedback TR-RAG-013)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-116", me` | [view](#d-req-rag-116) |
| REQ-RAG-117 | A connector run that is cancelled or fails part-way shall still hand back the sync state and the per-item reasons gathered so far, so the next run resumes instead of starting over *(Sevak feedback TR-RAG-022, the part BRD-127 left open)* | Verified | 100% | 2026-10-09 verify: PASS — test `REQ-RAG-117 FailedRunCarriesPartialOutcome` | [view](#d-req-rag-117) |
| REQ-RAG-118 | Each item result shall say Failed or Skipped as a typed value, byte sizes in reasons shall be formatted the same on every machine, and the metadata builder connector ingestion uses shall be public *(Sevak feedback TR-RAG-023)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-118", me` | [view](#d-req-rag-118) |
| REQ-RAG-119 | Mail content nested past the parser's depth limit shall be reported in a new optional notes list on the parsed message, never dropped without notice; the list is an addition, so existing callers are unaffected *(Sevak feedback TR-RAG-035)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-119", me` | [view](#d-req-rag-119) |
| REQ-RAG-120 | Text ingestion shall take an optional caller key and replace the earlier document stored under that key; `IngestConnectorAsync` shall pass the connector's item id as the key *(Sevak feedback TR-RAG-026)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-120", me` | [view](#d-req-rag-120) |
| REQ-RAG-121 | Registering a tool name a second time shall replace its definition as well as its handler, so the tool list sent to the model holds each name once *(Sevak feedback TR-RAG-039)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-121", me` | [view](#d-req-rag-121) |
| REQ-RAG-122 | When an app gives no folder for a SQLite database, an existing `techierag.db` in the folder the app runs from shall be used, with a logged warning naming that folder. Otherwise the database shall be created in the per-user TechieRag folder the models use, under `data/<app name>/`. The location follows the models' override order (a folder set in code, then an environment variable, then the per-user default), and a public property returns it. Each app gets its own sub-folder, so two apps never share one database *(Sevak feedback TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them)* | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-122", me` | [view](#d-req-rag-122) |
| REQ-RAG-123 | `FlowNodeCatalog.CreateNode` shall take an optional step name, and its documentation shall say a host should set its own, localized name *(Sevak feedback TR-RAG-042)* | Verified | 100% | 2026-10-09 verify: PASS — test `REQ-RAG-123 CreateNodeCarriesHostStepName` | [view](#d-req-rag-123) |
| REQ-RAG-124 | A flow definition shall say whether any of its steps calls a language model and list those steps *(Sevak feedback TR-RAG-043)* | Verified | 100% | 2026-10-09 verify: PASS — test `REQ-RAG-124 FlowWithoutModelStepsAnswersNo` | [view](#d-req-rag-124) |
| REQ-RAG-125 | A host shall be able to check, before any download, whether a local model can run and fit on this device, and a load shall refuse a model that cannot fit before downloading it | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-125", me` | [d](#d-req-rag-125) |
| REQ-RAG-126 | The library shall list the models a connector or route serves, for LM Studio, Ollama and every OpenAI-compatible connector | Verified | 100% | 2026-10-09 verify: PASS — test `REQ-RAG-126 PublicListingReachesGivenEndpoint` | [d](#d-req-rag-126) |
| REQ-RAG-127 | Creating a provider from a model name shall take an optional endpoint, so a local runtime on another machine needs no rebuilt route | Verified | 100% | 2026-10-09 verify: PASS — test `REQ-RAG-127 EndpointOverrideIsValidatedAndOldCallStillWorks` | [d](#d-req-rag-127) |
| REQ-RAG-128 | The library shall choose a model for a piece of work: a small model reads the request and a list of candidate models (each with a tier, notes and cost) and returns one candidate's id and the reason, so a host can show and log the choice | Verified | 100% | 2026-10-09 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-128", me` | [d](#d-req-rag-128) |

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


## Local model fit check

- <a id="d-req-rag-125"></a> **REQ-RAG-125** A host shall be able to check, before any download, whether a local model can run and fit on this device, and a load shall refuse a model that cannot fit before downloading it *(Sevak feedback TR-RAG-047)*. *BRD:* BRD-185
  - *Acceptance:* When a developer checks a local model that cannot fit before its download, then the check says so without a request, and loading it refuses before the download.


## Model routing

- <a id="d-req-rag-126"></a> **REQ-RAG-126** The library shall list the models a connector or route serves, for LM Studio, Ollama and every OpenAI-compatible connector *(Lekhak feedback TR-RAG-004)*. *BRD:* BRD-186
  - *Acceptance:* When a developer asks for the models of an LM Studio, Ollama or OpenAI-compatible connector on Model routing, then the model ids that service reports are returned

- <a id="d-req-rag-127"></a> **REQ-RAG-127** Creating a provider from a model name shall take an optional endpoint, so a local runtime on another machine needs no rebuilt route *(Lekhak feedback TR-RAG-005)*. *BRD:* BRD-187
  - *Acceptance:* When a developer creates a provider for lmstudio/<model> with an endpoint on Model routing, then its requests go to that endpoint

- <a id="d-req-rag-128"></a> **REQ-RAG-128** The library shall choose a model for a piece of work: a small model reads the request and a list of candidate models (each with a tier, notes and cost) and returns one candidate's id and the reason, so a host can show and log the choice *(Chatur feedback TR-RAG-006)*. *BRD:* BRD-188
  - *Acceptance:* When a developer asks the chooser to pick between candidate models for a request on Model routing, then one candidate's id and the small model's reason are returned
