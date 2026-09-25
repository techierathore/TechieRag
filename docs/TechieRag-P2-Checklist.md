# TechieRag — Checklist — Phase 2

| | |
|---|---|
| App | TechieRag |
| Size | Medium |
| Phase | 2 of 2 |

## Goal

Deliver phase 2 of `docs/TechieRag-P2-BRD.md`: the agents package and repository separation (2026-09-03), four-platform groundwork, `TechieRag.Local`, typed streaming and subscription sign-in (2026-09-24), and the v3 library features harvested from the application ledger with their statuses carried in. REQ ids run on from phase 1 and are never reused.

## Requirements Status

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-RAG-015 | LIB: agentic retrieval contract in core — `TechieRag.Agentic` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-015 RegisteredSearchReturnsRefsScoresAndStatus | [view](#d-req-rag-015) |
| REQ-RAG-016 | LIB: `TechieRag.Agents` package — Microsoft Agent Framework agent over TechieRag, builder in the existing style, LM Studio primary | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-016 BuiltAgentAnswersFromDocuments | [view](#d-req-rag-016) |
| REQ-RAG-017 | LIB: public seam adapters in `TechieRag.Agents` — `ILlmProvider`→`IChatClient`, `IToolHandler`↔`AITool`, middleware→`IProgress<AgentStep>`, `IConversationMemory`→`ChatHistoryProvider` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-017 AgentReadsAndWritesConversationMemory | [view](#d-req-rag-017) |
| REQ-FN-004 | Consumer-facing install documentation shall lead with the public feed (`dotnet add package TechieRag` / `TechieRag.Embedded`, no authentication, no PA | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-004 ReadmeInstallLeadsWithPublicFeed | [view](#d-req-fn-004) |
| REQ-FN-005 | Pack + publish `TechieRag.Agents` alongside the other two packages on both feeds at the same version | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-005 PublicWorkflowChecksEveryPackageId | [view](#d-req-fn-005) |
| REQ-FN-006 | Repository separation — the app (now **Sevak**, TechieDesk renamed) leaves this repo; solution = `src/*` + `tests/TechieRag.Tests` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-006 RepositoryHoldsOnlyTheLibrary | [view](#d-req-fn-006) |
| REQ-RAG-042 | The system shall run declarative agent flows: five node kinds (Agent, Tool, Condition, Handoff, Terminal), `FlowCondition` routing as data with 11 ope | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-042 AFiveKindFlowStopsAtMaxSteps | [view](#d-req-rag-042) |
| REQ-RAG-044 | The shipped `IVectorStore` set is SQLite, pgvector and Qdrant; `PgVectorStore` shall be proven against a real PostgreSQL through `LivePgVectorStoreTes | PARTIAL | 75% | 2026-09-25 verify: not verified — every test named REQ-RAG-044 was skipped; acceptance not measured (2 test(s) skipped, none ran) | [view](#d-req-rag-044) |
| REQ-RAG-046 | Image generation, realtime audio, batch, fine-tuning, moderation and OCR endpoints are deferred and re-scoped on demand | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-046 BrdMarksEndpointsDeferred | [view](#d-req-rag-046) |
| REQ-RAG-050 | Every user-visible message the flow engine produces (refusals, budget exhaustion, validation) shall be a `FlowMessage` carrying a `FlowMessageCodes` c | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-050 ARefusedToolCallGivesTheTraceRowACodeAndItsArgum | [view](#d-req-rag-050) |
| REQ-RAG-051 | `AgentToolHandler.ForFlow` shall report a blocked or budget-exhausted inner run as an unsuccessful `ToolResult` (`IsSuccess = false`), never as a succ | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-051 ABudgetExhaustedSubFlowReportsAFailedToolCall | [view](#d-req-rag-051) |
| REQ-RAG-052 | `EmbeddedEmbeddingProvider` shall encode text as XLM-RoBERTa expects (SentencePiece ids shifted by the fairseq offset, wrapped in `<s>` … `</s>`), eve | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-052 AnEarlierRevisionOfTheSameModelIsStale | [view](#d-req-rag-052) |
| REQ-FN-054 | F-PLATFORM: platform support matrix in BRD §9 and the UsageGuide, per package × platform, tested cells name a device and date | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-054 PlatformMatrixCellsAreClassified | [view](#d-req-fn-054) |
| REQ-RAG-053 | F-PLATFORM: model and cache root defaults to the per-user application data folder, host-overridable; embedded model, reranker and local LLM share it | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-053 AppleRootIsApplicationSupportNotDocuments | [view](#d-req-rag-053) |
| REQ-FN-055 | F-PLATFORM: `TechieRag.Embedded` ships buildTransitive targets with the native ONNX Runtime wiring for Mac Catalyst, iOS and Android; Sevak removes its hand-written copy | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-055 ProbeCarriesNoHandWrittenNativeWiring | [view](#d-req-fn-055) |
| REQ-RAG-054 | F-PLATFORM: on Android and iOS `UseEmbedded()` defaults to a 384-dimension model and refuses bge-m3 with a message naming its 2.3 GB size | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-054 PhoneDefaultIs384Dimensions | [view](#d-req-rag-054) |
| REQ-RAG-055 | F-PLATFORM: every model download reports its total size before the first byte and progress through `ModelDownloadService` events | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-055 SizeIsKnownBeforeTheFirstByte | [view](#d-req-rag-055) |
| REQ-RAG-056 | F-PLATFORM: `SqliteVecStore` managed similarity search vectorised and documented as the supported path; dead sqlite-vec code removed or marked; 1k/10k/50k timings recorded | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-056 UsageGuideRecordsSearchBenchmark | [view](#d-req-rag-056) |
| REQ-FN-056 | F-PLATFORM: `samples/TechieRag.Probe`, a .NET MAUI app with Windows, Mac Catalyst, Android and iOS heads; one button embeds three texts, stores, searches, shows the top result and timings | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-056 CoreAssemblyUsesNoReflectionEmit | [view](#d-req-fn-056) |
| REQ-FN-057 | F-PLATFORM: CI builds the probe for all four heads on every push and runs its button on an Android emulator where CI allows; an unbuildable head is reported, never skipped | PARTIAL | 75% | [REQ-FN-057] 2026-09-25 verify: tests PASS (workflow builds and reports all four heads); acceptance needs a real CI run page, which exists only after the owner's next push — held at PARTIAL by flow-master | [view](#d-req-fn-057) |
| REQ-RAG-057 | F-LOCAL-LLM: `TechieRag.Local` package with `.UseLocalLlm()` via `UseCustomLlmProvider`, platform-appropriate default model, overloads for a model id or folder; nothing in core | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-057 PhoneModelIsOwnConversion | [view](#d-req-rag-057) |
| REQ-RAG-058 | F-LOCAL-LLM: one public provider over an internal `ILocalLlmRuntime` with one implementation per platform per `DECISIONS.md`; identical behaviour everywhere | PARTIAL | 75% | [REQ-RAG-058] 2026-09-25 verify: tests PASS; real-engine conformance ran on macOS (both models, 17/17) and the probe generated on Mac Catalyst and the iOS simulator; conformance on Windows and Android not yet run — held at PARTIAL by flow-master | [view](#d-req-rag-058) |
| REQ-RAG-059 | F-LOCAL-LLM: the provider fully implements `ILlmProvider` including typed streaming, applies the chat template, honours every option and cancellation, refuses an over-long prompt | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-059 PenaltiesMapToRepetitionPenalty | [view](#d-req-rag-059) |
| REQ-RAG-060 | F-LOCAL-LLM: memory check before load; refuses with a clear message instead of letting the OS kill the app | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-060 ShortfallIsNamed | [view](#d-req-rag-060) |
| REQ-RAG-061 | F-LOCAL-LLM: one-time resumable download to the app-data root, `TECHIERAG_MODEL_BASE_URL`, SHA-256 verified, size known first, `ModelDownloadService` progress | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-061 InterruptedDownloadResumes | [view](#d-req-rag-061) |
| REQ-RAG-062 | F-LOCAL-LLM: download requires explicit acceptance of the model's terms; weights never inside the package or the app bundle | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-062 DownloadWithoutTermsRequestsNothing | [view](#d-req-rag-062) |
| REQ-FN-058 | F-LOCAL-LLM: `TechieRag.Local` ships buildTransitive targets with the native runtime libraries for all four platforms; no hand-written csproj changes | PARTIAL | 75% | [REQ-FN-058] 2026-09-25 verify: tests PASS; probe built with no native wiring and loaded the runtime on Mac Catalyst and the iOS simulator; Android and Windows heads not built on this Mac (no Android SDK; Windows needs the laptop or CI) — held at PARTIAL by flow-master | [view](#d-req-fn-058) |
| REQ-RAG-063 | F-LOCAL-LLM: `CompleteAsync<T>` and JSON mode return valid JSON for the schema; `SupportsToolCalling` false until a test proves otherwise | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-063 JsonRequestsBecomeGuidance | [view](#d-req-rag-063) |
| REQ-RAG-064 | F-LOCAL-LLM: `LlmSource.Local`, a `LlmProviderFactory` arm and a `LlmConnectorCatalog` row so `ModelRouter` resolves `local/<model>`; no endpoint, no key | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-064 RouterResolvesLocalModel | [view](#d-req-rag-064) |
| REQ-RAG-065 | F-LOCAL-LLM: `EstimateTokenCount` uses the model's real tokenizer | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-065 EstimateTokenCountUsesModelTokenizer (FakeRuntim | [view](#d-req-rag-065) |
| REQ-FN-059 | F-LOCAL-LLM: the probe app's second button generates one sentence from the local model and shows time to first token and tokens per second | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-059 ProbeSecondButtonShowsSentenceAndTimings | [view](#d-req-fn-059) |
| REQ-RAG-066 | F-LOCAL-LLM: live tests gated by `LiveLocalLlmFactAttribute` in a non-parallel collection, skipping with a reason when no model is present | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-066 LiveTestsSkipWithReasonWhenNoModel | [view](#d-req-rag-066) |
| REQ-FN-060 | F-LOCAL-LLM: tokens per second, time to first token and peak memory recorded per platform on named devices in the support matrix | PARTIAL | 75% | [REQ-FN-060] 2026-09-25 verify: test PASS; matrix has Windows laptop, M4 Max Mac, Mac Catalyst and iOS-simulator numbers with device and date; Android phone and physical iPhone cells say "not yet run" (owner devices) — held at PARTIAL by flow-master | [view](#d-req-fn-060) |
| REQ-FN-061 | F-LOCAL-LLM: UsageGuide gains a `TechieRag.Local` section; matrix updated; every 'offline' / 'air-gapped' claim corrected to 'downloads once, then works offline' | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-061 NoPageClaimsOfflineWithoutDownloadingOnce | [view](#d-req-fn-061) |
| REQ-RAG-067 | F-LLM: additive typed streaming method on `ILlmProvider` (text delta, tool call, completed-with-usage); `ChatStreamAsync` unchanged and implemented over it; all six providers | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-067 TypedStreamOrdersTextToolCallCompleted(provider: | [view](#d-req-rag-067) |
| REQ-RAG-068 | F-AGENT: `AgentLoopRunner` streaming run over typed events, tools executed through `ToolRegistry`; `TechieRag.Agents` `IChatClient` adapter streams the same | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-068 StreamingRunWorksOverLmStudioWireFormat | [view](#d-req-rag-068) |
| REQ-RAG-069 | F-LLM: subscription sign-in providers, one builder method per permitting vendor (first `UseChatGptSubscriptionLlm(signInCallback)`), host drives the browser, library yields an `ILlmProvider`, session persisted via a documented seam | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-069 PollWaitsUntilAuthorised | [view](#d-req-rag-069) |
| REQ-FN-062 | F-LLM: per-vendor sign-in policy and flow (OpenAI, Anthropic, Google, Groq, xAI, Meta) checked against live documentation and recorded in `DECISIONS.md` before BRD-112 is built | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-062 DecisionsRecordEveryVendorsSignInPolicy | [view](#d-req-fn-062) |
| REQ-RAG-070 | F-LLM: `LlmSource.Subscription`, factory arm, catalog rows per vendor carrying stated terms and the date checked; `ModelRouter` resolves a subscription model; a vendor with no permitted flow has no method | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-070 EveryRowCarriesTermsAndCheckDate | [view](#d-req-rag-070) |
| REQ-RAG-071 | The system shall offer pluggable chunking through `IChunker` with recursive, token, markdown/code-aware and sentence strategies (`WithChunking`, `WithCustomChun | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-071 MarkdownProcessorKeepsCodeFenceForMarkdownStrate | [view](#d-req-rag-071) |
| REQ-RAG-072 | The system shall extract text from XLSX, PPTX and CSV files through dedicated processors | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-072 IngestsXlsxWorkbook | [view](#d-req-rag-072) |
| REQ-RAG-073 | The system shall ingest audio files through an `AudioTranscriptionProcessor` that transcribes via an OpenAI-compatible speech endpoint; a local Whisper ONNX pat | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-073 AnAudioFileIsTranscribedChunkedAndEmbedded | [view](#d-req-rag-073) |
| REQ-RAG-074 | A developer can ingest a single web page by URL: fetch, clean to text, chunk and embed (`WebIngestionExtensions`, `WebPageReader`) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-074 IngestUrlStoresCleanTextAsOneDocument | [view](#d-req-rag-074) |
| REQ-RAG-075 | A developer can crawl a site with depth and maximum-link limits (`SiteCrawler`, `WebCrawlOptions`) and ingest every page reached | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-075 SiteIngestionStopsAtDepthTwo | [view](#d-req-rag-075) |
| REQ-RAG-076 | ~~A developer can ingest a YouTube video's transcript by URL (`YouTubeTranscriptReader`)~~ | N/A | 100% | Removed 2026-09-24 by owner decision: YouTube ingestion was never asked for in the library (an application feature at most) and had stopped working (TR-RAG-015 in `docs/Sevak-TechieRag-Feedback.md`). Code removal is REQ for BRD-164. Miss logged. Added 2026-09-24 by day-1 brownfield (harvested). | [view](#d-req-rag-076) |
| REQ-RAG-077 | Every outbound web fetch shall pass a connect-time SSRF guard (`HttpWebContentFetcher.CreateGuardedHandler`) that refuses private, loopback and link-local targe | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-077 GuardedHandlerRefusesToConnectToLoopback | [view](#d-req-rag-077) |
| REQ-RAG-078 | The system shall provide an `IDataConnector` framework with a `ConnectorRunner` that returns per-item results and per-item failure reasons and keeps incremental | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-078 ReturnsSyncStateForTheNextRun | [view](#d-req-rag-078) |
| REQ-RAG-079 | A developer can connect a GitHub or GitLab repository and ingest its files with branch and glob filters (`RepositoryConnector`) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-079 RepositoryIngestsOnlyMatchingFilesOnTheBranch | [view](#d-req-rag-079) |
| REQ-RAG-080 | A developer can connect a Confluence space and ingest its pages, with scheme, host and port pinned on cursors and links so the token never reaches a host named  | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-080 ConfluenceSpaceIsIngestedWithoutLeavingTheHost | [view](#d-req-rag-080) |
| REQ-RAG-081 | A developer can ingest a mailbox over IMAP (generic, Gmail, Microsoft 365) or a local mbox file with folder, date, sender and subject filters, optional PDF/DOCX | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-081 PushesTheScopeFiltersToTheServer | [view](#d-req-rag-081) |
| REQ-RAG-082 | Connector transports shall reuse the SSRF-guarded connect-time handler, cap a run at `MaxTotalBytes` (64 MB), and the IMAP client shall refuse command injection | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-082 IngestionCarriesTheByteBudgetCode | [view](#d-req-rag-082) |
| REQ-RAG-083 | `ConnectorRunner` shall hand each fetched document to ingestion as it arrives instead of collecting every document before ingesting, so a run stopped part-way k | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-083 ACancelledRunHasAlreadyIngestedWhatItFetched | [view](#d-req-rag-083) |
| REQ-RAG-084 | The system shall consume Model Context Protocol tool servers in the agent loop through `McpClient` with stdio and HTTP transports and `McpToolHandler` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-084 AgentLoopRunsToolFromHttpMcpServer | [view](#d-req-rag-084) |
| REQ-RAG-085 | The system shall expose an `IMcpServerRegistry` with an in-memory implementation, a `McpTrustPolicy`, and the tool-to-server mapping (`McpToolHandler.ServerName | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-085 EveryQualifiedToolNameResolvesToItsServer | [view](#d-req-rag-085) |
| REQ-RAG-086 | `IFlowGuardrail` shall run at Input, Output and ToolCall, deny by default, and `FlowRuntime.HostGuardrails` shall be set only by the host so a flow definition c | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-086 AHostGuardrailAppliesToANodeThatNamesNoGuardrail | [view](#d-req-rag-086) |
| REQ-RAG-087 | A flow definition shall round-trip through `FlowSerializer` and `FlowValidator` shall refuse an invalid graph (unreachable nodes, cycles without `AllowCycles`,  | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-087 AFlowRoundTripsThroughTheSerializerUnchanged | [view](#d-req-rag-087) |
| REQ-RAG-088 | A flow run shall complete or fail within a configurable time limit; a Tool-node flow that waits on a host confirmation shall end with a coded timeout message in | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-088 GuardrailConfirmationThatNeverAnswersTimesOut | [view](#d-req-rag-088) |
| REQ-RAG-089 | The system shall provide database-backed conversation memory with threads (`DbConversationMemory`, `IConversationStore`) on SQLite and PostgreSQL | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-089 ChatThreadSurvivesARestart | [view](#d-req-rag-089) |
| REQ-RAG-090 | `IConversationStore` shall persist every message of a thread (user, assistant, sources as JSON, content parts) per workspace and user | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-090 MessageReloadsWithRoleContentAndSources | [view](#d-req-rag-090) |
| REQ-RAG-091 | The system shall build the model context from persisted history with token-aware trimming that keeps the system message and the most recent turns | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-091 TrimsHistoryToTokenBudget | [view](#d-req-rag-091) |
| REQ-RAG-092 | The system shall provide workspace primitives (`IWorkspaceStore`, `WorkspaceManager`, `Workspace`): isolated documents and settings, document pinning, a per-wor | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-092 RetrievalSettingsAreIsolatedBetweenWorkspaces | [view](#d-req-rag-092) |
| REQ-RAG-093 | Ingestion shall deduplicate by content hash so a document embedded once is reused across workspaces (`IWorkspaceStore.FindDocumentIdByHashAsync`) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-093 IdenticalContentIsEmbeddedOnceAndReusedAcrossWor | [view](#d-req-rag-093) |
| REQ-RAG-094 | Context assembly shall signal truncation (`WorkspaceContext.WasTruncated`, the `ContextTruncated` event) and evict retrieved chunks before pinned ones | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-094 RetrievedChunksAreEvictedBeforePinnedChunks | [view](#d-req-rag-094) |
| REQ-RAG-095 | Deleting a document shall remove its vectors and its membership from every workspace; `IWorkspaceStore.RemoveDocumentAsync` deletes only the membership row toda | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-095 DeletingADocumentRemovesItsVectorsAndEveryMember | [view](#d-req-rag-095) |
| REQ-RAG-096 | `PromptTemplateEngine.FormatContext` shall signal truncation the same way `WorkspaceManager` does instead of cutting context silently (TR-RAG-009) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-096 TheChatPromptAlsoSignalsTruncation | [view](#d-req-rag-096) |
| REQ-RAG-097 | The system shall provide an `IReranker` stage with a local ONNX cross-encoder (`OnnxCrossEncoderReranker`, bge-reranker-v2-m3, in `TechieRag.Embedded`) and API  | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-097 SearchReturnsTheRerankersOrder | [view](#d-req-rag-097) |
| REQ-RAG-098 | A developer can switch reranking per call through `SearchOptions.Rerank` and set the default with `WithRerankEnabledByDefault`; the workspace flag is authoritat | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-098 PerCallRerankFalseOverridesGlobalEnabled | [view](#d-req-rag-098) |
| REQ-RAG-099 | A developer can pick a hosted model by name alone: `ModelRouter` resolves the longest unambiguous prefix in `LlmConnectorCatalog` (OpenAI, Anthropic, Gemini, Gr | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-099 BuilderResolvesOpenAiFromTheModelNameAlone | [view](#d-req-rag-099) |
| REQ-RAG-100 | A developer can use Cohere, Voyage, Mistral and Google Gemini embedding providers (`UseCohereEmbedding`, `UseVoyageEmbedding`, `UseMistralEmbedding`, `UseGemini | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-100 VendorEntryPointTargetsTheVendorsApi(vendor: "Ge | [view](#d-req-rag-100) |
| REQ-RAG-101 | A developer can send images with a chat message through `IMultimodalLlmProvider`, `ChatImage` and `ChatContentPart`, mapped to each vendor's wire shape; audio a | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-101 GeminiSendsInlineDataPart | [view](#d-req-rag-101) |
| REQ-RAG-102 | The system shall pass prompt-caching hints through to Anthropic and Gemini (`PromptCacheOptions`) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-102 GeminiSendsTheCachedContentName | [view](#d-req-rag-102) |
| REQ-RAG-103 | Streaming RAG shall return source citations alongside the text (`RagStreamEvent`, `AskStreamWithSourcesAsync`, `ChatWithRagStreamWithSourcesAsync`) and honour ` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-103 StreamingYieldsSourcesThenTokensThenCompleted | [view](#d-req-rag-103) |
| REQ-RAG-104 | Cost shall come from a configurable pricing table (`WithModelPricing`, `ModelPricing`) and streamed-token usage shall be reported correctly on every provider | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-104 StreamedTokensAreRecordedAndPriced | [view](#d-req-rag-104) |
| REQ-RAG-105 | The system shall provide `ISpeechToText` and `ITextToSpeech` abstractions with OpenAI-compatible providers (`UseSpeechToText`) | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-105 TranscribeReadsTextLanguageAndDuration | [view](#d-req-rag-105) |
| REQ-FN-063 | `TechieRag` and `TechieRag.Telemetry` shall target `net10.0` and `net8.0`; `TechieRag.Embedded` targets `net10.0` because ONNX Runtime requires it | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-063 ANet8ConsumerRestoresAndBuilds | [view](#d-req-fn-063) |
| REQ-FN-064 | `TechieRag.Telemetry` shall be a separate opt-in package: OTLP or console exporters, tracing and metrics off by default, a loopback endpoint by default and a no | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-064 EnabledPipelineDeliversSpansAndMetricsToTheCollec | [view](#d-req-fn-064) |
| REQ-FN-065 | Both publishing workflows shall pack and publish `TechieRag.Telemetry` with the same SourceLink, symbol and README settings as the other packages; today no work | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-065 TelemetryIsPackedAndPushedAtTheSharedVersion | [view](#d-req-fn-065) |
| REQ-RAG-106 | `TechieRagBuilder.Build()` shall pass the embedding provider's `Dimensions` into every vector store instead of the 1024 default, so Cohere, OpenAI and Gemini em | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-106 PgVectorTakesTheEmbeddersDimensions | [view](#d-req-rag-106) |
| REQ-FN-066 | `AddTechieRag(IConfiguration)` and `AddTechieRag(TechieRagConfig)` shall map every configuration field the builder accepts, including `VectorStore.ApiKey`, embe | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-066 AppSettingsApiKeyAndSystemPromptReachBuiltInstanc | [view](#d-req-fn-066) |
| REQ-FN-067 | Exactly one path shall publish to nuget.org: the manual dispatch of `publish-nuget.yml`; the automatic `publish-nuget-org` job in `publish-github-packages.yml`  | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-067 PublicWorkflowIsManualDispatchOnly | [view](#d-req-fn-067) |
| REQ-FN-068 | Unit tests shall cover processors, providers, the agent loop, memory and cost math; live tests are gated by attributes that skip with a reason | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-068 LiveTestClassesUseGatedAttributes | [view](#d-req-fn-068) |
| REQ-RAG-107 | `YouTubeTranscriptReader`, `YouTubeUrl`, the YouTube entry point in `WebIngestionExtensions` and their tests (including `Web/Live/LiveYouTubeTranscriptTests.cs` | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-107 SourceHasNoYouTubeTypeOrEntryPoint | [view](#d-req-rag-107) |
| REQ-FN-069 | The UsageGuide's Platform notes shall carry the owner's step-by-step runbook for the probe app: how to build and deploy it to a Mac, an Android phone and an iPh | Verified | 100% | 2026-09-25 verify: PASS — test REQ-FN-069 UsageGuideHasANumberedProbeRunbookPerDevice | [view](#d-req-fn-069) |
| REQ-RAG-108 | F-LOCAL-LLM: `LocalModel.FromHuggingFace(repository, folder, version)` runs any ONNX Runtime GenAI model on Hugging Face by name, licence first, every file checked; phone default stays our own Qwen on the owner's account | Verified | 100% | 2026-09-25 verify: PASS — test REQ-RAG-108 MessagesForOwnTemplateAlternate | [view](#d-req-rag-108) |

**Status values:** `Not Started` · `In Progress` · `Implemented` · `Verified` · `Done (pre-existing)` · `Needs re-verify` · `PARTIAL` · `FAIL` · `Blocked` · `Owner-UAT` · `N/A`. `Owner-UAT` marks a row only the owner can close, by following the UsageGuide test plan; the verifier cannot reach it from this machine.

## Surface: Agents package

<a id="d-req-rag-015"></a>
- **REQ-RAG-015** — LIB: agentic retrieval contract in core — `TechieRag.Agentic`. *BRD:* BRD-83. *Phase 2.*
  - *Acceptance:* When a developer registers the knowledge-base tools from `TechieRag.Agentic` on the agent loop, then `search_knowledge_base` returns refs, scores and a strong/weak/none status.

<a id="d-req-rag-016"></a>
- **REQ-RAG-016** — LIB: `TechieRag.Agents` package — Microsoft Agent Framework agent over TechieRag, builder in the existing style, LM Studio primary. *BRD:* BRD-84. *Phase 2.*
  - *Acceptance:* When a developer builds an agent with `TechieRagAgentBuilder` over a TechieRag instance, then the agent answers from the documents through Microsoft Agent Framework.

<a id="d-req-rag-017"></a>
- **REQ-RAG-017** — LIB: public seam adapters in `TechieRag.Agents` — `ILlmProvider`→`IChatClient`, `IToolHandler`↔`AITool`, middleware→`IProgress<AgentStep>`, `IConversationMemory`→`ChatHistoryProvider`. *BRD:* BRD-85. *Phase 2.*
  - *Acceptance:* When a developer adapts an `ILlmProvider`, `IToolHandler` or `IConversationMemory` through the public seam adapters, then the agent uses them unchanged.

## Surface: Packaging and quality follow-ups

<a id="d-req-fn-004"></a>
- **REQ-FN-004** — Consumer-facing install documentation shall lead with the public feed (`dotnet add package TechieRag` / `TechieRag.Embedded`, no authentication, no PA. *BRD:* BRD-163. *Phase 2.*
  - *Acceptance:* When a reader opens the README installation section, then the first path is `dotnet add package TechieRag` from nuget.org with no token.

<a id="d-req-fn-005"></a>
- **REQ-FN-005** — Pack + publish `TechieRag.Agents` alongside the other two packages on both feeds at the same version. *BRD:* BRD-86. *Phase 2.*
  - *Acceptance:* When a maintainer runs either publishing workflow on GitHub Actions, then `TechieRag.Agents` is packed and pushed at the same version with the same metadata.

<a id="d-req-rag-044"></a>
- **REQ-RAG-044** — The shipped `IVectorStore` set is SQLite, pgvector and Qdrant; `PgVectorStore` shall be proven against a real PostgreSQL through `LivePgVectorStoreTes. *BRD:* BRD-157. *Phase 2.*
  - *Acceptance:* When `LivePgVectorStoreTests` run against a real PostgreSQL, then upsert, search and delete pass.

<a id="d-req-rag-046"></a>
- **REQ-RAG-046** — Image generation, realtime audio, batch, fine-tuning, moderation and OCR endpoints are deferred and re-scoped on demand. *BRD:* BRD-161. *Phase 2.*
  - *Acceptance:* When a reader checks the deferred endpoints, then the BRD says deferred and no code claims them.

<a id="d-req-fn-065"></a>
- **REQ-FN-065** — Both publishing workflows shall pack and publish `TechieRag.Telemetry` with the same SourceLink, symbol and README settings as the other packages; today no work. *BRD:* BRD-156. *Phase 2.*
  - *Acceptance:* When a maintainer runs either publishing workflow on GitHub Actions, then a `TechieRag.Telemetry` package is packed and pushed at the same version.

<a id="d-req-rag-106"></a>
- **REQ-RAG-106** — `TechieRagBuilder.Build()` shall pass the embedding provider's `Dimensions` into every vector store instead of the 1024 default, so Cohere, OpenAI and Gemini em. *BRD:* BRD-158. *Phase 2.*
  - *Acceptance:* When a developer builds with a 1536-dimension embedder and pgvector, then the `Embedding` column is `vector(1536)`.

<a id="d-req-fn-066"></a>
- **REQ-FN-066** — `AddTechieRag(IConfiguration)` and `AddTechieRag(TechieRagConfig)` shall map every configuration field the builder accepts, including `VectorStore.ApiKey`, embe. *BRD:* BRD-159. *Phase 2.*
  - *Acceptance:* When a developer sets `VectorStore.ApiKey` and `Prompt.SystemPrompt` in appsettings, then the built instance uses both.

<a id="d-req-fn-067"></a>
- **REQ-FN-067** — Exactly one path shall publish to nuget.org: the manual dispatch of `publish-nuget.yml`; the automatic `publish-nuget-org` job in `publish-github-packages.yml` . *BRD:* BRD-160. *Phase 2.*
  - *Acceptance:* When a maintainer pushes a `v*` tag on GitHub, then GitHub Packages receives the packages and nuget.org receives nothing until the manual dispatch.

<a id="d-req-fn-068"></a>
- **REQ-FN-068** — Unit tests shall cover processors, providers, the agent loop, memory and cost math; live tests are gated by attributes that skip with a reason. *BRD:* BRD-162. *Phase 2.*
  - *Acceptance:* When `dotnet test tests/TechieRag.Tests` runs on a host without live credentials, then every non-live test passes and live tests skip with a reason.

## Surface: Repository separation

<a id="d-req-fn-006"></a>
- **REQ-FN-006** — Repository separation — the app (now **Sevak**, TechieDesk renamed) leaves this repo; solution = `src/*` + `tests/TechieRag.Tests`. *BRD:* BRD-87. *Phase 2.*
  - *Acceptance:* When a maintainer opens the repository after the split, then only `src/`, `tests/TechieRag.Tests` and library docs remain and Sevak consumes the 1.0.7 packages.

## Surface: Flow orchestration

<a id="d-req-rag-042"></a>
- **REQ-RAG-042** — The system shall run declarative agent flows: five node kinds (Agent, Tool, Condition, Handoff, Terminal), `FlowCondition` routing as data with 11 ope. *BRD:* BRD-130. *Phase 2.*
  - *Acceptance:* When a developer runs a five-node flow with a condition and a handoff, then routing follows the data and the run stops at `MaxSteps`.

<a id="d-req-rag-050"></a>
- **REQ-RAG-050** — Every user-visible message the flow engine produces (refusals, budget exhaustion, validation) shall be a `FlowMessage` carrying a `FlowMessageCodes` c. *BRD:* BRD-132. *Phase 2.*
  - *Acceptance:* When the engine refuses a step, then the message reaching the host carries a `FlowMessageCodes` code with arguments.

<a id="d-req-rag-051"></a>
- **REQ-RAG-051** — `AgentToolHandler.ForFlow` shall report a blocked or budget-exhausted inner run as an unsuccessful `ToolResult` (`IsSuccess = false`), never as a succ. *BRD:* BRD-133. *Phase 2.*
  - *Acceptance:* When an inner agent run is blocked or exhausts its budget, then `AgentToolHandler.ForFlow` returns a `ToolResult` with `IsSuccess` false.

<a id="d-req-rag-086"></a>
- **REQ-RAG-086** — `IFlowGuardrail` shall run at Input, Output and ToolCall, deny by default, and `FlowRuntime.HostGuardrails` shall be set only by the host so a flow definition c. *BRD:* BRD-131. *Phase 2.*
  - *Acceptance:* When a host guardrail denies a tool call, then the flow reports the denial and the flow definition cannot bypass the host guardrail.

<a id="d-req-rag-087"></a>
- **REQ-RAG-087** — A flow definition shall round-trip through `FlowSerializer` and `FlowValidator` shall refuse an invalid graph (unreachable nodes, cycles without `AllowCycles`, . *BRD:* BRD-134. *Phase 2.*
  - *Acceptance:* When a developer serialises a flow and validates a graph with a cycle, then the round-trip is identical and validation refuses the cycle with a code.

<a id="d-req-rag-088"></a>
- **REQ-RAG-088** — A flow run shall complete or fail within a configurable time limit; a Tool-node flow that waits on a host confirmation shall end with a coded timeout message in. *BRD:* BRD-135. *Phase 2.*
  - *Acceptance:* When a Tool-node flow waits longer than the time limit for a confirmation, then the run ends with a coded timeout message.

## Surface: Provider breadth

<a id="d-req-rag-052"></a>
- **REQ-RAG-052** — `EmbeddedEmbeddingProvider` shall encode text as XLM-RoBERTa expects (SentencePiece ids shifted by the fairseq offset, wrapped in `<s>` … `</s>`), eve. *BRD:* BRD-148. *Phase 2.*
  - *Acceptance:* When a Hindi query is embedded by `EmbeddedEmbeddingProvider`, then it ranks a relevant English passage above an irrelevant one and stale vectors are detected.

<a id="d-req-rag-099"></a>
- **REQ-RAG-099** — A developer can pick a hosted model by name alone: `ModelRouter` resolves the longest unambiguous prefix in `LlmConnectorCatalog` (OpenAI, Anthropic, Gemini, Gr. *BRD:* BRD-146. *Phase 2.*
  - *Acceptance:* When a developer calls `UseLlmForModel("gpt-4o")`, then the router resolves the OpenAI connector and the provider is built without naming it.

<a id="d-req-rag-100"></a>
- **REQ-RAG-100** — A developer can use Cohere, Voyage, Mistral and Google Gemini embedding providers (`UseCohereEmbedding`, `UseVoyageEmbedding`, `UseMistralEmbedding`, `UseGemini. *BRD:* BRD-147. *Phase 2.*
  - *Acceptance:* When a developer calls `UseCohereEmbedding`, `UseVoyageEmbedding`, `UseMistralEmbedding` or `UseGeminiEmbedding`, then embeddings reach that vendor's endpoint.

<a id="d-req-rag-101"></a>
- **REQ-RAG-101** — A developer can send images with a chat message through `IMultimodalLlmProvider`, `ChatImage` and `ChatContentPart`, mapped to each vendor's wire shape; audio a. *BRD:* BRD-149. *Phase 2.*
  - *Acceptance:* When a developer sends a chat message with an image to a multimodal provider, then the image reaches the vendor in its wire shape.

<a id="d-req-rag-102"></a>
- **REQ-RAG-102** — The system shall pass prompt-caching hints through to Anthropic and Gemini (`PromptCacheOptions`). *BRD:* BRD-150. *Phase 2.*
  - *Acceptance:* When a developer sets `PromptCacheOptions` on an Anthropic or Gemini call, then the caching hint is present in the request.

<a id="d-req-rag-103"></a>
- **REQ-RAG-103** — Streaming RAG shall return source citations alongside the text (`RagStreamEvent`, `AskStreamWithSourcesAsync`, `ChatWithRagStreamWithSourcesAsync`) and honour `. *BRD:* BRD-151. *Phase 2.*
  - *Acceptance:* When a developer iterates `AskStreamWithSourcesAsync`, then source events arrive with the text events.

<a id="d-req-rag-104"></a>
- **REQ-RAG-104** — Cost shall come from a configurable pricing table (`WithModelPricing`, `ModelPricing`) and streamed-token usage shall be reported correctly on every provider. *BRD:* BRD-152. *Phase 2.*
  - *Acceptance:* When a developer streams a completion with a pricing table set, then usage and cost for the streamed tokens are recorded correctly.

<a id="d-req-rag-105"></a>
- **REQ-RAG-105** — The system shall provide `ISpeechToText` and `ITextToSpeech` abstractions with OpenAI-compatible providers (`UseSpeechToText`). *BRD:* BRD-153. *Phase 2.*
  - *Acceptance:* When a developer calls `UseSpeechToText` with an OpenAI-compatible endpoint and transcribes audio, then text returns.

<a id="d-req-fn-063"></a>
- **REQ-FN-063** — `TechieRag` and `TechieRag.Telemetry` shall target `net10.0` and `net8.0`; `TechieRag.Embedded` targets `net10.0` because ONNX Runtime requires it. *BRD:* BRD-154. *Phase 2.*
  - *Acceptance:* When a consumer on `net8.0` references `TechieRag` and `TechieRag.Telemetry`, then restore and build succeed.

<a id="d-req-fn-064"></a>
- **REQ-FN-064** — `TechieRag.Telemetry` shall be a separate opt-in package: OTLP or console exporters, tracing and metrics off by default, a loopback endpoint by default and a no. *BRD:* BRD-155. *Phase 2.*
  - *Acceptance:* When a developer adds `TechieRag.Telemetry` with defaults on a host app, then nothing is exported; with `EnableTracing` and a loopback endpoint, spans reach it.

## Surface: Platform groundwork

<a id="d-req-fn-054"></a>
- **REQ-FN-054** — F-PLATFORM: platform support matrix in BRD §9 and the UsageGuide, per package × platform, tested cells name a device and date. *BRD:* BRD-88. *Phase 2.*
  - *Acceptance:* When a reader opens the UsageGuide's platform matrix, then every package × platform cell reads supported, tested (device and date) or not supported.

<a id="d-req-rag-053"></a>
- **REQ-RAG-053** — F-PLATFORM: model and cache root defaults to the per-user application data folder, host-overridable; embedded model, reranker and local LLM share it. *BRD:* BRD-89. *Phase 2.*
  - *Acceptance:* When a developer calls `UseEmbedded()` in a MAUI app with no override, then model files land under the per-user application data folder on every platform.

<a id="d-req-fn-055"></a>
- **REQ-FN-055** — F-PLATFORM: `TechieRag.Embedded` ships buildTransitive targets with the native ONNX Runtime wiring for Mac Catalyst, iOS and Android; Sevak removes its hand-written copy. *BRD:* BRD-90. *Phase 2.*
  - *Acceptance:* When a developer builds the probe app for Mac Catalyst, iOS or Android with no native wiring in its csproj, then ONNX Runtime loads and embeds.

<a id="d-req-rag-054"></a>
- **REQ-RAG-054** — F-PLATFORM: on Android and iOS `UseEmbedded()` defaults to a 384-dimension model and refuses bge-m3 with a message naming its 2.3 GB size. *BRD:* BRD-91. *Phase 2.*
  - *Acceptance:* When a developer calls `UseEmbedded()` on Android or iOS, then the 384-dimension model is selected and asking for bge-m3 throws a message naming 2.3 GB.

<a id="d-req-rag-055"></a>
- **REQ-RAG-055** — F-PLATFORM: every model download reports its total size before the first byte and progress through `ModelDownloadService` events. *BRD:* BRD-92. *Phase 2.*
  - *Acceptance:* When a model download starts in any consuming app, then a size-known event fires before the first byte and progress events follow.

<a id="d-req-rag-056"></a>
- **REQ-RAG-056** — F-PLATFORM: `SqliteVecStore` managed similarity search vectorised and documented as the supported path; dead sqlite-vec code removed or marked; 1k/10k/50k timings recorded. *BRD:* BRD-93. *Phase 2.*
  - *Acceptance:* When a developer runs the search benchmark on 1,000, 10,000 and 50,000 chunks, then the recorded timings and identical ranking to the old path appear in the UsageGuide.

<a id="d-req-fn-056"></a>
- **REQ-FN-056** — F-PLATFORM: `samples/TechieRag.Probe`, a .NET MAUI app with Windows, Mac Catalyst, Android and iOS heads; one button embeds three texts, stores, searches, shows the top result and timings. *BRD:* BRD-94. *Phase 2.*
  - *Acceptance:* When a user presses the probe app's button on Windows, Mac Catalyst, Android or iOS, then the top result and embed, store and search timings appear on screen.

<a id="d-req-fn-057"></a>
- **REQ-FN-057** — F-PLATFORM: CI builds the probe for all four heads on every push and runs its button on an Android emulator where CI allows; an unbuildable head is reported, never skipped. *BRD:* BRD-95. *Phase 2.*
  - *Acceptance:* When a developer opens the workflow run on the CI run page after a push, then all four probe heads show built, failed or not run.

<a id="d-req-fn-069"></a>
- **REQ-FN-069** — The UsageGuide's Platform notes shall carry the owner's step-by-step runbook for the probe app: how to build and deploy it to a Mac, an Android phone and an iPh. *BRD:* BRD-165. *Phase 2.*
  - *Acceptance:* When the owner opens the Platform notes on the UsageGuide, then numbered build, deploy, run and record steps for the probe exist per device.

## Surface: Local model

<a id="d-req-rag-057"></a>
- **REQ-RAG-057** — F-LOCAL-LLM: `TechieRag.Local` package with `.UseLocalLlm()` via `UseCustomLlmProvider`, platform-appropriate default model, overloads for a model id or folder; nothing in core. *BRD:* BRD-96. *Phase 2.*
  - *Acceptance:* When a developer references `TechieRag.Local` and calls `UseLocalLlm()` in a MAUI app, then a provider with the platform default model answers a prompt.

<a id="d-req-rag-058"></a>
- **REQ-RAG-058** — F-LOCAL-LLM: one public provider over an internal `ILocalLlmRuntime` with one implementation per platform per `DECISIONS.md`; identical behaviour everywhere. *BRD:* BRD-97. *Phase 2.*
  - *Acceptance:* When the conformance suite runs on each platform that has a runtime, then every test passes and the app cannot observe which runtime is in use.

<a id="d-req-rag-059"></a>
- **REQ-RAG-059** — F-LOCAL-LLM: the provider fully implements `ILlmProvider` including typed streaming, applies the chat template, honours every option and cancellation, refuses an over-long prompt. *BRD:* BRD-98. *Phase 2.*
  - *Acceptance:* When the conformance suite exercises every `ILlmProvider` member and option on the local provider, then each is honoured and an over-long prompt throws before inference.

<a id="d-req-rag-060"></a>
- **REQ-RAG-060** — F-LOCAL-LLM: memory check before load; refuses with a clear message instead of letting the OS kill the app. *BRD:* BRD-99. *Phase 2.*
  - *Acceptance:* When a developer loads a model on a device with less free memory than it needs, then load throws a message naming the shortfall and the app keeps running.

<a id="d-req-rag-061"></a>
- **REQ-RAG-061** — F-LOCAL-LLM: one-time resumable download to the app-data root, `TECHIERAG_MODEL_BASE_URL`, SHA-256 verified, size known first, `ModelDownloadService` progress. *BRD:* BRD-100. *Phase 2.*
  - *Acceptance:* When a developer interrupts and restarts a model download in the probe app, then it resumes, verifies SHA-256 and reports size before starting.

<a id="d-req-rag-062"></a>
- **REQ-RAG-062** — F-LOCAL-LLM: download requires explicit acceptance of the model's terms; weights never inside the package or the app bundle. *BRD:* BRD-101. *Phase 2.*
  - *Acceptance:* When a developer starts a model download without signalling terms acceptance, then nothing downloads and the terms URL is exposed to the host.

<a id="d-req-fn-058"></a>
- **REQ-FN-058** — F-LOCAL-LLM: `TechieRag.Local` ships buildTransitive targets with the native runtime libraries for all four platforms; no hand-written csproj changes. *BRD:* BRD-102. *Phase 2.*
  - *Acceptance:* When a developer builds the probe app for all four heads with no native wiring in its csproj, then `TechieRag.Local` loads its runtime on each.

<a id="d-req-rag-063"></a>
- **REQ-RAG-063** — F-LOCAL-LLM: `CompleteAsync<T>` and JSON mode return valid JSON for the schema; `SupportsToolCalling` false until a test proves otherwise. *BRD:* BRD-103. *Phase 2.*
  - *Acceptance:* When a developer calls `CompleteAsync<T>` with the three test schemas on the local provider, then each response parses into T.

<a id="d-req-rag-064"></a>
- **REQ-RAG-064** — F-LOCAL-LLM: `LlmSource.Local`, a `LlmProviderFactory` arm and a `LlmConnectorCatalog` row so `ModelRouter` resolves `local/<model>`; no endpoint, no key. *BRD:* BRD-104. *Phase 2.*
  - *Acceptance:* When a developer configures `LlmSource.Local` with no endpoint or key, then the factory builds the provider and `ModelRouter` resolves `local/<model>`.

<a id="d-req-rag-065"></a>
- **REQ-RAG-065** — F-LOCAL-LLM: `EstimateTokenCount` uses the model's real tokenizer. *BRD:* BRD-105. *Phase 2.*
  - *Acceptance:* When a developer calls `EstimateTokenCount` on the fixed test strings, then the count equals the runtime tokenizer's count.

<a id="d-req-fn-059"></a>
- **REQ-FN-059** — F-LOCAL-LLM: the probe app's second button generates one sentence from the local model and shows time to first token and tokens per second. *BRD:* BRD-106. *Phase 2.*
  - *Acceptance:* When a user presses the second button on the probe app's screen, then one generated sentence, time to first token and tokens per second appear.

<a id="d-req-rag-066"></a>
- **REQ-RAG-066** — F-LOCAL-LLM: live tests gated by `LiveLocalLlmFactAttribute` in a non-parallel collection, skipping with a reason when no model is present. *BRD:* BRD-107. *Phase 2.*
  - *Acceptance:* When the test suite runs on a host with no local model, then every live local test skips with a printed reason and nothing fails.

<a id="d-req-fn-060"></a>
- **REQ-FN-060** — F-LOCAL-LLM: tokens per second, time to first token and peak memory recorded per platform on named devices in the support matrix. *BRD:* BRD-108. *Phase 2.*
  - *Acceptance:* When a reader opens the support matrix, then tokens per second, time to first token and peak memory are recorded per platform with device and date.

<a id="d-req-fn-061"></a>
- **REQ-FN-061** — F-LOCAL-LLM: UsageGuide gains a `TechieRag.Local` section; matrix updated; every 'offline' / 'air-gapped' claim corrected to 'downloads once, then works offline'. *BRD:* BRD-109. *Phase 2.*
  - *Acceptance:* When a reader opens the UsageGuide, then a `TechieRag.Local` section exists and no page claims offline without saying downloads once first.

<a id="d-req-rag-108"></a>
- **REQ-RAG-108** — F-LOCAL-LLM: `LocalModel.FromHuggingFace(repository, folder, version)` runs any ONNX Runtime GenAI model on Hugging Face by name: files listed by its public API, the model card's licence accepted before any download, every file checked against Hugging Face's fingerprint, an unpinned name resolved to one version and recorded; the phone default stays the library's own Qwen conversion on the owner's Hugging Face account. *BRD:* BRD-166. *Phase 2.*
  - *Acceptance:* When a developer calls `UseLocalLlm(LocalModel.FromHuggingFace("Arm/gemma-3-1b-instruct-onnx-genai-int4-emb-int8"))` and the host accepts the shown licence, then the files download, every fingerprint is checked, and the provider answers a prompt.

## Surface: Typed streaming

<a id="d-req-rag-067"></a>
- **REQ-RAG-067** — F-LLM: additive typed streaming method on `ILlmProvider` (text delta, tool call, completed-with-usage); `ChatStreamAsync` unchanged and implemented over it; all six providers. *BRD:* BRD-110. *Phase 2.*
  - *Acceptance:* When a developer streams a tool-using turn through the new method on any of the six providers, then text deltas, a tool-call event and a completed event arrive in order.

<a id="d-req-rag-068"></a>
- **REQ-RAG-068** — F-AGENT: `AgentLoopRunner` streaming run over typed events, tools executed through `ToolRegistry`; `TechieRag.Agents` `IChatClient` adapter streams the same. *BRD:* BRD-111. *Phase 2.*
  - *Acceptance:* When a developer runs `AgentLoopRunner`'s streaming run on a tool-using prompt, then text streams as it arrives and the tool executes through `ToolRegistry`.

## Surface: Subscription sign-in

<a id="d-req-rag-069"></a>
- **REQ-RAG-069** — F-LLM: subscription sign-in providers, one builder method per permitting vendor (first `UseChatGptSubscriptionLlm(signInCallback)`), host drives the browser, library yields an `ILlmProvider`, session persisted via a documented seam. *BRD:* BRD-112. *Phase 2.*
  - *Acceptance:* When a developer calls `UseChatGptSubscriptionLlm(callback)` in a test host, then the callback receives URL and code and a working provider is yielded after authorisation.

<a id="d-req-fn-062"></a>
- **REQ-FN-062** — F-LLM: per-vendor sign-in policy and flow (OpenAI, Anthropic, Google, Groq, xAI, Meta) checked against live documentation and recorded in `DECISIONS.md` before BRD-112 is built. *BRD:* BRD-112, BRD-114. *Phase 2.*
  - *Acceptance:* When a developer calls `UseChatGptSubscriptionLlm(callback)` in a test host, then the callback receives URL and code and a working provider is yielded after authorisation.

<a id="d-req-rag-070"></a>
- **REQ-RAG-070** — F-LLM: `LlmSource.Subscription`, factory arm, catalog rows per vendor carrying stated terms and the date checked; `ModelRouter` resolves a subscription model; a vendor with no permitted flow has no method. *BRD:* BRD-113. *Phase 2.*
  - *Acceptance:* When a developer reads the connector catalog, then each subscription vendor row carries its terms text and check date, and Anthropic's row reads not permitted.

## Surface: Ingestion breadth

<a id="d-req-rag-071"></a>
- **REQ-RAG-071** — The system shall offer pluggable chunking through `IChunker` with recursive, token, markdown/code-aware and sentence strategies (`WithChunking`, `WithCustomChun. *BRD:* BRD-115. *Phase 2.*
  - *Acceptance:* When a developer sets `WithChunking(ChunkingStrategy.Markdown)` and ingests a markdown file, then chunks follow heading boundaries; each strategy has a test.

<a id="d-req-rag-072"></a>
- **REQ-RAG-072** — The system shall extract text from XLSX, PPTX and CSV files through dedicated processors. *BRD:* BRD-116. *Phase 2.*
  - *Acceptance:* When a developer ingests an XLSX, a PPTX and a CSV file, then text from cells, slides and rows is extracted by the dedicated processor.

<a id="d-req-rag-073"></a>
- **REQ-RAG-073** — The system shall ingest audio files through an `AudioTranscriptionProcessor` that transcribes via an OpenAI-compatible speech endpoint; a local Whisper ONNX pat. *BRD:* BRD-117. *Phase 2.*
  - *Acceptance:* When a developer ingests an audio file with a speech endpoint configured, then its transcript is chunked and embedded.

<a id="d-req-rag-074"></a>
- **REQ-RAG-074** — A developer can ingest a single web page by URL: fetch, clean to text, chunk and embed (`WebIngestionExtensions`, `WebPageReader`). *BRD:* BRD-118. *Phase 2.*
  - *Acceptance:* When a developer ingests a URL, then the page's readable text is fetched, cleaned, chunked and embedded as one document.

<a id="d-req-rag-075"></a>
- **REQ-RAG-075** — A developer can crawl a site with depth and maximum-link limits (`SiteCrawler`, `WebCrawlOptions`) and ingest every page reached. *BRD:* BRD-119. *Phase 2.*
  - *Acceptance:* When a developer crawls a site with depth 2 and a link cap, then pages within the limits are ingested and none beyond.

<a id="d-req-rag-076"></a>
- **REQ-RAG-076** — ~~A developer can ingest a YouTube video's transcript by URL (`YouTubeTranscriptReader`)~~. *BRD:* BRD-120. *Phase 2.*
  - *Acceptance:* When a developer looks for YouTube ingestion on the web ingestion API, then no such member exists and the documents say it was removed.

<a id="d-req-rag-077"></a>
- **REQ-RAG-077** — Every outbound web fetch shall pass a connect-time SSRF guard (`HttpWebContentFetcher.CreateGuardedHandler`) that refuses private, loopback and link-local targe. *BRD:* BRD-121. *Phase 2.*
  - *Acceptance:* When a fetch redirects to a private or loopback address, then the guarded handler refuses the connection and the error names the guard.

<a id="d-req-rag-107"></a>
- **REQ-RAG-107** — `YouTubeTranscriptReader`, `YouTubeUrl`, the YouTube entry point in `WebIngestionExtensions` and their tests (including `Web/Live/LiveYouTubeTranscriptTests.cs`. *BRD:* BRD-164. *Phase 2.*
  - *Acceptance:* When a developer searches `src/` and `docs/` for YouTube after the build, then no type, entry point, test or sentence about YouTube ingestion remains.

## Surface: Data connectors

<a id="d-req-rag-078"></a>
- **REQ-RAG-078** — The system shall provide an `IDataConnector` framework with a `ConnectorRunner` that returns per-item results and per-item failure reasons and keeps incremental. *BRD:* BRD-122. *Phase 2.*
  - *Acceptance:* When a developer runs a connector through `ConnectorRunner`, then per-item results and failures return and sync state advances.

<a id="d-req-rag-079"></a>
- **REQ-RAG-079** — A developer can connect a GitHub or GitLab repository and ingest its files with branch and glob filters (`RepositoryConnector`). *BRD:* BRD-123. *Phase 2.*
  - *Acceptance:* When a developer connects a repository with a branch and a glob, then only matching files on that branch are ingested.

<a id="d-req-rag-080"></a>
- **REQ-RAG-080** — A developer can connect a Confluence space and ingest its pages, with scheme, host and port pinned on cursors and links so the token never reaches a host named . *BRD:* BRD-124. *Phase 2.*
  - *Acceptance:* When a developer connects a Confluence space, then its pages are ingested and no request leaves the configured host.

<a id="d-req-rag-081"></a>
- **REQ-RAG-081** — A developer can ingest a mailbox over IMAP (generic, Gmail, Microsoft 365) or a local mbox file with folder, date, sender and subject filters, optional PDF/DOCX. *BRD:* BRD-125. *Phase 2.*
  - *Acceptance:* When a developer connects an IMAP mailbox or an mbox file with filters, then matching messages and allowed attachments are ingested incrementally.

<a id="d-req-rag-082"></a>
- **REQ-RAG-082** — Connector transports shall reuse the SSRF-guarded connect-time handler, cap a run at `MaxTotalBytes` (64 MB), and the IMAP client shall refuse command injection. *BRD:* BRD-126. *Phase 2.*
  - *Acceptance:* When a connector run exceeds `MaxTotalBytes` or an IMAP server sends an oversized literal, then the run stops with a coded error.

<a id="d-req-rag-083"></a>
- **REQ-RAG-083** — `ConnectorRunner` shall hand each fetched document to ingestion as it arrives instead of collecting every document before ingesting, so a run stopped part-way k. *BRD:* BRD-127. *Phase 2.*
  - *Acceptance:* When a connector run is cancelled after some documents were fetched, then those documents are already ingested.

## Surface: MCP tools

<a id="d-req-rag-084"></a>
- **REQ-RAG-084** — The system shall consume Model Context Protocol tool servers in the agent loop through `McpClient` with stdio and HTTP transports and `McpToolHandler`. *BRD:* BRD-128. *Phase 2.*
  - *Acceptance:* When a developer registers an MCP server over stdio or HTTP, then its tools appear in the agent loop and execute through `McpToolHandler`.

<a id="d-req-rag-085"></a>
- **REQ-RAG-085** — The system shall expose an `IMcpServerRegistry` with an in-memory implementation, a `McpTrustPolicy`, and the tool-to-server mapping (`McpToolHandler.ServerName. *BRD:* BRD-129. *Phase 2.*
  - *Acceptance:* When a host asks which server a tool belongs to, then `ServerNameFor` answers and an untrusted server's tools are refused by the trust policy.

## Surface: Workspaces and memory

<a id="d-req-rag-089"></a>
- **REQ-RAG-089** — The system shall provide database-backed conversation memory with threads (`DbConversationMemory`, `IConversationStore`) on SQLite and PostgreSQL. *BRD:* BRD-136. *Phase 2.*
  - *Acceptance:* When a developer configures `WithPersistence(StoreProvider.Sqlite, ...)` and chats in a thread, then messages persist and reload after restart.

<a id="d-req-rag-090"></a>
- **REQ-RAG-090** — `IConversationStore` shall persist every message of a thread (user, assistant, sources as JSON, content parts) per workspace and user. *BRD:* BRD-137. *Phase 2.*
  - *Acceptance:* When a message with sources is stored through `IConversationStore`, then it reloads with role, content parts and sources intact.

<a id="d-req-rag-091"></a>
- **REQ-RAG-091** — The system shall build the model context from persisted history with token-aware trimming that keeps the system message and the most recent turns. *BRD:* BRD-138. *Phase 2.*
  - *Acceptance:* When persisted history exceeds the token budget, then the context keeps the system message and the latest turns.

<a id="d-req-rag-092"></a>
- **REQ-RAG-092** — The system shall provide workspace primitives (`IWorkspaceStore`, `WorkspaceManager`, `Workspace`): isolated documents and settings, document pinning, a per-wor. *BRD:* BRD-139. *Phase 2.*
  - *Acceptance:* When a developer creates two workspaces with different documents and settings, then retrieval in one never returns the other's chunks.

<a id="d-req-rag-093"></a>
- **REQ-RAG-093** — Ingestion shall deduplicate by content hash so a document embedded once is reused across workspaces (`IWorkspaceStore.FindDocumentIdByHashAsync`). *BRD:* BRD-140. *Phase 2.*
  - *Acceptance:* When the same file is added to two workspaces, then it is embedded once and both workspaces reference the same document id.

<a id="d-req-rag-094"></a>
- **REQ-RAG-094** — Context assembly shall signal truncation (`WorkspaceContext.WasTruncated`, the `ContextTruncated` event) and evict retrieved chunks before pinned ones. *BRD:* BRD-141. *Phase 2.*
  - *Acceptance:* When retrieved context exceeds the budget with pinned chunks present, then retrieved chunks are evicted first and `WasTruncated` is true.

<a id="d-req-rag-095"></a>
- **REQ-RAG-095** — Deleting a document shall remove its vectors and its membership from every workspace; `IWorkspaceStore.RemoveDocumentAsync` deletes only the membership row toda. *BRD:* BRD-142. *Phase 2.*
  - *Acceptance:* When a developer deletes a document, then its vectors and every workspace membership are gone.

<a id="d-req-rag-096"></a>
- **REQ-RAG-096** — `PromptTemplateEngine.FormatContext` shall signal truncation the same way `WorkspaceManager` does instead of cutting context silently (TR-RAG-009). *BRD:* BRD-143. *Phase 2.*
  - *Acceptance:* When `PromptTemplateEngine.FormatContext` drops chunks to fit, then the caller is told that truncation happened.

## Surface: Reranking

<a id="d-req-rag-097"></a>
- **REQ-RAG-097** — The system shall provide an `IReranker` stage with a local ONNX cross-encoder (`OnnxCrossEncoderReranker`, bge-reranker-v2-m3, in `TechieRag.Embedded`) and API . *BRD:* BRD-144. *Phase 2.*
  - *Acceptance:* When a developer enables the ONNX cross-encoder or Cohere reranker and searches, then the top results are reordered by reranker score.

<a id="d-req-rag-098"></a>
- **REQ-RAG-098** — A developer can switch reranking per call through `SearchOptions.Rerank` and set the default with `WithRerankEnabledByDefault`; the workspace flag is authoritat. *BRD:* BRD-145. *Phase 2.*
  - *Acceptance:* When a developer passes `SearchOptions.Rerank = false` on a rerank-by-default instance, then that search skips the reranker.

