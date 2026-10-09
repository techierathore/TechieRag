# TechieRag — Business Requirements — Phase 3: Consumer feedback from Sevak

| | |
|---|---|
| App | TechieRag |
| Kind | library |
| Size | Small |
| Phase | 3 of 3 |
| Status | Approved |
| Date | 2026-10-06 |

## 1. Summary

Phase 3 takes the defects Sevak reported against the library that are still unfixed on 2026-10-06. Each one was checked against the source that day. They sit in their own phase for two reasons. Phase 2 is at 90 verified items and waiting for UAT, so adding them there would reopen it. They would also take it past the 100-item limit for one phase. The items make workspaces testable and faster, make connector runs survive a cancel with their sync state, let repeated syncs replace a document instead of duplicating it, report what the mail parser drops, stop duplicate tool definitions, and answer two questions a flow builder needs. They also give an app that names no database folder a stable, per-app location beside the shared model folder, without moving any existing app's data. Ids run on from phase 2: BRD-174 to BRD-188 (added later: BRD-185 on 2026-10-07, a pre-download fit check for local models; BRD-186 and BRD-187 on 2026-10-08, model listing and a per-call endpoint, from Lekhak; BRD-188 on 2026-10-09, a small-model chooser, from Chatur). Each item names the Sevak feedback entry it answers.

## 2. Screens and flow

Each row is a public surface this phase extends; the Route column names its entry point. No mockups exist for a library.

| Screen | Route | Role | Mockup | Fields |
|---|---|---|---|---|
| Workspace pinning and testing | `WorkspaceManager`, `ITechieRag.GetWorkspaceManager()` | Developer | — (library, no mockup) | pinned documents, rerank switch, interface |
| Connector run results | `ConnectorRunner`, `ConnectorItemFailure`, `MimeParser` | Developer | — (library, no mockup) | sync state on cancel, item outcome, parse notes |
| Document replace on re-ingest | `ITechieRag.IngestTextAsync`, `IngestConnectorAsync` | Developer | — (library, no mockup) | document key, replace |
| Tool registry | `ToolRegistry` | Agent builder | — (library, no mockup) | tool name, definition, handler |
| Storage defaults | `UseSqliteVec()`, `PersistenceConfig.ConnectionString`, the data folder | App developer | — (library, no mockup) | database path, data root, app name |
| Flow step names and model use | `FlowNodeCatalog`, `FlowDefinition` | Agent builder | — (library, no mockup) | step name, uses a model |
| Local model fit check | `LocalLlmProvider`, `LocalModel`, `AvailableMemory` | App developer | — (library, no mockup) | runtime available, memory needed, memory free, download progress |
| Model routing | `LlmProviderFactory.ListModelsAsync`, `LlmProviderFactory.CreateForModel` | App developer | — (library, no mockup) | connector, endpoint, model ids |

**Primary journey:**
1. A developer builds an app on the packages without naming a database folder; the data lands in the per-user TechieRag folder under the app's own name, and the log says where.
2. The app syncs a connector, cancels part-way, and resumes from the sync state it got back; changed items replace their earlier documents.
3. The developer unit-tests a service built on workspaces with a stand-in for the workspace manager.

## 3. Requirements

One item per thing the verifier will test, grouped by surface. Same rules as the phase-1 BRD.

### Workspace pinning and testing

Pinned documents cost one search, follow the workspace's own settings, and can be replaced in tests.

- **BRD-174** — Pinned-document retrieval shall run one filtered search over all of a workspace's pinned documents instead of one search per document *(Sevak feedback TR-RAG-010)* *Screen:* Workspace pinning and testing
  - *Acceptance:* When a developer asks a question in a workspace with five pinned documents, then one vector search covers all five.
- **BRD-175** — Pinned results shall follow the workspace's own rerank switch, and the API documentation shall state the rule *(Sevak feedback TR-RAG-011)* *Screen:* Workspace pinning and testing
  - *Acceptance:* When a workspace turns reranking off while the library default has it on, then pinned results are not reranked.
- **BRD-176** — A public `IWorkspaceManager` interface shall describe the workspace manager, `WorkspaceManager` shall implement it, and the library shall accept a stand-in through it, so a host can unit-test services built on workspaces *(Sevak feedback TR-RAG-013)* *Screen:* Workspace pinning and testing
  - *Acceptance:* When a developer passes a test double of `IWorkspaceManager` to a service, then the service runs without building a library client.

### Connector run results

A connector run keeps its place on a cancel, and its results and parse losses are reported in a form code can read.

- **BRD-177** — A connector run that is cancelled or fails part-way shall still hand back the sync state and the per-item reasons gathered so far, so the next run resumes instead of starting over *(Sevak feedback TR-RAG-022, the part BRD-127 left open)* *Screen:* Connector run results
  - *Acceptance:* When a developer cancels a run after 3 of 10 items, then the sync state for those 3 items is returned.
- **BRD-178** — Each item result shall say Failed or Skipped as a typed value, byte sizes in reasons shall be formatted the same on every machine, and the metadata builder connector ingestion uses shall be public *(Sevak feedback TR-RAG-023)* *Screen:* Connector run results
  - *Acceptance:* When an item is skipped for size on a German-language machine, then its outcome reads Skipped and its size has no thousands separator.
- **BRD-180** — Mail content nested past the parser's depth limit shall be reported in a new optional notes list on the parsed message, never dropped without notice; the list is an addition, so existing callers are unaffected *(Sevak feedback TR-RAG-035)* *Screen:* Connector run results
  - *Acceptance:* When a developer parses a message nested past the depth limit, then the parsed message carries a note naming the skipped part.

### Document replace on re-ingest

Re-syncing a source replaces documents instead of piling up copies.

- **BRD-179** — Text ingestion shall take an optional caller key and replace the earlier document stored under that key; `IngestConnectorAsync` shall pass the connector's item id as the key *(Sevak feedback TR-RAG-026)* *Screen:* Document replace on re-ingest
  - *Acceptance:* When a developer ingests text twice under the same key, then the document list holds one document with the second text.

### Tool registry

The model is told about each tool once.

- **BRD-181** — Registering a tool name a second time shall replace its definition as well as its handler, so the tool list sent to the model holds each name once *(Sevak feedback TR-RAG-039)* *Screen:* Tool registry
  - *Acceptance:* When a developer registers the same tool name twice, then the tool list sent to the model contains that name once.

### Storage defaults

An app that names no database folder gets a stable place of its own; an app that already has data keeps it.

- **BRD-182** — When an app gives no folder for a SQLite database, an existing `techierag.db` in the folder the app runs from shall be used, with a logged warning naming that folder. Otherwise the database shall be created in the per-user TechieRag folder the models use, under `data/<app name>/`. The location follows the models' override order (a folder set in code, then an environment variable, then the per-user default), and a public property returns it. Each app gets its own sub-folder, so two apps never share one database *(Sevak feedback TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them)* *Screen:* Storage defaults
  - *Acceptance:* When a new app builds the library naming no database folder, then the database is created under the per-user TechieRag data folder for that app.

### Flow step names and model use

A flow builder can name steps in its own language and ask whether a flow needs a model.

- **BRD-183** — `FlowNodeCatalog.CreateNode` shall take an optional step name, and its documentation shall say a host should set its own, localized name *(Sevak feedback TR-RAG-042)* *Screen:* Flow step names and model use
  - *Acceptance:* When a developer creates a node with a step name, then the node carries that name instead of the English one.
- **BRD-184** — A flow definition shall say whether any of its steps calls a language model and list those steps *(Sevak feedback TR-RAG-043)* *Screen:* Flow step names and model use
  - *Acceptance:* When a developer asks a flow with one model step whether it uses a model, then it answers yes and lists that step.

### Local model fit check

An app can tell whether the local model will run and fit before it offers the download.

- **BRD-185** — A host shall be able to check, before any download, whether a local model can run and fit on this device, and a load shall refuse a model that cannot fit before downloading it. Whether the platform has a local runtime, the free memory the model needs and the free memory the library measures shall be public, and a provider's download progress shall reach an option on that provider alone *(Sevak feedback TR-RAG-047, added 2026-10-07)* *Screen:* Local model fit check
  - *Acceptance:* When a developer checks a local model that cannot fit before its download, then the check says so without a request, and loading it refuses before the download.

### Model routing

A host can list a service's models, reach a service on another machine by its model name alone, and let a small model choose which model does a piece of work.

- **BRD-186** — The library shall list the models a connector or route serves, for LM Studio, Ollama and every OpenAI-compatible connector *(Lekhak feedback TR-RAG-004, added 2026-10-08)* *Screen:* Model routing
  - *Acceptance:* When a developer asks for the models of an LM Studio, Ollama or OpenAI-compatible connector on Model routing, then the model ids that service reports are returned.
- **BRD-187** — Creating a provider from a model name shall take an optional endpoint, so a local runtime on another machine needs no rebuilt route *(Lekhak feedback TR-RAG-005, added 2026-10-08)* *Screen:* Model routing
  - *Acceptance:* When a developer creates a provider for lmstudio/<model> with an endpoint on Model routing, then its requests go to that endpoint.
- **BRD-188** — The library shall choose a model for a piece of work: a small model reads the request and a list of candidate models (each with a tier, notes and cost) and returns one candidate's id and the reason, so a host can show and log the choice *(Chatur feedback TR-RAG-006, added 2026-10-09)* *Screen:* Model routing
  - *Acceptance:* When a developer asks the chooser to pick between candidate models for a request on Model routing, then one candidate's id and the small model's reason are returned.

## 5. Development status

Written by the status gate after every build, verify and handoff; not by hand.

**Snapshot as of 2026-10-09.** Live per-requirement status: `PROJECT-STATUS.md` and the Requirements Status table in `docs/TechieRag-P3-Checklist.md`.

| Screen | Requirements | Verified | Open | Status |
|---|---|---|---|---|
| RAG / AI requirements | 11 | 11 | 0 | Done |
| Local model fit check | 1 | 1 | 0 | Done |
| Model routing | 3 | 3 | 0 | Done |

## 6. Where the rest lives

| What | Where |
|---|---|
| Scope, users and roles, the context diagram | [phase 1 BRD](TechieRag-BRD.md) |
| Non-functional requirements for the whole library | [phase 1 BRD](TechieRag-BRD.md) |
| Constraints, assumptions and risks | [phase 1 BRD](TechieRag-BRD.md) |
| Every phase, its screens and its BRD range | [TechieRag-Phases.md](TechieRag-Phases.md) |
| This phase's work list | [TechieRag-P3-Checklist.md](TechieRag-P3-Checklist.md) |
| The defects these items answer | `Sevak/docs/Sevak-TechieRag-Feedback.md` (Sevak repository) |
