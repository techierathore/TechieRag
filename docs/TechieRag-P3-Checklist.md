# TechieRag — Checklist (phase 3)

| | |
|---|---|
| App | TechieRag |
| Size | Small |
| Phase | 3 of 3 |

## Goal

Fix the Sevak-reported defects still open on 2026-10-06: workspace pinning and testing, connector results on a cancel, replacing a re-ingested document, mail parse notes, duplicate tool definitions, flow step names and model use, and a stable per-app database folder when none is given. BRD-174 to BRD-184; each row names its Sevak feedback entry. Added later: BRD-185 to BRD-202 (fit check, routing, thinking text) and BRD-203 to BRD-209 (owner, 2026-11-05: local picture reading, REQ-RAG-145 to REQ-RAG-151) and BRD-210 to BRD-213 (owner, 2026-11-07: PostgreSQL end to end, the host-data skill, image generation, REQ-RAG-152 to REQ-RAG-155). This checklist is the whole work list of this phase.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-RAG-114 | Pinned-document retrieval shall run one filtered search over all of a workspace's pinned documents instead of one search per document *(Sevak feedback TR-RAG-010)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-114 PinnedDocumentsUseOneSearch` | [view](#d-req-rag-114) |
| REQ-RAG-115 | Pinned results shall follow the workspace's own rerank switch, and the API documentation shall state the rule *(Sevak feedback TR-RAG-011)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-115 PinnedResultsFollowWorkspaceRerankOn` | [view](#d-req-rag-115) |
| REQ-RAG-116 | A public `IWorkspaceManager` interface shall describe the workspace manager, `WorkspaceManager` shall implement it, and the library shall accept a stand-in through it, so a host can unit-test services built on workspaces *(Sevak feedback TR-RAG-013)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-116 LibraryAcceptsWorkspaceManagerStandIn` | [view](#d-req-rag-116) |
| REQ-RAG-117 | A connector run that is cancelled or fails part-way shall still hand back the sync state and the per-item reasons gathered so far, so the next run resumes instead of starting over *(Sevak feedback TR-RAG-022, the part BRD-127 left open)* | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-117", me` | [view](#d-req-rag-117) |
| REQ-RAG-118 | Each item result shall say Failed or Skipped as a typed value, byte sizes in reasons shall be formatted the same on every machine, and the metadata builder connector ingestion uses shall be public *(Sevak feedback TR-RAG-023)* | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-118", me` | [view](#d-req-rag-118) |
| REQ-RAG-119 | Mail content nested past the parser's depth limit shall be reported in a new optional notes list on the parsed message, never dropped without notice; the list is an addition, so existing callers are unaffected *(Sevak feedback TR-RAG-035)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-119 FullyReadMessageHasNoNotes` | [view](#d-req-rag-119) |
| REQ-RAG-120 | Text ingestion shall take an optional caller key and replace the earlier document stored under that key; `IngestConnectorAsync` shall pass the connector's item id as the key *(Sevak feedback TR-RAG-026)* | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-120", me` | [view](#d-req-rag-120) |
| REQ-RAG-121 | Registering a tool name a second time shall replace its definition as well as its handler, so the tool list sent to the model holds each name once *(Sevak feedback TR-RAG-039)* | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-121", me` | [view](#d-req-rag-121) |
| REQ-RAG-122 | When an app gives no folder for a SQLite database, an existing `techierag.db` in the folder the app runs from shall be used, with a logged warning naming that folder. Otherwise the database shall be created in the per-user TechieRag folder the models use, under `data/<app name>/`. The location follows the models' override order (a folder set in code, then an environment variable, then the per-user default), and a public property returns it. Each app gets its own sub-folder, so two apps never share one database *(Sevak feedback TR-RAG-040; owner decision 2026-10-06: warn and keep existing files, never move them)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-122 PublicOverloadsNeverGiveOneCallTwoMeanings` | [view](#d-req-rag-122) |
| REQ-RAG-123 | `FlowNodeCatalog.CreateNode` shall take an optional step name, and its documentation shall say a host should set its own, localized name *(Sevak feedback TR-RAG-042)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-123 CreateNodeCarriesHostStepName` | [view](#d-req-rag-123) |
| REQ-RAG-124 | A flow definition shall say whether any of its steps calls a language model and list those steps *(Sevak feedback TR-RAG-043)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-124 FlowWithoutModelStepsAnswersNo` | [view](#d-req-rag-124) |
| REQ-RAG-125 | A host shall be able to check, before any download, whether a local model can run and fit on this device, and a load shall refuse a model that cannot fit before downloading it | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-125 PublicOverloadsNeverGiveOneCallTwoMeanings` | [d](#d-req-rag-125) |
| REQ-RAG-126 | The library shall list the models a connector or route serves, for LM Studio, Ollama and every OpenAI-compatible connector | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-126", me` | [d](#d-req-rag-126) |
| REQ-RAG-127 | Creating a provider from a model name shall take an optional endpoint, so a local runtime on another machine needs no rebuilt route | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-127", me` | [d](#d-req-rag-127) |
| REQ-RAG-128 | The library shall choose a model for a piece of work: a small model reads the request and a list of candidate models (each with a tier, notes and cost) and returns one candidate's id and the reason, so a host can show and log the choice | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-128", me` | [d](#d-req-rag-128) |
| REQ-RAG-129 | Disposing a local model provider shall cancel or wait for the calls already inside it, free the native model only once none is left, and make every call that resumes afterwards throw ObjectDisposedException before it touches the model | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-129 StreamResumedAfterDisposeThrowsObjectDisposed` | [d](#d-req-rag-129) |
| REQ-RAG-130 | The library shall take two generic routes, openai-compatible/<model> and anthropic-compatible/<model>, that reach any service at an endpoint the app gives (its Base URL or an @https:// suffix on the route), refuse with a clear message when no endpoint is given, and list models where the service offers /models | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-130", me` | [d](#d-req-rag-130) |
| REQ-RAG-131 | The library shall have a venice connector, so venice/<model> reaches https://api.venice.ai/api/v1 with Venice's own system prompt switched off | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-131", me` | [d](#d-req-rag-131) |
| REQ-RAG-132 | The connector catalog shall describe itself for app screens through one public method: per connector its display name, whether it needs a key, whether it needs an endpoint, local or hosted, default endpoint and one example route | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-132", me` | [d](#d-req-rag-132) |
| REQ-RAG-133 | The Anthropic provider shall keep the path of its endpoint, so an Anthropic-compatible service under a path is reached | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-133", me` | [d](#d-req-rag-133) |
| REQ-RAG-134 | A failed LLM call shall carry the provider's error text: status code, trimmed response body and the provider's own error message, in a typed exception that is still an HttpRequestException (Lekhak feedback TR-RAG-007) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-134", me` | [d](#d-req-rag-134) |
| REQ-RAG-137 | Streamed thinking text shall reach the host as its own event (Chatur feedback TR-RAG-008) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-137", me` | [d](#d-req-rag-137) |
| REQ-RAG-138 | A leading `<think>` block in the answer shall be split into the thinking stream (Chatur feedback TR-RAG-009) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-138", me` | [d](#d-req-rag-138) |
| REQ-RAG-139 | The Anthropic provider shall send extended-thinking blocks as thinking text, streamed and whole, and send them back on tool-using turns (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-139", me` | [d](#d-req-rag-139) |
| REQ-RAG-140 | The Gemini provider shall send thought parts as thinking text, streamed and whole, and report the thought token count (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-140", me` | [d](#d-req-rag-140) |
| REQ-RAG-141 | The Ollama provider shall send its thinking field, and a leading `<think>` block, as thinking text, streamed and whole (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-141", me` | [d](#d-req-rag-141) |
| REQ-RAG-142 | The local model provider shall split a leading `<think>` block out of the answer, streamed and whole (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-142 LeadingThinkBlockStreamsAsThinking` | [d](#d-req-rag-142) |
| REQ-RAG-143 | The ChatGPT subscription provider shall send its reasoning summary as thinking text, streamed and whole, with the reasoning token count (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-143", me` | [d](#d-req-rag-143) |
| REQ-RAG-144 | The AI reference shall name the providers that send thinking text and the ones that cannot, with the rules every provider keeps (owner request, 2026-10-29) | Verified | 100% | 2026-11-05 verify: PASS — test `ShippedReferenceNamesPhaseThreeMember(row: "REQ-RAG-144", me` | [d](#d-req-rag-144) |
| REQ-RAG-145 | TechieRag.Local shall download, check and load a model that reads pictures from Hugging Face the same way it loads text models today (terms, fit check, fingerprint check, then load), with ONNX Runtime GenAI's multimodal processor. The catalogue shall name one that runs on a laptop CPU: Phi-3.5-vision-instruct, `microsoft/Phi-3.5-vision-instruct-onnx`, folder `cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4` (MIT, about 3.2 GB), pinned to one commit *(owner, added 2026-11-05)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-145 VisionModelIsPinnedAndHashed` | [view](#d-req-rag-145) |
| REQ-RAG-146 | On a machine with a GPU the catalogue shall name a GPU model that reads pictures: Phi-4-multimodal-instruct, `microsoft/Phi-4-multimodal-instruct-onnx`, folder `gpu/gpu-int4-rtn-block-32` (MIT, about 5.1 GB), pinned to one commit, loaded through an ONNX Runtime GenAI GPU runtime. Asking for it where no GPU runtime is present shall refuse before the download with a message saying so *(owner, added 2026-11-05; which GPU runtime is open, see the Decision Request)* | Blocked | 50% | [REQ-RAG-146] 2026-11-05: the refusal half passed verify (`REQ-RAG-146 GpuModelRefusedBeforeDownload`); the GPU load half is not built. The DirectML engine package stops at 0.14.1, below this library's GenAI 0.16.0 (ORTGENAI-003); WinML or CUDA needs the owner's choice. This laptop has only an Intel Iris Xe | [view](#d-req-rag-146) |
| REQ-RAG-147 | The local provider shall implement `IMultimodalLlmProvider`: a chat turn with one or more `ChatImage` parts shall reach a model that reads pictures, in order with the text, and the answer comes back as text like any other chat *(owner, added 2026-11-05)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-147 OversizedPicturePromptIsTooLong` | [view](#d-req-rag-147) |
| REQ-RAG-148 | The model catalogue shall say per model whether it reads pictures, for built-in models and for a model named by its Hugging Face repository (read from the model's own configuration), and `SupportsInput(Image)` shall give the same answer for the loaded model *(owner, added 2026-11-05)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-148 CatalogueSaysWhichModelsReadPictures` | [view](#d-req-rag-148) |
| REQ-RAG-149 | The DevGuide shall have one sample on the local provider: a picture in, text out, with the same `ChatImage` and `ChatContentPart` code a hosted provider takes *(owner, added 2026-11-05)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-149 DevGuideSampleIsTheTestedCode` | [view](#d-req-rag-149) |
| REQ-RAG-150 | A test with one fixed picture of a calendar page, kept in the test fixtures, shall send it to the local vision model and check that the text contains each meeting title and time on that page *(owner, added 2026-11-05)* | Implemented | 75% | 2026-11-05 verify: not measured — every test named REQ-RAG-150 was skipped; acceptance not measured (1 test(s) skipped, none ran) | [view](#d-req-rag-150) |
| REQ-RAG-151 | A local model that does not read pictures, given a chat turn with an image, shall throw a clear error naming the model before any inference; an image is never dropped without notice *(owner, added 2026-11-05; today the template formatter keeps only the text parts)* | Verified | 100% | 2026-11-05 verify: PASS — test `REQ-RAG-151 FolderTextModelRefusesPictureWhenStreaming` | [view](#d-req-rag-151) |
| REQ-RAG-152 | The PostgreSQL path shall be proven end to end on a real PostgreSQL 18 with pgvector that the test harness starts in Docker: `UsePgVector` for the vectors and the PostgreSQL conversation and workspace stores in the same database, reached through one `UsePostgres(connectionString)` builder call, with `LivePgVectorStoreTests` (BRD-157) and live store tests run in CI, so a host can offer its users a server database of their own *(Sevak feedback TR-RAG-055; Sevak phase 4, BRD-182 to BRD-185; owner, 2026-11-07)* The live tests read their connection string from a settings file beside the tests (a git-ignored `livetests.settings.json` with a committed example), never from an environment variable (owner's rule, 2026-11-07; today `LivePostgresFactAttribute` reads `TechieRagTestPostgres`). | Not Started | 0% | — | [view](#d-req-rag-152) |
| REQ-RAG-153 | The library shall offer a host-data skill: a host registers its own entities (name, fields, and handlers to list, read, create, update and delete) and the agent gets one tool set for them, so a host's assistant can read and change the host's data in plain words *(Sevak feedback TR-RAG-056; Sevak BRD-203; owner, 2026-11-07)* | Not Started | 0% | — | [view](#d-req-rag-153) |
| REQ-RAG-154 | Every change the host-data skill makes shall come back to the host as a typed change record (entity, id, before, after, the turn that caused it), and the skill shall reverse a record on request through the host's own handlers, so a host can show a card with Undo *(Sevak feedback TR-RAG-057; Sevak BRD-204; owner, 2026-11-07)* | Not Started | 0% | — | [view](#d-req-rag-154) |
| REQ-RAG-155 | The library shall generate images: an `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, for OpenAI, Gemini and Venice, routed like the chat providers, with no key ever persisted; this re-scopes BRD-161 for images only *(Sevak feedback TR-RAG-058; Sevak BRD-127 and BRD-281; owner, 2026-11-07)* | Not Started | 0% | — | [view](#d-req-rag-155) |

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

<a id="d-req-rag-152"></a>
- **REQ-RAG-152** — The PostgreSQL path shall be proven end to end on a real PostgreSQL 18 with pgvector that the test harness starts in Docker: `UsePgVector` for the vectors and the PostgreSQL conversation and workspace stores in the same database, reached through one `UsePostgres(connectionString)` builder call, with `LivePgVectorStoreTests` (BRD-157) and live store tests run in CI, so a host can offer its users a server database of their own *(Sevak feedback TR-RAG-055; Sevak phase 4, BRD-182 to BRD-185; owner, 2026-11-07)* The live tests read their connection string from a settings file beside the tests (a git-ignored `livetests.settings.json` with a committed example), never from an environment variable (owner's rule, 2026-11-07; today `LivePostgresFactAttribute` reads `TechieRagTestPostgres`).. *BRD:* BRD-210
  - *Acceptance:* When a developer runs the live PostgreSQL tests on PostgreSQL end to end against the harness-started server, then vectors, conversations and workspaces round-trip in one database.

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

- <a id="d-req-rag-130"></a> **REQ-RAG-130** The library shall take two generic routes, openai-compatible/<model> and anthropic-compatible/<model>, that reach any service at an endpoint the app gives (its Base URL or an @https:// suffix on the route), refuse with a clear message when no endpoint is given, and list models where the service offers /models *(owner report from Lekhak's provider page)*. *BRD:* BRD-190
  - *Acceptance:* When a developer creates a provider for openai-compatible/<model> or anthropic-compatible/<model> with an endpoint and key on Model routing, then requests reach that endpoint; missing either, it is refused

- <a id="d-req-rag-131"></a> **REQ-RAG-131** The library shall have a venice connector, so venice/<model> reaches https://api.venice.ai/api/v1 with Venice's own system prompt switched off *(owner report from Lekhak's provider page)*. *BRD:* BRD-191
  - *Acceptance:* When a developer creates a provider for venice/zai-org-glm-5-1 on Model routing, then requests reach Venice with Venice's own system prompt switched off

- <a id="d-req-rag-132"></a> **REQ-RAG-132** The connector catalog shall describe itself for app screens through one public method: per connector its display name, whether it needs a key, whether it needs an endpoint, local or hosted, default endpoint and one example route *(owner report from Lekhak's provider page)*. *BRD:* BRD-192
  - *Acceptance:* When an app calls LlmConnectorCatalog.Describe() on Model routing, then every connector, venice and both generic routes included, has an example route that resolves to it

- <a id="d-req-rag-133"></a> **REQ-RAG-133** The Anthropic provider shall keep the path of its endpoint, so an Anthropic-compatible service under a path is reached *(owner report from Lekhak's provider page)*. *BRD:* BRD-193
  - *Acceptance:* When a developer points the Anthropic provider at https://api.z.ai/api/anthropic on Model routing, then messages go to https://api.z.ai/api/anthropic/v1/messages

- <a id="d-req-rag-134"></a> **REQ-RAG-134** A failed LLM call shall carry the provider's error text: status code, trimmed response body and the provider's own error message, in a typed exception that is still an HttpRequestException *(Lekhak feedback TR-RAG-007)*. *BRD:* BRD-194
  - *Acceptance:* When a service on Model routing answers a chat with 404 and an error message, then the thrown LlmHttpException carries that message, the body and the status

## Local model lifetime

- <a id="d-req-rag-129"></a> **REQ-RAG-129** Disposing a local model provider shall cancel or wait for the calls already inside it, free the native model only once none is left, and make every call that resumes afterwards throw ObjectDisposedException before it touches the model *(Sevak feedback TR-RAG-053)*. *BRD:* BRD-189
  - *Acceptance:* When a developer disposes a local provider on Local model lifetime while three calls are inside, then each ends with ObjectDisposedException and the model is freed once



## LLM streaming

- <a id="d-req-rag-137"></a> **REQ-RAG-137** Streamed thinking text shall reach the host as its own event (Chatur feedback TR-RAG-008). *BRD:* BRD-195
  - *Acceptance:* When a reasoning model streams reasoning_content on Model routing, then each fragment arrives as a Thinking event and the TextDelta events hold only the answer

- <a id="d-req-rag-138"></a> **REQ-RAG-138** A leading `<think>` block in the answer shall be split into the thinking stream (Chatur feedback TR-RAG-009). *BRD:* BRD-196
  - *Acceptance:* When a model on Model routing opens its answer with a `<think>` block, then the block arrives as Thinking events and the answer text holds only the answer

- <a id="d-req-rag-139"></a> **REQ-RAG-139** The Anthropic provider shall send extended-thinking blocks as thinking text, streamed and whole, and send them back on tool-using turns (owner request, 2026-10-29). *BRD:* BRD-197
  - *Acceptance:* When a recorded Anthropic reply on Model routing carries a thinking block, then Thinking events and LlmResponse.Thinking hold it and the answer text holds only the answer

- <a id="d-req-rag-140"></a> **REQ-RAG-140** The Gemini provider shall send thought parts as thinking text, streamed and whole, and report the thought token count (owner request, 2026-10-29). *BRD:* BRD-198
  - *Acceptance:* When a recorded Gemini reply on Model routing carries a thought part, then Thinking holds it, the answer text is clean, and the thought token count is reported

- <a id="d-req-rag-141"></a> **REQ-RAG-141** The Ollama provider shall send its thinking field, and a leading `<think>` block, as thinking text, streamed and whole (owner request, 2026-10-29). *BRD:* BRD-199
  - *Acceptance:* When a recorded Ollama reply on Model routing carries a thinking field or opens with a `<think>` block, then Thinking holds it and the answer text is clean

- <a id="d-req-rag-142"></a> **REQ-RAG-142** The local model provider shall split a leading `<think>` block out of the answer, streamed and whole (owner request, 2026-10-29). *BRD:* BRD-200
  - *Acceptance:* When a local model on Model routing opens its answer with a `<think>` block, then Thinking holds the block and the answer text holds only the answer

- <a id="d-req-rag-143"></a> **REQ-RAG-143** The ChatGPT subscription provider shall send its reasoning summary as thinking text, streamed and whole, with the reasoning token count (owner request, 2026-10-29). *BRD:* BRD-201
  - *Acceptance:* When a recorded subscription stream on Model routing carries reasoning summary text, then Thinking events hold it, the answer is clean, and the reasoning count is reported

- <a id="d-req-rag-144"></a> **REQ-RAG-144** The AI reference shall name the providers that send thinking text and the ones that cannot, with the rules every provider keeps (owner request, 2026-10-29). *BRD:* BRD-202
  - *Acceptance:* When a developer reads the thinking section of the AI reference on Model routing, then every built-in provider is listed as sending thinking or not, with the reason

## Page: Local picture reading (`LocalModel`, `LocalLlmProvider` as `IMultimodalLlmProvider`, `ChatImage`, `ChatContentPart`)

<a id="d-req-rag-145"></a>
- **REQ-RAG-145** — TechieRag.Local shall download, check and load a model that reads pictures from Hugging Face the same way it loads text models today (terms, fit check, fingerprint check, then load), with ONNX Runtime GenAI's multimodal processor. The catalogue shall name one that runs on a laptop CPU: Phi-3.5-vision-instruct, `microsoft/Phi-3.5-vision-instruct-onnx`, folder `cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4` (MIT, about 3.2 GB), pinned to one commit *(owner, added 2026-11-05)*. *BRD:* BRD-203
  - *Acceptance:* When a developer loads the Phi-3.5 vision model on Local picture reading on a CPU-only laptop, then its files download, pass their fingerprints and load.

<a id="d-req-rag-146"></a>
- **REQ-RAG-146** — On a machine with a GPU the catalogue shall name a GPU model that reads pictures: Phi-4-multimodal-instruct, `microsoft/Phi-4-multimodal-instruct-onnx`, folder `gpu/gpu-int4-rtn-block-32` (MIT, about 5.1 GB), pinned to one commit, loaded through an ONNX Runtime GenAI GPU runtime. Asking for it where no GPU runtime is present shall refuse before the download with a message saying so *(owner, added 2026-11-05; which GPU runtime is open, see the Decision Request)*. *BRD:* BRD-204
  - *Acceptance:* When a developer loads the Phi-4 multimodal model on Local picture reading, then it loads with the GPU runtime and refuses before any download without it.

<a id="d-req-rag-147"></a>
- **REQ-RAG-147** — The local provider shall implement `IMultimodalLlmProvider`: a chat turn with one or more `ChatImage` parts shall reach a model that reads pictures, in order with the text, and the answer comes back as text like any other chat *(owner, added 2026-11-05)*. *BRD:* BRD-205
  - *Acceptance:* When a developer on Local picture reading sends a turn with two images to a vision model, then both reach the model and text comes back.

<a id="d-req-rag-148"></a>
- **REQ-RAG-148** — The model catalogue shall say per model whether it reads pictures, for built-in models and for a model named by its Hugging Face repository (read from the model's own configuration), and `SupportsInput(Image)` shall give the same answer for the loaded model *(owner, added 2026-11-05)*. *BRD:* BRD-206
  - *Acceptance:* When a developer on Local picture reading lists the catalogue, then the two vision models say they read pictures and Qwen2.5 0.5B and Phi-3 mini say they do not.

<a id="d-req-rag-149"></a>
- **REQ-RAG-149** — The DevGuide shall have one sample on the local provider: a picture in, text out, with the same `ChatImage` and `ChatContentPart` code a hosted provider takes *(owner, added 2026-11-05)*. *BRD:* BRD-207
  - *Acceptance:* When a developer on Local picture reading runs the DevGuide sample on a picture, then the local model's text description comes back.

<a id="d-req-rag-150"></a>
- **REQ-RAG-150** — A test with one fixed picture of a calendar page, kept in the test fixtures, shall send it to the local vision model and check that the text contains each meeting title and time on that page *(owner, added 2026-11-05)*. *BRD:* BRD-208
  - *Acceptance:* When the calendar-page test runs on Local picture reading, then every meeting title and its time on the fixed picture appears in the model's text.

<a id="d-req-rag-151"></a>
- **REQ-RAG-151** — A local model that does not read pictures, given a chat turn with an image, shall throw a clear error naming the model before any inference; an image is never dropped without notice *(owner, added 2026-11-05; today the template formatter keeps only the text parts)*. *BRD:* BRD-209
  - *Acceptance:* When a developer on Local picture reading sends an image to Phi-3 mini, then the call throws an error naming Phi-3 mini before inference.

## Page: Host data skill (`HostDataSkill`, `IHostEntity`, `HostDataChange`)

<a id="d-req-rag-153"></a>
- **REQ-RAG-153** — The library shall offer a host-data skill: a host registers its own entities (name, fields, and handlers to list, read, create, update and delete) and the agent gets one tool set for them, so a host's assistant can read and change the host's data in plain words *(Sevak feedback TR-RAG-056; Sevak BRD-203; owner, 2026-11-07)*. *BRD:* BRD-211
  - *Acceptance:* When a developer registers a "task" entity on Host data skill and a user asks for a task, then the create handler runs with the extracted fields.

<a id="d-req-rag-154"></a>
- **REQ-RAG-154** — Every change the host-data skill makes shall come back to the host as a typed change record (entity, id, before, after, the turn that caused it), and the skill shall reverse a record on request through the host's own handlers, so a host can show a card with Undo *(Sevak feedback TR-RAG-057; Sevak BRD-204; owner, 2026-11-07)*. *BRD:* BRD-212
  - *Acceptance:* When a developer calls Undo with a change record on Host data skill, then the entity is restored to its "before" state through the host's handler.

## Page: Image generation (`IImageGenerationProvider`, `GenerateImageAsync`)

<a id="d-req-rag-155"></a>
- **REQ-RAG-155** — The library shall generate images: an `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, for OpenAI, Gemini and Venice, routed like the chat providers, with no key ever persisted; this re-scopes BRD-161 for images only *(Sevak feedback TR-RAG-058; Sevak BRD-127 and BRD-281; owner, 2026-11-07)*. *BRD:* BRD-213
  - *Acceptance:* When a developer calls GenerateImageAsync on Image generation with a prompt, a size and a keyed provider, then PNG bytes of that size and the usage come back.

