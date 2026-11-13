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

Phase 3 takes the defects Sevak reported against the library that are still unfixed on 2026-10-06. Each one was checked against the source that day. They sit in their own phase because phase 2, at 90 verified items and waiting for UAT, would be reopened and taken past the 100-item limit. The items make workspaces testable and faster, make connector runs survive a cancel, let re-syncs replace a document, report what the mail parser drops, stop duplicate tool definitions, answer two flow-builder questions, and give an app that names no database folder a stable per-app location without moving existing data. Ids run on from phase 2: BRD-174 to BRD-193. Added later: BRD-185 (fit check), BRD-186 and 187 (Lekhak), BRD-188 (Chatur), BRD-189 (Sevak), BRD-190 to 193 (routes, from Lekhak's page), BRD-194 (Lekhak, provider error text), BRD-195 and 196 (Chatur, thinking text), BRD-197 to 202 (owner, thinking text for every provider), BRD-203 to 209 (owner, local picture reading), BRD-210 to BRD-213 (owner, 2026-11-07: what Sevak's assistant phases need: PostgreSQL end to end, the host-data skill, image generation), BRD-214 and 215 (owner, 2026-11-08: openai-compatible and Azure OpenAI image routes). Each item names the Sevak feedback entry or requirement it answers.

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
| Model routing | `LlmProviderFactory.ListModelsAsync`, `LlmProviderFactory.CreateForModel`, `ModelChooser` | App developer | — (library, no mockup) | connector, endpoint, model ids, candidates, chosen model, reason |
| Local model lifetime | `LocalLlmProvider.Dispose`, `LocalLlmProvider.DisposeAsync` | App developer | — (library, no mockup) | calls inside, memory freed |
| Local picture reading | `LocalModel`, `LocalLlmProvider` as `IMultimodalLlmProvider`, `ChatImage`, `ChatContentPart` | App developer | — (library, no mockup) | model, reads pictures, images in the turn, text out, error naming the model |
| PostgreSQL end to end | `UsePostgres`, `UsePgVector`, the live PostgreSQL tests | App developer | — (library, no mockup) | connection string, vectors, conversations, workspaces in one database |
| Host data skill | `HostDataSkill`, `IHostEntity`, `HostDataChange` | Agent builder | — (library, no mockup) | entity, fields, handlers, change record, undo |
| Image generation | `IImageGenerationProvider`, `GenerateImageAsync` | App developer | — (library, no mockup) | prompt, size, provider, bytes, media type, usage |

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

A host can list a service's models, reach a service on another machine by its model name alone, reach any OpenAI- or Anthropic-compatible service and Venice by route, show the routes it can offer, and let a small model choose which model does a piece of work.

- **BRD-186** — The library shall list the models a connector or route serves, for LM Studio, Ollama and every OpenAI-compatible connector *(Lekhak feedback TR-RAG-004, added 2026-10-08)* *Screen:* Model routing
  - *Acceptance:* When a developer asks for the models of an LM Studio, Ollama or OpenAI-compatible connector on Model routing, then the model ids that service reports are returned.
- **BRD-187** — Creating a provider from a model name shall take an optional endpoint, so a local runtime on another machine needs no rebuilt route *(Lekhak feedback TR-RAG-005, added 2026-10-08)* *Screen:* Model routing
  - *Acceptance:* When a developer creates a provider for lmstudio/<model> with an endpoint on Model routing, then its requests go to that endpoint.
- **BRD-188** — The library shall choose a model for a piece of work: a small model reads the request and a list of candidate models (each with a tier, notes and cost) and returns one candidate's id and the reason, so a host can show and log the choice *(Chatur feedback TR-RAG-006, added 2026-10-09)* *Screen:* Model routing
  - *Acceptance:* When a developer asks the chooser to pick between candidate models for a request on Model routing, then one candidate's id and the small model's reason are returned.
- **BRD-190** — The library shall take two generic routes, openai-compatible/<model> and anthropic-compatible/<model>, that reach any service at an endpoint the app gives (its Base URL or an @https:// suffix on the route), refuse with a clear message when no endpoint is given, and list models where the service offers /models *(owner report from Lekhak's provider page, added 2026-10-11)* *Screen:* Model routing
  - *Acceptance:* When a developer creates a provider for openai-compatible/<model> or anthropic-compatible/<model> with an endpoint and key on Model routing, then requests reach that endpoint; missing either, it is refused.
- **BRD-191** — The library shall have a venice connector, so venice/<model> reaches https://api.venice.ai/api/v1 with Venice's own system prompt switched off *(owner report from Lekhak's provider page, added 2026-10-11)* *Screen:* Model routing
  - *Acceptance:* When a developer creates a provider for venice/zai-org-glm-5-1 on Model routing, then requests reach Venice with Venice's own system prompt switched off.
- **BRD-192** — The connector catalog shall describe itself for app screens through one public method: per connector its display name, whether it needs a key, whether it needs an endpoint, local or hosted, default endpoint and one example route *(owner report from Lekhak's provider page, added 2026-10-11)* *Screen:* Model routing
  - *Acceptance:* When an app calls LlmConnectorCatalog.Describe() on Model routing, then every connector, venice and both generic routes included, has an example route that resolves to it.
- **BRD-193** — The Anthropic provider shall keep the path of its endpoint, so an Anthropic-compatible service under a path is reached *(owner report from Lekhak's provider page, added 2026-10-11)* *Screen:* Model routing
  - *Acceptance:* When a developer points the Anthropic provider at https://api.z.ai/api/anthropic on Model routing, then messages go to https://api.z.ai/api/anthropic/v1/messages.
- **BRD-194** — A failed LLM call shall carry the provider's error text: the status code, the response body (cut to a safe length) and the provider's own error message, in a typed exception that is still an HttpRequestException, so an app can say why a call failed *(Lekhak feedback TR-RAG-007, added 2026-10-15)* *Screen:* Model routing
  - *Acceptance:* When a service on Model routing answers a chat with 404 and an error message, then the thrown LlmHttpException carries that message, the body and the status.
- **BRD-195** — A reasoning model's thinking text shall reach the app on its own while streaming, apart from the answer, as a thinking event, and a whole reply shall return it in its own field, with the reasoning token count still reported *(Chatur feedback TR-RAG-008, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a reasoning model streams reasoning_content on Model routing, then each fragment arrives as a Thinking event and the TextDelta events hold only the answer.
- **BRD-196** — A thinking block that opens a model's answer shall be taken out of the answer and handed over as thinking text, even when its tags arrive cut across pieces of the stream; the same tag later in the answer is kept *(Chatur feedback TR-RAG-009, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a model on Model routing opens its answer with a `<think>` block, then the block arrives as Thinking events and the answer text holds only the answer.
- **BRD-197** — The Anthropic provider shall send a model's extended-thinking content blocks as thinking text, streamed and whole, ask for thinking when the app sets a reasoning budget or effort, and send the thinking blocks back unchanged on a tool-using turn, as the service requires *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a recorded Anthropic reply on Model routing carries a thinking block, then Thinking events and LlmResponse.Thinking hold it and the answer text holds only the answer.
- **BRD-198** — The Gemini provider shall send a model's thought parts as thinking text, streamed and whole, ask the service for them when the app sets a reasoning budget or effort, and report the service's thought token count as the reasoning token count *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a recorded Gemini reply on Model routing carries a thought part, then Thinking holds it, the answer text is clean, and the thought token count is reported.
- **BRD-199** — The Ollama provider shall send the thinking field of its chat reply as thinking text, streamed and whole, map the app's reasoning switch to the service's think setting, and also split a leading think block out of the answer text *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a recorded Ollama reply on Model routing carries a thinking field or opens with a `<think>` block, then Thinking holds it and the answer text is clean.
- **BRD-200** — The local model provider shall split a think block that opens the model's answer out of the answer, streamed and whole, so a local reasoning model's thinking reaches the app as thinking text *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a local model on Model routing opens its answer with a `<think>` block, then Thinking holds the block and the answer text holds only the answer.
- **BRD-201** — The ChatGPT subscription provider shall send the reasoning summary its service returns as thinking text, streamed and whole, and report the service's reasoning token count; the raw reasoning stays hidden because the service never sends it *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a recorded subscription stream on Model routing carries reasoning summary text, then Thinking events hold it, the answer is clean, and the reasoning count is reported.
- **BRD-202** — The AI reference shall name the providers that send thinking text and the ones that cannot, and state the rules every provider keeps: thinking never inside the answer, no Thinking event where there is no thinking, and the event order *(owner request, thinking for every provider, added 2026-10-29)* *Screen:* Model routing
  - *Acceptance:* When a developer reads the thinking section of the AI reference on Model routing, then every built-in provider is listed as sending thinking or not, with the reason.

### Local picture reading

An app reads a picture with a model on the user's own machine, through the same image types a hosted provider takes, so its code is the same for local and hosted. The model only turns the picture into text; checking dates, overlaps and duplicates stays in the app *(owner, 2026-11-05: Sevak reads a screenshot or phone photo of an office calendar into draft tasks, and nothing leaves the laptop; extends BRD-149 to the local provider)*.

- **BRD-203** — TechieRag.Local shall download, check and load a model that reads pictures from Hugging Face the same way it loads text models today (terms, fit check, fingerprint check, then load), with ONNX Runtime GenAI's multimodal processor. The catalogue shall name one that runs on a laptop CPU: Phi-3.5-vision-instruct, `microsoft/Phi-3.5-vision-instruct-onnx`, folder `cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4` (MIT, about 3.2 GB), pinned to one commit *(owner, added 2026-11-05)* *Screen:* Local picture reading
  - *Acceptance:* When a developer loads the Phi-3.5 vision model on Local picture reading on a CPU-only laptop, then its files download, pass their fingerprints and load.
- **BRD-204** — On a machine with a GPU the catalogue shall name a GPU model that reads pictures: Phi-4-multimodal-instruct, `microsoft/Phi-4-multimodal-instruct-onnx`, folder `gpu/gpu-int4-rtn-block-32` (MIT, about 5.1 GB), pinned to one commit, loaded through an ONNX Runtime GenAI GPU runtime. Asking for it where no GPU runtime is present shall refuse before the download with a message saying so *(owner, added 2026-11-05; which GPU runtime is open, see the Decision Request)* *Screen:* Local picture reading
  - *Acceptance:* When a developer loads the Phi-4 multimodal model on Local picture reading, then it loads with the GPU runtime and refuses before any download without it.
- **BRD-205** — The local provider shall implement `IMultimodalLlmProvider`: a chat turn with one or more `ChatImage` parts shall reach a model that reads pictures, in order with the text, and the answer comes back as text like any other chat *(owner, added 2026-11-05)* *Screen:* Local picture reading
  - *Acceptance:* When a developer on Local picture reading sends a turn with two images to a vision model, then both reach the model and text comes back.
- **BRD-206** — The model catalogue shall say per model whether it reads pictures, for built-in models and for a model named by its Hugging Face repository (read from the model's own configuration), and `SupportsInput(Image)` shall give the same answer for the loaded model *(owner, added 2026-11-05)* *Screen:* Local picture reading
  - *Acceptance:* When a developer on Local picture reading lists the catalogue, then the two vision models say they read pictures and Qwen2.5 0.5B and Phi-3 mini say they do not.
- **BRD-207** — The DevGuide shall have one sample on the local provider: a picture in, text out, with the same `ChatImage` and `ChatContentPart` code a hosted provider takes *(owner, added 2026-11-05)* *Screen:* Local picture reading
  - *Acceptance:* When a developer on Local picture reading runs the DevGuide sample on a picture, then the local model's text description comes back.
- **BRD-208** — A test with one fixed picture of a calendar page, kept in the test fixtures, shall send it to the local vision model and check that the text contains each meeting title and time on that page *(owner, added 2026-11-05)* *Screen:* Local picture reading
  - *Acceptance:* When the calendar-page test runs on Local picture reading, then every meeting title and its time on the fixed picture appears in the model's text.

- **BRD-209** — A local model that does not read pictures, given a chat turn with an image, shall throw a clear error naming the model before any inference; an image is never dropped without notice *(owner, added 2026-11-05; today the template formatter keeps only the text parts)* *Screen:* Local picture reading
  - *Acceptance:* When a developer on Local picture reading sends an image to Phi-3 mini, then the call throws an error naming Phi-3 mini before inference.

### Local model lifetime

A host can unload or swap a local model while calls are still using it, and the process never crashes.

- **BRD-189** — Disposing a local model provider shall cancel or wait for the calls already inside it, free the native model only once none is left, and make every call that resumes afterwards throw ObjectDisposedException before it touches the model *(Sevak feedback TR-RAG-053, added 2026-10-11)* *Screen:* Local model lifetime
  - *Acceptance:* When a developer disposes a local provider on Local model lifetime while three calls are inside, then each ends with ObjectDisposedException and the model is freed once.

### PostgreSQL end to end

A host offers its users a server database of their own: vectors, conversations and workspaces in one PostgreSQL, proven on a real server in CI.

- **BRD-210** — The PostgreSQL path shall be proven end to end on a real PostgreSQL 18 with pgvector that the test harness starts in Docker: `UsePgVector` for the vectors and the PostgreSQL conversation and workspace stores in the same database, reached through one `UsePostgres(connectionString)` builder call, with `LivePgVectorStoreTests` (BRD-157) and live store tests run in CI, so a host can offer its users a server database of their own *(Sevak feedback TR-RAG-055; Sevak phase 4, BRD-182 to BRD-185; owner, 2026-11-07)* The live tests read their connection string from a settings file beside the tests (a git-ignored `livetests.settings.json` with a committed example), never from an environment variable (owner's rule, 2026-11-07; today `LivePostgresFactAttribute` reads `TechieRagTestPostgres`). *Screen:* PostgreSQL end to end
  - *Acceptance:* When a developer runs the live PostgreSQL tests on PostgreSQL end to end against the harness-started server, then vectors, conversations and workspaces round-trip in one database.

### Host data skill

A host's assistant reads and changes the host's own data in plain words, and every change can be undone.

- **BRD-211** — The library shall offer a host-data skill: a host registers its own entities (name, fields, and handlers to list, read, create, update and delete) and the agent gets one tool set for them, so a host's assistant can read and change the host's data in plain words *(Sevak feedback TR-RAG-056; Sevak BRD-203; owner, 2026-11-07)* *Screen:* Host data skill
  - *Acceptance:* When a developer registers a "task" entity on Host data skill and a user asks for a task, then the create handler runs with the extracted fields.
- **BRD-212** — Every change the host-data skill makes shall come back to the host as a typed change record (entity, id, before, after, the turn that caused it), and the skill shall reverse a record on request through the host's own handlers, so a host can show a card with Undo *(Sevak feedback TR-RAG-057; Sevak BRD-204; owner, 2026-11-07)* *Screen:* Host data skill
  - *Acceptance:* When a developer calls Undo with a change record on Host data skill, then the entity is restored to its "before" state through the host's handler.

### Image generation

Images from a prompt, through the same routing as the chat providers.

- **BRD-213** — The library shall generate images: an `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, for OpenAI, Gemini and Venice, routed like the chat providers, with no key ever persisted; this re-scopes BRD-161 for images only *(Sevak feedback TR-RAG-058; Sevak BRD-127 and BRD-281; owner, 2026-11-07)* *Screen:* Image generation
  - *Acceptance:* When a developer calls GenerateImageAsync on Image generation with a prompt, a size and a keyed provider, then PNG bytes of that size and the usage come back.
- **BRD-214** — Image generation shall take an `openai-compatible/<model>` route that reaches any service speaking OpenAI's image format at an endpoint the app gives (its Base URL or an `@https://` suffix on the route, as the chat route does), and refuse it with a clear message when no endpoint is given *(owner, 2026-11-08)* *Screen:* Image generation
  - *Acceptance:* When a developer creates openai-compatible/<model> on Image generation with an endpoint and key, then requests reach that endpoint; without an endpoint, it is refused.
- **BRD-215** — Image generation shall take an `azure-openai/<deployment>` route to an Azure OpenAI resource: the resource endpoint is required, the key goes in the `api-key` header, and the library sets the `api-version` *(owner, 2026-11-08)* *Screen:* Image generation
  - *Acceptance:* When a developer creates azure-openai/<deployment> on Image generation with an endpoint and key, then the request reaches that deployment with the api-key header.

## 5. Development status

Written by the status gate after every build, verify and handoff; not by hand.

**Snapshot as of 2026-11-13.** Live per-requirement status: `PROJECT-STATUS.md` and the Requirements Status table in `docs/TechieRag-P3-Checklist.md`.

| Screen | Requirements | Verified | Open | Status |
|---|---|---|---|---|
| RAG / AI requirements | 12 | 12 | 0 | Done |
| Local model fit check | 1 | 1 | 0 | Done |
| Model routing | 8 | 8 | 0 | Done |
| Local model lifetime | 1 | 1 | 0 | Done |
| LLM streaming | 8 | 8 | 0 | Done |
| Local picture reading (`LocalModel`, `LocalLlmProvider` as `IMultimodalLlmProvider`, `ChatImage`, `ChatContentPart`) | 7 | 6 | 1 | Partial |
| Host data skill (`HostDataSkill`, `IHostEntity`, `HostDataChange`) | 2 | 2 | 0 | Done |
| Image generation (`IImageGenerationProvider`, `GenerateImageAsync`) | 3 | 2 | 1 | Partial |

## 6. Where the rest lives

| What | Where |
|---|---|
| Scope, users and roles, the context diagram | [phase 1 BRD](TechieRag-BRD.md) |
| Non-functional requirements for the whole library | [phase 1 BRD](TechieRag-BRD.md) |
| Constraints, assumptions and risks | [phase 1 BRD](TechieRag-BRD.md) |
| Every phase, its screens and its BRD range | [TechieRag-Phases.md](TechieRag-Phases.md) |
| This phase's work list | [TechieRag-P3-Checklist.md](TechieRag-P3-Checklist.md) |
| The defects these items answer | `Sevak/docs/Sevak-TechieRag-Feedback.md` (Sevak repository) |
