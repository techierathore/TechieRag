# TechieRag Feedback — surfaced during TechieRagWeb

## Summary (filled by /flow-master on consolidation)
- 0 blockers · 3 major (1 OPEN: TR-RAG-001 streaming RAG sources; 2 FIXED: TR-RAG-005 cold-start deadlock, TR-RAG-006 LmStudio tool calling) · 3 minor (1 OPEN: TR-RAG-002 streamed 0-usage; TR-RAG-003/004 resolved SDK-usage notes) · 0 nice-to-have
- **Open for the TechieRag team: TR-RAG-001** (major — streaming RAG can't return sources / bypasses PromptTemplateEngine); TR-RAG-002 (minor). All others fixed app-side or are SDK-usage notes.
- 2026-07-26: TR-RAG-013 appended (minor, open) — `WorkspaceManager` has no interface / no virtual members, so consuming apps cannot unit-test services composed over it without building a full client.
- 2026-07-27: TR-RAG-014/015/016 appended (all major, all OPEN) — found by running REQ-RAG-016/017/018 against the **real** network and the **real** embedded model for the first time. Every one was invisible to the 135-test hermetic suite, because each fails only in a component those tests replace with a fake. **TR-RAG-015 blocks REQ-RAG-018 and needs a product decision, not a code fix.**
- 2026-07-27: TR-RAG-017..021 appended by REQ-RAG-032 (`IDataConnector` framework). **TR-RAG-017 and TR-RAG-018 are major security defects, both found and FIXED in-cluster** — a connector HTTP transport that re-derived the SSRF guard more weakly than the one this codebase already had and defaulted it off, and a Confluence connector that followed response-supplied URLs with the site's API token attached. Both were invisible to a 476-test suite because neither had any test at all. TR-RAG-019 (no aggregate byte budget) fixed. TR-RAG-020 (runner materialises all fetched documents; no streaming API) and TR-RAG-021 (`IConnectorTransport` is GET-only) left **OPEN** by design — both want the REQ-FN-020 consumer to define the seam.
- 2026-07-27: TR-RAG-022/023 appended by REQ-FN-020 (connector background jobs), the consumer TR-RAG-020 was waiting for. **TR-RAG-022 is major and raises TR-RAG-020's severity**: the collect-then-return shape does not merely cost memory, it *discards every fetched document, the sync state and the collected per-item reasons* when a run is cancelled — and `IngestConnectorAsync` ingests only after the walk, so a run stopped at item 480 of 500 ingests zero. The entry names the seam TR-RAG-020 asked for (decorate `IDataConnector`, not the runner) and the two requirements a streaming API must meet beyond memory. TR-RAG-023 (minor) — failures and deliberate skips share one type distinguishable only by an English string prefix; plus culture-formatted byte sizes and a `private` metadata builder.
- 2026-07-28: TR-RAG-024/025/026 appended by REQ-RAG-019/REQ-RAG-020 (saved repository + Confluence connectors). All three were found by RUNNING the thing, not by the suite. **TR-RAG-024** — the vector store round-trips only `SourcePath`, so a connector document's `SourceUrl` came back empty and every citation pointed nowhere; web ingestion had already hit this and left the workaround in a comment. **TR-RAG-025** — `TechieRag.Embedded` cannot load its ONNX native library in a plain `net10.0` console on macOS, so the default offline embedding provider blocks all ingestion in the scheduler helper host; no test covers it because every suite substitutes a stub provider. **TR-RAG-026** — `IngestTextAsync` has no upsert, so the library's own `IngestConnectorAsync` duplicates a changed item's document on every re-sync; worked around app-side with an item→document table.
- 2026-07-28: TR-RAG-027..036 appended by the security audit of `src/TechieRag/Connectors/Email/**` — the hand-rolled IMAP + MIME stack, which had never been reviewed. **TR-RAG-027 is the serious one and is FIXED: IMAP command injection.** A folder name, sender/subject filter, account name or message identifier carrying a CRLF became a *second IMAP command*, executed with the mailbox's own credentials — proved on the wire, and it defeats the connector's entire read-only promise (`BODY.PEEK` is only read-only for the commands this connector sends). TR-RAG-028/029/030 (all major, all FIXED) are the hostile-server trio: a server-declared literal length honoured without a bound (a 2 GB allocation the server chose), an `ImapMailboxOptions.Timeout` that had no effect at all on asynchronous reads (a 2s timeout still blocked at 12s), and response accumulation with no bound on either line length or untagged-line count. TR-RAG-031..034 (minor, FIXED) — a 39 MB message decoding to 400,000 retained attachments, a culture-formatted IMAP `SINCE` key that would be rejected outright on a non-English machine, mboxrd `>>From ` left unescaped, and an attachment name retaining the NTFS ADS colon. TR-RAG-035 (minor, **OPEN** — needs a product decision) — `MimeParser` silently discards content nested past its depth cap and has nowhere to report it, because `ParsedMailMessage` has no diagnostics field and adding one is a breaking change. TR-RAG-036 (informational) records why the HTTP connectors' SSRF guard must **not** be copied here. Every one of these was invisible to the suite that existed: the pre-existing email tests drove a *cooperative* scripted server, and every defect above is something the server or the configuration does adversarially. **REQ-RAG-049 / BRD-135 remains `Not Started` — this was an audit of pre-existing code, not delivery of the requirement.**
- 2026-07-30: **TR-RAG-038 appended and FIXED, and TR-RAG-024 CLOSED with it** — same root cause from two directions. No ingestion route recorded the source artefact's byte size, and the desktop's default store wrote every document row's `Metadata` column as a literal `{}`, so nothing a route recorded about a document could survive the round trip at all. REQ-UI-021's `Size` column was therefore not merely empty, it was unfillable. Fixed by a new `DocumentMetadataKeys` (the shared allowlist of document-scoped keys), `TechieRagClient` recording `FileSize` on both the file and the text ingestion routes, and all three stores round-tripping the document-scoped set. A third trap sat behind the first two: metadata deserialized into `Dictionary<string, object>` yields `JsonElement` values, which are not `IConvertible` — the size would have been stored correctly and still been unreadable. **Also resolves an ID collision:** the checklist cited "TR-RAG-004" for this defect; TR-RAG-004 is an unrelated Qdrant scroll note and now says so.
- Last consolidated: 2026-07-17 (handoff after REQ-UI-014 TechieDesk rename — no new library issues; rename was a pure app-side refactor. Prior: 2026-07-02 — TR-RAG-005/006 confirmed fixed & runtime-verified)

## Issues

<!-- Append entries as gaps are found. IDs are append-only, never renumbered.
     TrBlazeUI → TR-NNN · TechieRag → TR-RAG-NNN -->

### TR-RAG-001 — Streaming RAG cannot return sources or accept pre-fetched context (forces a second vector search)
- **Severity:** major
- **Repro:**
  ```csharp
  // Streaming yields only string tokens — no sources, no way to pass in results:
  await foreach (var token in rag.AskStreamAsync(question, topK, null, filter)) { ... }
  // To render the "Sources Used" panel the page must ALSO call:
  var results = await rag.SearchAsync(question, topK, filter); // <- second vector search
  ```
- **Expected:** A streaming overload that either (a) exposes the `IReadOnlyList<SearchResult>` it retrieved (e.g. a companion callback / final result object), or (b) accepts a pre-fetched `IReadOnlyList<SearchResult>` so the caller can search once and stream from those results. Then one question = one vector search AND sources can be shown.
- **Actual:** `AskStreamAsync` runs its own internal `SearchAsync` and discards the sources. Showing sources for a streamed answer requires a separate `SearchAsync`, so a single question triggers TWO vector searches. `IPromptTemplate` is also not registered in DI, so the caller cannot reuse the library's configured RAG prompt while streaming from pre-fetched results.
- **Encountered in:** REQ-UI-005
- **Workaround:** In the Chat sample's Auto-RAG streaming path, search once with `SearchAsync` (for the sources panel), build a minimal RAG prompt inline from those `SearchResult`s, and stream via `ILlmProvider.ChatStreamAsync`. This keeps retrieval to a single search but diverges from the configured `PromptTemplateEngine` (system prompt / context-chunk template / max-chunks from `PromptConfig` are not applied).
- **Suggested fix:** Add `IAsyncEnumerable<string> AskStreamAsync(..., out/callback sources)` or an overload taking pre-retrieved `IReadOnlyList<SearchResult>`; and/or register `IPromptTemplate` in DI so callers can reuse the configured prompt engine.

### TR-RAG-002 — Streaming completions report zero token usage on some providers
- **Severity:** minor
- **Repro:**
  ```csharp
  // OpenAICompatibleLlmProvider.ChatStreamAsync ends with:
  RaiseCompletionEvent(0, 0, sw.Elapsed, true, false); // input=0, output=0
  ```
- **Expected:** After a streamed completion, `OnCompletionCompleted` / the `ITokenTracker` session usage should reflect the real (or estimated) input/output tokens, consistent with the non-streaming path.
- **Actual:** `OpenAICompatibleLlmProvider` (and any provider not parsing a usage chunk) raises a `0/0` completion event, so `ITokenTracker.GetSessionUsage()` gains an operation but no tokens/cost. `AnthropicLlmProvider` does capture real streamed usage, so behaviour is provider-dependent. A chat footer reading the tracker stays at "0 tokens / $0.0000" for those providers.
- **Encountered in:** REQ-UI-005
- **Workaround:** In the Chat sample, read the `ITokenTracker` session-usage delta after streaming; if it is zero, fall back to `ILlmProvider.EstimateTokenCount(...)` on the prompt + streamed answer and `RecordUsage(...)` the estimate so both the chat footer and the Token Usage dashboard move off zero.
- **Suggested fix:** Have streaming providers estimate output tokens (via `EstimateTokenCount`) and input tokens from the request when the API does not return a usage chunk, so streamed usage is never silently zero.

### TR-RAG-003 — Qdrant.Client CollectionInfo exposes no total vectors count (only IndexedVectorsCount)
- **Severity:** minor
- **Note type:** SDK-version note (Qdrant.Client 1.16.1), not a TechieRag-library gap — logged here per feedback-routing policy.
- **Repro:**
  ```csharp
  var info = await client.GetCollectionInfoAsync(name); // Qdrant.Client.Grpc.CollectionInfo
  // Available: info.PointsCount, info.IndexedVectorsCount, info.SegmentsCount
  // NOT available: info.VectorsCount  -> reflection shows VectorsCount lives on the per-point
  //                Vector / VectorOutput messages (named-vector count), never on CollectionInfo.
  ```
- **Expected:** A collection-level total vectors count distinct from `PointsCount`, so an admin grid can show a truthful "Vectors" column separate from "Points".
- **Actual:** `CollectionInfo` has no `VectorsCount`. The only distinct vectors figure is `IndexedVectorsCount` (HNSW-indexed vectors), which equals `PointsCount` once indexing settles but differs while indexing is in progress.
- **Encountered in:** REQ-UI-012 (BRD-72), QdrantAdminService.ListCollectionsAsync.
- **Workaround:** Bind the admin grid's "Vectors" column to `IndexedVectorsCount` (was previously duplicating `PointsCount`). This is meaningfully distinct from "Points" and needs no SDK change.
- **Suggested fix:** None required for TechieRag. If a true total is ever needed, count via `ScrollAsync`/`CountAsync`; otherwise treat `IndexedVectorsCount` as the "Vectors" metric.

### TR-RAG-004 — Qdrant scroll pagination is cursor-based (NextPageOffset), numeric offset silently breaks past page 1
- **Severity:** minor
- **Note type:** SDK-usage note (Qdrant.Client 1.16.1) — correct-usage reminder, not a library defect.
- ⚠ **ID collision, resolved 2026-07-30.** `docs/TechieDesk-Checklist.md` cites "TR-RAG-004" on REQ-UI-021 and REQ-FN-012 for a **completely different** gap — the document library's empty `Size` column. That gap was never logged in this file. It is now **TR-RAG-038** (✅ fixed 2026-07-30), and its root cause is shared with **TR-RAG-024**. This entry is, and always was, only about Qdrant scroll pagination.
- **Repro:**
  ```csharp
  // WRONG: numeric cursor — returns wrong/empty pages for UUID or non-contiguous ids beyond page 1
  var page = await client.ScrollAsync(name, limit: 20, offset: new PointId { Num = (ulong)offset });
  // RIGHT: thread back the opaque cursor from the previous response
  var resp = await client.ScrollAsync(name, limit: 20, offset: previousResp.NextPageOffset);
  // resp.NextPageOffset is a PointId (possibly a UUID); it is null on the final page.
  ```
- **Expected:** Forward pagination by feeding `ScrollResponse.NextPageOffset` back as the next `offset`.
- **Actual:** The sample encoded pagination as `new PointId { Num = (ulong)offset }`, which only works for dense contiguous numeric ids on page 1.
- **Encountered in:** REQ-UI-013 (BRD-73), QdrantAdminService.BrowseVectorsAsync + QdrantAdmin.razor.
- **Workaround / fix applied:** `BrowseVectorsAsync` now takes an opaque `cursor` (serialized `PointId`) and returns `VectorPage.NextPageOffset`; the razor page keeps a cursor history for forward-only "Previous". No SDK change needed.
- **Suggested fix:** None — this is standard Qdrant scroll semantics.

### TR-RAG-005 — `GetLlmProvider()` / `GetTokenTracker()` sync-over-async DEADLOCKS on a cold instance and poisons the whole app
- **Severity:** major
- **Note type:** sample-side (`TechieRagManager`) integration bug; surfaced only when an LLM/Token page is the FIRST thing to build the RAG instance. Found live 2026-07-01 by the verifier against LM Studio.
- **Repro:**
  ```csharp
  // TechieRagManager.cs:366-368 (and GetTokenTracker :372-374)
  public ILlmProvider? GetLlmProvider()
      => GetInstanceAsync().GetAwaiter().GetResult().GetLlmProvider();  // sync-blocks the Blazor circuit thread
  // GetInstanceAsync() -> CreateInstanceFromConfigAsync() -> await File.ReadAllTextAsync(...)
  ```
  Boot the app; WITHOUT visiting any async-build page first, open LLM Playground / RAG Chat (Direct LLM) / Tool Demo / Token Usage and trigger the provider. `.GetAwaiter().GetResult()` blocks the circuit's sync-context thread while `GetInstanceAsync` awaits `File.ReadAllTextAsync` → **classic sync-over-async deadlock**. The click handler never proceeds: no `TechieRagManager` "Loaded configuration" log, no outbound LLM call, the button never enters its "Generating…"/"Running…" state, and no response ever renders.
- **Worse — it poisons the process:** the deadlocked call is stuck *inside* `_lock` (SemaphoreSlim) in `GetInstanceAsync`, so it never releases the lock. **Every subsequent RAG operation app-wide then hangs too** (even an Ingestion page's async `InitializeAsync` blocks on the same `_lock`). Observed: after one cold Playground hang, a fresh warm-up navigation to `/ingestion` also hung indefinitely; only an app restart recovered.
- **Expected:** provider/tracker accessors must not sync-block on instance construction. Cold-start of an LLM page should build the instance and answer (verified: a WARM instance completes in ~1s — `Input: 43 | Output: 3`).
- **Encountered in:** REQ-UI-005, REQ-UI-006, REQ-UI-007, REQ-UI-008, REQ-UI-009 (all consume `GetLlmProvider()`/`GetTokenTracker()`).
- **Workaround (used by the verifier):** visit an async-build page (`/ingestion`) once after boot to build+cache the singleton instance BEFORE opening any LLM/Token page; then `GetLlmProvider()` hits the `if (_currentInstance != null) return` fast path and never awaits. (REQ-UI-009 passed on 2026-07-01 presumably because a prior page had already warmed the instance.)
- **Suggested fix:** make the accessors async (`Task<ILlmProvider?> GetLlmProviderAsync()`) and await them from the pages; or eagerly build the instance once at startup (hosted service / `InitializeAsync` on first request) so no page ever triggers a cold sync build; or at minimum do the instance build off the circuit thread. Never `.GetAwaiter().GetResult()` on the Blazor synchronization context.
- ✅ FIXED 2026-07-01 — Sample-side fix in `apps/TechieDesk/`, core library untouched. Added async accessors to `TechieRagManager` (`GetLlmProviderAsync`/`GetTokenTrackerAsync`/`GetConversationMemoryAsync`), each awaiting `GetInstanceAsync()` — no more sync-over-async. The three `.GetAwaiter().GetResult()` accessors were removed; the `ITechieRag` contract members (which the core interface still mandates and cannot be dropped) were reimplemented as **explicit interface implementations that never build synchronously** — they read the cached `_currentInstance` only (`GetTokenTracker` throws `InvalidOperationException` when still cold rather than deadlocking), so the poisoned-lock path is gone and consumers on a `TechieRagManager` reference are steered to the async accessors. Updated every call site (Chat `HandleDirectLlm`/`HandleAutoRag`/`AccountStreamedUsage` now `async Task`, LlmPlayground `GetProviderAsync`, ToolDemo `RunAgentLoop`, TokenUsage `OnInitializedAsync`, LlmSettings `TestLlmConnectionAsync`); the four pages that used these accessors now `@inject TechieRagManager` (same DI singleton) instead of `ITechieRag`. No new `.GetAwaiter().GetResult()`/`.Result`/`.Wait()` introduced. Build: `apps/TechieDesk` 0 errors. Commit `[REQ-UI-007] ... (TR-RAG-005)`.

### TR-RAG-006 — Agent loop performs NO tool call with the LM Studio / OpenAI-compatible provider (execution trace only ever shows the hardcoded fallback)
- **Severity:** major
- **Repro:** Tool Demo, live LM Studio (`qwen2.5-coder-32b-instruct`), prompt "What is the current weather in Tokyo? You must call the get_weather tool…". `AgentLoopRunner.RunAsync(messages, progress)` returns a plain text answer, the model **hallucinates** the weather, and `IProgress<AgentStep>` fires **zero** times → the page falls through to its `executionSteps.Count == 0` fallback (`ToolDemo.razor:283`) and shows only "Step 1: LLM generated final answer". No `ToolCallRequested`/`ToolExecuted` step is ever reported.
- **Proof it is not the model/endpoint:** a raw call to the same endpoint WITH the tool definition returns a structured tool call —
  ```
  finish_reason: "tool_calls"; tool_calls:[{function:{name:"get_weather",arguments:"{\"city\":\"Tokyo\"}"}}]
  ```
  So LM Studio + qwen DO tool-call when tools are sent; the library path does not elicit one.
- **Expected:** `AgentLoopRunner` (via the LmStudio/OpenAICompatible provider) sends the `ToolRegistry`'s tool definitions on each `ChatAsync`, parses returned `tool_calls`, executes the tool, and reports each as an `AgentStep` — so the Execution Trace shows the real tool request/execution steps (REQ-UI-007 / REQ-RAG-009 acceptance).
- **Actual:** the loop degenerates to a single completion with no tools in play; the AgentStep-progress feature (added for REQ-UI-007) therefore never emits a step at runtime with this provider.
- **Encountered in:** REQ-UI-007 (BRD-67), REQ-RAG-009 (BRD-40…43). Likely root cause in the LmStudio/OpenAICompatible provider's tool serialization or `tool_calls` parsing within the agent-loop `ChatAsync` path (needs library-side confirmation).
- **Workaround:** none from the sample — the tool round-trip must work in the provider/runner. (Direct completion, streaming, and typed `CompleteAsync<T>` all work against the same provider — see REQ-UI-005/006 verified — so the gap is specific to tool calling in the agent loop.)
- **Suggested fix:** verify the agent-loop `ChatAsync` request includes the tool schema for LmStudio/OpenAICompatible and that the response's `tool_calls` are parsed and surfaced as `AgentStep`s; add an integration test that asserts ≥1 `ToolExecuted` step for a weather/time prompt.
- ✅ FIXED 2026-07-01 — Root cause was in `LmStudioLlmProvider` only (the OpenAICompatible sibling was already correct). Fixed in `src/TechieRag/Llm/LmStudioLlmProvider.cs`: `SupportsToolCalling` now `true`; `BuildOpenAIRequest` now emits the `tools`/`tool_choice` blocks and a full message projection (`content`/`tool_call_id`/`tool_calls`) so follow-up agent turns are well-formed; `ChatAsync` now parses `tool_calls` via a new `ParseToolCalls` helper and passes `HasToolCalls` to the completion event. Unit tests added (`tests/TechieRag.Tests/Llm/LmStudioLlmProviderTests.cs`) asserting the serialized request carries a `tools` array and that a `finish_reason:"tool_calls"` response is parsed into `LlmResponse.ToolCalls` (`HasToolCalls == true`). Build + tests green. Commit `[REQ-RAG-009] ... (TR-RAG-006)`.

### TR-RAG-009 — Non-workspace context path still truncates silently (minor, found 2026-07-25)

- **Severity:** minor
- **Repro:** call `ITechieRag.AskAsync` / `AskStreamWithSourcesAsync` directly (not via `WorkspaceManager`) with more than `PromptConfig.MaxContextChunks` search results.
- **Expected:** after REQ-RAG-048, callers can detect that context was truncated.
- **Actual:** REQ-RAG-048 fixed the **workspace** seam only. `PromptTemplateEngine.FormatContext` still does `searchResults.Take(config.MaxContextChunks)` with no flag, event, or log, so non-workspace consumers remain blind to truncation.
- **Encountered in:** REQ-RAG-048 (scoped to the merged pinned+retrieved workspace context, so this is out of that REQ's acceptance).
- **Suggested fix:** apply the same `WorkspaceContext`-style diagnostics (or at minimum the warning log) at the `PromptTemplateEngine` seam.

### TR-RAG-010 — Pinned-chunk retrieval issues one vector search per pinned document (minor, found 2026-07-25)

- **Severity:** minor (scales badly, correct today)
- **Actual:** `WorkspaceManager.CollectPinnedChunksAsync` runs one vector search **per pinned document** — observed live: 6 pinned documents produced 7 searches for a single question.
- **Impact:** fine at small scale; O(n) round-trips at 50+ pinned documents, against a remote vector store this is the dominant latency.
- **Suggested fix:** a single filtered query over the pinned document-id set.

### TR-RAG-011 — Pinned chunks bypass reranking (nice-to-have, found 2026-07-25)

- **Severity:** nice-to-have (needs a product decision)
- **Actual:** with the new per-workspace rerank switch (REQ-RAG-047) enabled, retrieved chunks are reranked but **pinned** chunks are not — they are merged ahead of the reranked results by position.
- **Rationale for current behaviour:** kept deliberately stable/low-risk in REQ-RAG-047 so pinning semantics did not change.
- **Suggested fix:** decide explicitly whether reranking should order *within* pinned documents, and document whichever way it lands.

### TR-RAG-012 — `RerankConfig.Enabled` has changed meaning (informational, 2026-07-25)

- **Severity:** informational — **needs a release note before the next NuGet publish**
- **Change (REQ-RAG-047):** `Enabled` previously meant "build and use a reranker". It now means "rerank by **default** for calls that do not specify". The reranker is constructed whenever a usable source + credentials exist, because a workspace must be able to force rerank ON while the instance default is OFF — impossible if no `IReranker` was ever instantiated.
- **Consumer impact:** anyone relying on `Enabled: false` to prevent reranker *construction* (e.g. to avoid an ONNX model download or an API key check) will see different behaviour.

### TR-RAG-013 — `WorkspaceManager` is a sealed-in-practice concrete class with no interface, so app services that compose it cannot be unit-tested in isolation (minor, found 2026-07-26)

- **Severity:** minor
- **Repro:**
  ```csharp
  // TechieDesk's WorkspaceService is a thin facade over the library manager:
  var manager = await rag.GetWorkspaceManagerAsync();
  var all = await manager.ListWorkspacesAsync(ct);
  ```
  There is no `IWorkspaceManager`, and `ListWorkspacesAsync`/`CreateWorkspaceAsync`/`GetWorkspaceAsync` are non-virtual, so a consumer cannot substitute a double for the manager.
- **Expected:** an `IWorkspaceManager` (or virtual members) so a consuming app can test its own workspace logic — visibility, slug resolution, bootstrap — without standing up a store.
- **Actual:** the only way to exercise a consumer of `WorkspaceManager` is to build a real one, which means going through `TechieRagBuilder.Build()` and therefore constructing an embedding provider and a vector store as well, neither of which workspace listing touches.
- **Encountered in:** REQ-FN-041 (TechieDesk) — writing the regression test that proves every workspace is listed for the local owner after the assignment-scoping code was deleted.
- **Workaround:** build a real `WorkspaceManager` via `TechieRagBuilder` with `UseCustomEmbeddingProvider(() => stub)` + `SqliteVec` + `WithPersistence(StoreProvider.Sqlite, temp)`. This works and is arguably better coverage (a real SQLite store), but it means every such test pays for a vector-store construction it does not use, and it only works because a stub embedding provider can be injected. `TechieDesk.Core`'s own `TechieRagManager.GetWorkspaceManagerAsync()` had to be made `virtual` app-side to let the manager be substituted at all.
- **Suggested fix:** extract an `IWorkspaceManager` interface covering the CRUD surface (`List`/`Get`/`Create`/`Update`/`Delete`), or mark those members virtual. A separate, smaller win: let `TechieRagBuilder.Build()` skip vector-store construction when only persistence is requested.

### TR-RAG-014 — `SqliteVecStore` discards document-level metadata, so `ListDocumentsAsync` always returns `Metadata = {}`
- **Severity:** major
- **Repro:**
  ```csharp
  var rag = new TechieRagBuilder()
      .UseEmbedded()
      .UseVectorStore(VectorStoreType.SqliteVec, "Data Source=vectors.db")
      .Build();
  await rag.InitializeAsync();

  await rag.IngestTextAsync(
      "some prose",
      "Example",
      new Dictionary<string, object> { ["SourceUrl"] = "https://example.com/page" });

  var document = (await rag.ListDocumentsAsync()).Single();
  Console.WriteLine(document.Metadata.Count); // 0
  ```
- **Expected:** the metadata handed to `IngestTextAsync` is readable back from `ListDocumentsAsync`. It is the only documented way to record where a document came from, and `Document.Metadata` exists specifically to carry it.
- **Actual:** `Metadata` is always an empty dictionary. `src/TechieRag/VectorStores/SqliteVecStore.cs` writes the `Documents` row with a hardcoded `Metadata = "{}"` (lines 169 and 246); the caller's metadata is persisted only onto the `Chunks` rows. Nothing warns, and nothing fails — the ingestion reports success and the documents are fully searchable. Only the metadata is silently gone.
- **Encountered in:** REQ-RAG-016 / REQ-RAG-017 (TechieDesk "Add from web"). Every ingested web document came back with an empty `SourceUrl`, so the results list rendered a blank source column for every row while reporting "Ingested 3 documents". This is exactly the render-empty failure the smoke policy exists to catch, and it survived a 135-test suite because every one of those tests asserts the service's return value against a scripted store.
- **Workaround:** web ingestion now writes the URL to **both** `SourceUrl` and `SourcePath` in the ingestion metadata (`src/TechieRag/Web/WebIngestionExtensions.cs`), because `SourcePath` *is* lifted from chunk metadata onto the document row. A new `Document.WebSourceUrl()` extension reads whichever survived, and `WebIngestionService.ReadCatalogueAsync` calls it instead of indexing `Metadata` directly. Regression tests: `IngestedDocumentsReportTheSourceUrlAfterAStoreRoundTrip` and `EachCrawledDocumentReportsItsOwnSourceUrl` in `tests/TechieDesk.Tests/Web/WebIngestionServiceTests.cs`, which run through the real SQLite-vec store.
- **Suggested fix:** serialize the document's metadata into the `Documents.Metadata` column instead of `"{}"`. The column, the JSON round-trip in `DocumentRow.ToDocument()`, and the deserialization are all already there and correct — only the write is stubbed. Worth checking `PgVectorStore` and `QdrantStore` for the same stub, and worth a store-contract test asserting metadata round-trips, since a consumer cannot tell from the API that the guarantee does not hold.

### TR-RAG-015 — YouTube transcript ingestion is non-functional: timed-text URLs return HTTP 200 with a zero-byte body
- **Severity:** major (external platform change; the library cannot currently deliver REQ-RAG-018)
- **Repro:**
  ```csharp
  var reader = new YouTubeTranscriptReader(new HttpClient());
  await reader.ReadAsync("https://www.youtube.com/watch?v=aircAruvnKk");
  ```
  Reproduced independently of TechieRag with plain `curl`/`urllib` on 2026-07-27, from a residential connection, against four unrelated videos (`dQw4w9WgXcQ`, `jNQXAC9IVRw`, `aircAruvnKk`, `8S0FDjFBj8o`) and every output format (`fmt` absent, `json3`, `srv1`, `srv3`, `ttml`, `vtt`), with both a bot user-agent and a full browser user-agent plus `CONSENT` cookie.
- **Expected:** the timed-text URL named in the watch page returns the caption track.
- **Actual:** **HTTP 200, `Content-Length: 0`, empty body — every video, every format, every client shape.** Caption *discovery* still works: the watch page still contains `"captionTracks"` with valid-looking signed `baseUrl` values (31 tracks for `aircAruvnKk`). It is only the download that is dead. The InnerTube fallbacks are closed too: `/youtubei/v1/player` returns `UNPLAYABLE` for `WEB`/`MWEB`, `LOGIN_REQUIRED` for `ANDROID_VR`, and HTTP 400 for `ANDROID`/`IOS`. This is consistent with YouTube requiring a proof-of-origin token for caption downloads.
- **Encountered in:** REQ-RAG-018. Live canary `TranscriptIsReadFromARealVideoWithCaptions` in `tests/TechieRag.Tests/Web/Live/LiveYouTubeTranscriptTests.cs` — **currently and knowingly RED.**
- **Workaround:** none possible for the transcript itself. What was fixed is the *reporting*, so the failure is honest and cheap to diagnose:
  - The empty-body case previously said *"The caption track for this video was empty."*, which blames the video and sends the operator to try a different one that will fail identically. It now names the restriction and states that nothing was ingested.
  - Discovery and download are now separately observable: the live suite asserts the watch page still exposes `captionTracks` independently of the reader, so when this file is next read it is immediately clear which half is broken.
  - The end-to-end path is asserted to add **nothing** rather than an empty document (`RealVideoIngestionEitherYieldsATranscriptOrAddsNothingAtAll`), so a blank, unretrievable "video" document never reaches the library.
- **Suggested fix:** decide the product answer, because the technical one is not in the library's gift. Either (a) integrate the YouTube Data API v3 `captions` endpoint, which needs an API key and OAuth for most videos; (b) accept a caller-supplied cookie/PO-token so an authenticated operator can still ingest; or (c) surface the restriction in the UI and stop offering video ingestion as if it worked. The live canary should be kept red rather than deleted — it is the only instrument that will report the day this starts working again.

### TR-RAG-016 — `TechieRag.Embedded.UseEmbedded()` throws `DllNotFoundException` on macOS/Linux for any plain `net10.0` consumer
- **Severity:** major
- **Repro:** from a `net10.0` (non-MAUI) project on macOS or Linux:
  ```csharp
  var rag = new TechieRagBuilder().UseEmbedded().UseSqliteVec().Build();
  await rag.IngestTextAsync("hello", "Doc");
  // System.TypeInitializationException -> DllNotFoundException:
  //   Unable to load shared library 'onnxruntime.dll'
  ```
- **Expected:** `UseEmbedded()` works on every platform the package restores native assets for. The correct file is present in the output at `runtimes/osx-arm64/native/libonnxruntime.dylib`.
- **Actual:** `Microsoft.ML.OnnxRuntime.Managed` 1.24.1's `net8.0` assembly — the one a plain `net10.0` project resolves — hardcodes its `DllImport` name as the literal string `"onnxruntime.dll"`. On Unix .NET probes `onnxruntime.dll`, `libonnxruntime.dll`, `onnxruntime.dll.dylib` and `libonnxruntime.dll.dylib`, none of which matches `libonnxruntime.dylib`. The MAUI heads are unaffected because `net10.0-maccatalyst` resolves the `net9.0-maccatalyst18.0` managed assembly instead. So the failure is invisible from the shipping app and total for tests, console tools, and any server-side consumer on macOS/Linux.
- **Encountered in:** REQ-RAG-016/017/018 end-to-end verification. `tests/TechieDesk.Tests` targets plain `net10.0`, so nothing could exercise the real embedding model until this was worked around — which is why the "does ingested web content actually come back out of search" question had never been answered.
- **Workaround:** `tests/TechieDesk.Tests/Support/OnnxRuntimeNativeLibraryResolver.cs` — a `[ModuleInitializer]` that installs a `NativeLibrary.SetDllImportResolver` mapping `"onnxruntime.dll"` to the correct per-platform file under `runtimes/{rid}/native/`. It only ever answers a name the runtime has already failed to resolve, so it cannot mask a different problem. With it in place the live end-to-end suite runs the real BGE-M3 model and passes.
- **Suggested fix:** ship the same resolver inside `TechieRag.Embedded` rather than leaving it to every consumer to discover — a static constructor on `EmbeddedEmbeddingProvider` registering a `DllImportResolver` for the ONNX Runtime assembly would fix it once for everyone, and would also make the package honest about supporting non-Windows hosts. Separately, `EmbeddedEmbeddingProvider.GetModelDirectory()` resolves the 2.3 GB model cache relative to the *assembly* directory, so it is re-downloaded per output folder and wiped by a clean; a per-machine cache (e.g. under the user profile) with the assembly directory as a fallback would be a meaningful improvement for anyone running more than one consumer.

### TR-RAG-017 — `HttpConnectorTransport` shipped a weaker SSRF guard than the one this codebase had already learned to build, and defaulted it off
- **Severity:** major (security; found and fixed within REQ-RAG-032)
- **Repro:**
  ```csharp
  using var client = HttpConnectorTransport.CreateDefaultClient(); // plain HttpClientHandler
  var transport = new HttpConnectorTransport(client);              // blockPrivateTargets defaulted to FALSE
  await transport.GetAsync(new ConnectorHttpRequest("http://127.0.0.1.nip.io/admin"));
  // request lands on loopback, with the connector's Authorization header attached
  ```
- **Expected:** the connector transport enforces the same connect-time guard `HttpWebContentFetcher.CreateGuardedHandler` already provides — decide on the RESOLVED address, then connect only to the address that was checked.
- **Actual:** three independent weaknesses in one class. (1) The constructor's `blockPrivateTargets` defaulted to **false**, with a comment arguing that an operator-typed base URL is not attacker-influenced. (2) When it *was* enabled, the only check was `WebCrawlOptions.IsPrivateNetworkHost(uri.Host)` — the textual check whose docstring in this very repo says "It is not the enforcement point, and must not be treated as one", and which `127.0.0.1.nip.io` walks straight through. (3) `CreateDefaultClient()` built a bare `HttpClientHandler`, so no `ConnectCallback` ran at all and there was no connect-time guard to fall back on. There were no tests for any of it — `tests/TechieRag.Tests/Connectors/` had no transport test file.
- **Why it matters more here than for the crawler:** this transport attaches an `Authorization` header to every request. A URL steered at an internal endpoint does not merely read it, it presents the source's credential to it. The crawler's equivalent bug leaks a response; this one leaks a token.
- **Encountered in:** REQ-RAG-032 / BRD-113 (`IDataConnector` framework). The reasoning in the original comment is the interesting part: it was not an oversight but a deliberate argument that this component's threat model differed. It did not.
- **Workaround / fix applied:** `CreateDefaultClient(bool blockPrivateTargets = true)` now builds `HttpWebContentFetcher.CreateGuardedHandler(...)`; the constructor default is `true`; the textual check is retained only as a fast path and documented as such; connect-time refusals are unwrapped so the operator sees the accurate reason instead of "could not be reached"; and the final URL is re-checked after redirects for callers who supply their own unguarded `HttpClient`. Regression tests: `tests/TechieRag.Tests/Connectors/HttpConnectorTransportTests.cs` (17 hermetic, incl. a real loopback listener asserting the request never arrives) plus a live proof against `127.0.0.1.nip.io` in `Connectors/Live/LiveRepositoryConnectorTests.cs`. Mutation-proved: reverting the handler, the default, or the redirect re-check each turns a named test RED.
- **Suggested fix (library-wide):** the guard should not be something each new outbound component re-derives. Anything in this library that opens a socket to a caller-supplied address should be required to obtain its `HttpClient` from one factory that has the guard built in, so "did this component remember?" stops being a per-component question. A second component reimplementing a security control weakly, six months after the first one learned to do it properly, is a structural signal rather than a one-off defect.

### TR-RAG-018 — `ConfluenceConnector` followed URLs supplied by the response body, with the site's API token attached
- **Severity:** major (security; credential exfiltration; found and fixed within REQ-RAG-032)
- **Repro:** a Confluence site (or anything able to influence its responses) answers a listing with:
  ```json
  { "results": [], "_links": { "base": "https://attacker.example/", "next": "https://attacker.example/collect" } }
  ```
  The connector used `_links.next` verbatim as the next request URL, and `BuildHeaders()` attaches `Authorization: Basic base64(email:apiToken)` to every request.
- **Expected:** a URL derived from a response is still a URL, and must be constrained to the site the operator configured before a credential is sent to it.
- **Actual:** `ReadNextLink` returned any absolute `_links.next` unchanged, and `ReadLinkBase` accepted any `_links.base` and used it to resolve relative links and to build the citation URLs shown to users. So a hostile or compromised response could (a) receive the caller's Atlassian API token, (b) redirect the entire walk, and (c) set every citation link in the RAG answers to an address of its choosing. `_links` was followed precisely *because* Cloud sites answer on a `/wiki` prefix the caller may not have configured — a real requirement that made the input load-bearing.
- **Encountered in:** REQ-RAG-020 / BRD-64. This one is worth noting as a class: the SSRF review that produced TR-RAG-017 was aimed at the *base URL* as the attacker-influenced input. The response body being an equally good injection point for a "next URL" was a second, separate hole in a different file, and the transport-level fix does not close it — an attacker-named URL on a public host is not a private-network target.
- **Workaround / fix applied:** `RequireSameOrigin` (scheme + host + port) now gates the paging cursor at the point of request, the `next` link at the point of production, and the stated link base; a foreign absolute page link is discarded in favour of a constructed one. Refusal messages name the offending URL and never the token. Regression tests in `ConfluenceConnectorTests`: `RefusesAPagingLinkThatLeavesTheConfiguredSite`, `RefusesAForeignCursorSuppliedByTheCaller`, `RefusalNamesTheUrlAndNeverTheToken`, `IgnoresALinkBaseThatLeavesTheConfiguredSite`, `IgnoresAPageLinkThatLeavesTheConfiguredSite`, plus `StillFollowsAnAbsoluteSameSitePagingLink` so the guard does not break paging. All mutation-proved RED.
- **Suggested fix:** any future connector that follows a link stated by its source (a `Link:` header, a HAL `_links`, a GraphQL cursor URL) needs the same origin pin. Worth making it a shared helper on the connector framework rather than per-connector, and worth stating in `IDataConnector`'s remarks that response-supplied URLs are untrusted input.

### TR-RAG-019 — a connector run had no aggregate size budget, only a per-item one
- **Severity:** minor (found and fixed within REQ-RAG-032)
- **Repro:** run any connector over a documentation repository of ~500 files that are each 100 KB. Every file passes `MaxItemBytes` (2 MB) and the count is within `MaxItems` (500), so the run completes holding ~50 MB of text in `ConnectorRunResult.Documents`. At the defaults the theoretical worst case was `MaxItems × MaxItemBytes` = **1 GB**, and `ConnectorRunner`'s own docstring cited that product as the memory bound.
- **Expected:** the bound a caller reasons about should be the aggregate, since that is what occupies memory.
- **Actual:** no aggregate cap existed. A per-item cap stops one bad file and can never stop the sum of good ones, which is the shape every real source has.
- **Encountered in:** REQ-RAG-032 / BRD-113, design requirement "caps on item count and total bytes".
- **Workaround / fix applied:** `ConnectorRunOptions.MaxTotalBytes` (default 64 MB), enforced in `ConnectorRunner` on real UTF-8 byte counts of fetched text. The item that crosses the budget is kept and its version recorded — discarding it would re-fetch it forever and a source larger than the budget could never converge. Tests: `StopsAtMaxTotalBytes`, `KeepsTheItemThatReachedTheByteBudget`, `CountsMultiByteTextByItsBytes`, `DoesNotReportALimitWhenTheBudgetWasNotReached`.

### TR-RAG-020 — `ConnectorRunner` materialises every fetched document in memory; there is no streaming run API — **OPEN**
- **Severity:** minor (design limit, bounded but not removed)
- **Repro:**
  ```csharp
  var result = await new ConnectorRunner().RunAsync(connector);
  // result.Documents holds the full text of every item fetched, all at once
  ```
- **Expected:** for the "large repository must not be materialised in memory" requirement to hold end to end, a caller should be able to consume documents as they are fetched.
- **Actual:** *listing* is genuinely incremental — paged by cursor, filtered before fetch, oversized items skipped from the listing without downloading. *Fetching* is not: `RunAsync` collects `List<ConnectorDocument>` and returns it whole. The reason is defensible and documented — the failures and the sync state come out of the same walk, and handing them back through a side channel is how per-item failures get ignored — but it does mean peak memory is the whole run, now capped by `MaxTotalBytes` (TR-RAG-019) rather than by the design.
- **Encountered in:** REQ-RAG-032 / BRD-113. Called out here rather than fixed because the concurrent REQ-FN-020 background-job cluster is the consumer that would define what a streaming shape needs to yield, and guessing at it now would likely produce the wrong seam.
- **Workaround:** set `MaxTotalBytes` to what the host can afford; `ConnectorRunResult.ReachedLimit` plus the returned `Sync` make a truncated run resumable, so a source larger than the budget converges over several runs rather than failing.
- **Suggested fix:** an `IAsyncEnumerable<ConnectorRunEvent>` overload yielding a discriminated union of fetched-document / item-failure / progress, with the sync state available at completion. That shape keeps failures un-ignorable (they are in the same stream) while removing the aggregate memory cost, and it is also exactly what a progress-reporting background job wants to consume.

### TR-RAG-021 — `IConnectorTransport` is GET-only, which forecloses the search APIs these sources offer — **OPEN**
- **Severity:** minor
- **Repro:** `IConnectorTransport` exposes a single `GetAsync(ConnectorHttpRequest, CancellationToken)`. There is no POST, no request body, and no method selector.
- **Expected:** enough of an HTTP seam to reach the endpoints a connector may need.
- **Actual:** GET only. This is sufficient for everything shipped today (repository trees and blobs, Confluence content listings are all GET), so it is not currently blocking. It does foreclose: Confluence CQL search via POST, GitHub's GraphQL API — which is the documented way to avoid the recursive-tree truncation `RepositoryConnector` currently reports as a per-item failure — and any future connector whose listing is a query rather than a path.
- **Encountered in:** REQ-RAG-032 / BRD-113, while handling GitHub tree truncation. A repository whose tree exceeds the host's single-response cap is currently reported honestly as a partial run, but cannot be *fixed* without either GraphQL or per-subtree walking.
- **Workaround:** none needed yet; the truncation is reported rather than silently ingesting a prefix of the repository.
- **Suggested fix:** widen to `SendAsync(ConnectorHttpRequest, CancellationToken)` with `Method` and optional `Body` on the request record, keeping `GetAsync` as a default-implemented convenience so no existing implementation breaks. Worth doing before a fourth connector rather than after.

### TR-RAG-022 — the answer to TR-RAG-020: a streaming run API also has to make cancellation non-lossy — **OPEN**
- **Severity:** major (data loss on cancellation; TR-RAG-020 filed the memory half of the same gap as minor)
- **Repro:**
  ```csharp
  // Nine minutes into a ten-minute repository import, the user presses Stop:
  var result = await new ConnectorRunner().RunAsync(connector, previousSync, options, cts.Token);
  // OperationCanceledException. `result` never exists, so:
  //   - every document already fetched is discarded (they were only ever in the runner's List)
  //   - `Sync` is lost, so the next run re-downloads all of them
  //   - `Failures` is lost, so the per-item reasons collected so far are gone too
  // The same is true of rag.IngestConnectorAsync(...), which ingests only AFTER the walk finishes.
  ```
- **Expected:** cancelling keeps what the run already achieved. This is what `WebIngestionService` promises for a crawl ("cancelling keeps what was already ingested") and it is what BRD-65 requires of a connector run, because a cancelled import that ingested nothing is indistinguishable from one that never ran.
- **Actual:** `ConnectorRunner.RunAsync` returns its whole outcome — documents, failures and sync state — in one value at the end, so cancellation and any run-level `ConnectorException` throw all three away. `ConnectorIngestionExtensions.IngestConnectorAsync` compounds it: it calls `IngestTextAsync` only after `RunAsync` has returned, so a run stopped at item 480 of 500 ingests **zero** documents despite having downloaded 479 of them.
- **Encountered in:** REQ-FN-020 / BRD-65 (TechieDesk connector background jobs) — the consumer TR-RAG-020 said it was waiting for.
- **Workaround (shipped, and it is the seam TR-RAG-020 asked for):** `apps/TechieDesk.Core/Services/Connectors/ObservedConnector.cs` decorates `IDataConnector` itself rather than wrapping the runner. `ListAsync` reports the listing; `FetchAsync` reports the attempt, hands the fetched document straight to a per-item sink that ingests it immediately, records the per-item result with its reason, and rethrows any failure so the runner's own failure list and consecutive-failure breaker stay in step. Decorating the connector is the only seam that exists per item, and it is the same trick `ProgressReportingWebContentFetcher` plays on `IWebContentFetcher` for the crawler. Sync state for a cancelled run is reconstructed by the caller from the item versions it saw ingested and merged — never pruned — onto the previous state.
- **Suggested fix:** the `IAsyncEnumerable<ConnectorRunEvent>` overload TR-RAG-020 proposes is the right shape, and this entry adds two requirements to it that a memory-only framing would miss. (1) The stream must carry `ItemFetched` (document), `ItemFailed` (id, name, reason), `ItemSkipped` (id, name, reason — *distinct* from failed, see TR-RAG-023) and `Progress` (listed-so-far / attempted-so-far), so a caller can render live progress and per-item results without decorating anything. (2) Sync state must be readable **incrementally**, not only at completion — e.g. each `ItemFetched` carrying the id/version pair it would contribute — so a consumer that stops the stream keeps a correct, unpruned sync state. Pruning stale entries must remain a completion-only step, since only a full walk can tell "deleted at the source" from "not reached yet". With those two, `IngestConnectorAsync` can also become genuinely incremental instead of ingesting after the fact.

### TR-RAG-023 — `ConnectorItemFailure` conflates "we could not read this" with "we chose not to read this", and its message is culture-formatted
- **Severity:** minor
- **Repro:**
  ```csharp
  var options = new ConnectorRunOptions { MaxItemBytes = 2 * 1024 * 1024 };
  var result = await new ConnectorRunner().RunAsync(connector, null, options);
  // An oversized item and a 403 both arrive as ConnectorItemFailure. The only thing that tells
  // them apart is that ConnectorRunner prefixed one message with the literal string "Skipped: ".
  ```
- **Expected:** a deliberate policy skip and a genuine failure are different outcomes. A run that passed over one 400 MB database dump is a clean run; a run that got a 403 is a partial one. A consumer classifying run outcomes needs to tell them apart without parsing English.
- **Actual:** both are `ConnectorItemFailure`, and `ConnectorRunResult` has one `Failures` list holding both. `ConnectorRunResult.Unchanged` is a third shape again (`IReadOnlyList<ConnectorItem>`, no reason attached, and empty unless `ReportUnchanged` is set), so a caller wanting "every item with the reason it was not ingested" has to merge two differently-typed lists and string-match a prefix on one of them. Two smaller things in the same message: `$"Skipped: {size:N0} bytes exceeds the {options.MaxItemBytes:N0}-byte limit"` uses the ambient culture, so on an `en-IN` machine an operator is told `90,00,000 bytes exceeds the 20,97,152-byte limit`; and `ConnectorIngestionExtensions.BuildMetadata` is `private`, so any caller that ingests per item (see TR-RAG-022) must re-implement the `SourceType`/`SourceName`/`SourceUrl`/`ItemId`/`Version`/`ModifiedUtc` key contract by hand and will silently drift from it.
- **Encountered in:** REQ-FN-020 / BRD-65. `ConnectorJobHandler.LibrarySkipPrefix` is a literal `"Skipped:"` match, which is exactly the kind of coupling that breaks on a wording change nobody thinks of as a breaking change.
- **Workaround:** match the prefix, and record anything else as a failure. It is correct today and will stay correct only by luck.
- **Suggested fix:** add a `ConnectorItemOutcome` enum (`Failed` / `SkippedTooLarge` / `SkippedUnchanged` / `SkippedEmpty`) to `ConnectorItemFailure` — or, better, rename it `ConnectorItemResult` and give `ConnectorRunResult` a single `Items` list carrying every attempted item with its outcome and reason, which is the shape BRD-65 asks for and the shape every consumer builds by hand today. Format sizes with `CultureInfo.InvariantCulture` (or the caller's culture, explicitly chosen — not the ambient one). Make `BuildMetadata` public as `ConnectorIngestionExtensions.BuildDocumentMetadata(IDataConnector, ConnectorItem)` so per-item ingestion produces byte-identical metadata to the batch path.

### TR-RAG-024 — the vector store does not round-trip a document's metadata, so every ingestion route has to smuggle its source URL through `SourcePath` — ✅ **FIXED 2026-07-30**
- **Severity:** major (silent: the data is accepted, the count is right, and the field the user reads is blank)
- ✅ **FIXED 2026-07-30 (`*build-phase`, cluster D), together with TR-RAG-038, which is the same root cause seen from the size column.** The suggested fix's middle option was taken: document-level metadata is now persisted by the store instead of being dropped. `SqliteVecStore` no longer writes a literal `{}` — it lifts the keys named in the new `DocumentMetadataKeys.DocumentScoped` (which includes `SourceUrl`, `SourceType`, `SourceName`, `ItemId`, `ContentType`, `IngestedAtUtc` and `FileSize`) from the document's first chunk onto the document row, and `QdrantStore` carries and reads back the same set. An allowlist rather than the whole dictionary, because a chunk's page number or audio offset is not a fact about the document. `DocumentMetadataKeys.FromJson` also unwraps the stored JSON to CLR primitives, so a stored number is actually usable by the caller rather than an `IConvertible`-less `JsonElement`.
- **What this does NOT change:** the `SourcePath` duplication that web ingestion and `RagConnectorDocumentSink.BuildMetadata` perform is left in place. It is now belt-and-braces rather than the only path, and removing it would strand documents ingested by earlier builds, whose `SourceUrl` genuinely only exists in `SourcePath`. `Document.WebSourceUrl()` is still the right way to read a source URL for that reason.
- **Still not done:** the third option in the suggested fix — a first-class `SourceUri` on `Document` — was not taken. It is a model change on a published SDK and wants a deliberate decision, not a drive-by.
- **Repro:**
  ```csharp
  var id = await rag.IngestTextAsync(
      text, "README.md", new Dictionary<string, object> { ["SourceUrl"] = "https://github.com/o/r/blob/main/README.md" });

  var document = (await rag.ListDocumentsAsync()).Single(d => d.Id == id);
  document.Metadata.TryGetValue("SourceUrl", out _);   // false — the dictionary came back empty
  ```
- **Expected:** metadata supplied to `IngestTextAsync` is readable from `ListDocumentsAsync`. It is the only place a caller can put "where did this come from", and `Document.Metadata` is a public, documented property on the model it comes back in.
- **Actual:** the app's default store (`SqliteVec`) lifts exactly ONE key out of chunk metadata onto the document row — `SourcePath`. Everything else is dropped on the round trip. So a caller that records `SourceUrl` (the natural key, and the one `ConnectorIngestionExtensions.BuildMetadata` itself writes) gets a catalogue whose source column is empty and whose citations point nowhere.
- **Encountered in:** REQ-RAG-019 / BRD-63. Found by the live smoke, not by any test: the connector sink built correct metadata and the assertion "the sink wrote SourceUrl" passed, while the running app showed `url=(none)` for all three real GitHub files. Web ingestion had already hit this and left the fix in a comment — `WebIngestionExtensions.IngestPageAsync` writes the URL to BOTH `SourceUrl` and `SourcePath`, and `Document.WebSourceUrl()` exists solely to read the fallback. That workaround was invisible to the connector cluster, which is exactly how a per-caller workaround becomes a per-caller bug.
- **Workaround (shipped):** `RagConnectorDocumentSink.BuildMetadata` now writes `SourcePath` alongside `SourceUrl`, the same duplication web ingestion makes, with the reason stated at the call site. Asserted end to end in `ConnectorEndToEndTests.ASavedRepositoryConnectorIngestsItsFilesAndAuthenticatesFromTheCredentialStore` through a real store round trip, because the metadata-dictionary assertion is the one that passes while the screen is blank.
- **Suggested fix:** persist the document metadata dictionary in the store (it is already serialized per chunk), or — if that is a deliberate storage decision — make it explicit in `ITechieRag.IngestTextAsync`'s contract that only `SourcePath` survives, and have `ConnectorIngestionExtensions.BuildMetadata` set it itself so every connector caller inherits the correct behaviour instead of rediscovering it. The third option, a first-class `SourceUri` on `Document`, is probably the honest one: three ingestion routes now need it and all three are writing the same string into a general-purpose bag hoping it comes back.

### TR-RAG-025 — `TechieRag.Embedded` cannot load its ONNX native library in a plain `net10.0` console host on macOS — ✅ **FIXED 2026-07-28**
- **Severity:** major for any non-MAUI host (blocks all ingestion there); not reproducible from the MAUI head
- ✅ **FIXED 2026-07-28 (`*build-phase`).** **Root cause — the native asset was never missing, only misnamed at the probe.** `Microsoft.ML.OnnxRuntime` declares its P/Invokes against the literal string `onnxruntime.dll`, extension included. Because the name already carries an extension, .NET never substitutes the platform one: it probes `onnxruntime.dll` and `libonnxruntime.dll` and stops. The package ships `libonnxruntime.dylib`. Proved by reading the failing probe's own path list, which contained `…/runtimes/osx-arm64/native/libonnxruntime.dll` sitting right next to the real `libonnxruntime.dylib` — so the RID layout, the package and the copy-to-output were all correct the whole time.
- **Fix:** `src/TechieRag.Embedded/OnnxNativeLibraryResolver.cs` — a `[ModuleInitializer]` installs a `DllImportResolver` on the `Microsoft.ML.OnnxRuntime` assembly that maps any `onnxruntime*` import onto the real platform file, probing `runtimes/<rid>/native/`, then the app base directory, then `../MacOS` (the .app bundle layout). No-ops on Windows, where the declared name and the shipped file already agree. Returns `IntPtr.Zero` when it finds nothing, so it can only ADD a load path — the statically-linked Mac Catalyst head falls through to the resolution that already worked there, and was re-verified unchanged.
- **Verified:** ONNX now loads in a plain `net10.0` host on macOS-arm64 — `CoreMLExecutionProvider, WebGpuExecutionProvider, CPUExecutionProvider`. Regression tests: `tests/TechieRag.Tests/Embedding/OnnxNativeLoadTests.cs` (4 tests). **The test project did not reference `TechieRag.Embedded` at all** — that is why 1,422 green tests never caught this, and the reference is now added.
- ⚠ **Known limit, stated rather than papered over:** a module initializer runs when its assembly loads, so the fix applies from that point on. Code reaching `Microsoft.ML.OnnxRuntime` *directly*, without first touching a `TechieRag.Embedded` type, still gets the original `DllNotFoundException`. Not a problem for the embedded provider's own consumers (they go through this assembly by definition), but it means this is not a process-wide fix for ONNX Runtime.
- **Repro:** reference `TechieRag.Embedded` from a plain `net10.0` console app on macOS-arm64, build a client with no embedding provider configured (so the embedded BGE-M3 default applies), and ingest anything:
  ```
  System.TypeInitializationException: The type initializer for 'Microsoft.ML.OnnxRuntime.NativeMethods' threw an exception.
   ---> System.DllNotFoundException: Unable to load shared library 'onnxruntime.dll' or one of its dependencies.
        dlopen(.../runtimes/osx-arm64/native/onnxruntime.dll.dylib ... no such file)
        dlopen(.../runtimes/osx-arm64/native/libonnxruntime.dll.dylib ... no such file)
  ```
  The file that IS present is `runtimes/osx-arm64/native/libonnxruntime.dylib`. The probe never asks for that name, because the import is `onnxruntime.dll` and .NET's probing derives `libonnxruntime.dll.dylib` from it. Adding `-r osx-arm64 --self-contained false` does not change the outcome. The model download itself succeeds first ("BGE-M3 model ready!"), so the failure looks like a model problem and is not one.
- **Expected:** the embedded provider is the offline default, so it should work in any host that references the package — that is the whole point of "no cloud key required".
- **Actual:** it appears to work only from the MAUI head. No test covers it: `TechieDesk.Tests` and `TechieRag.Tests` both substitute a stub embedding provider, so a 1,394-test suite is green while the default provider cannot start on this platform.
- **Encountered in:** REQ-RAG-019 / REQ-RAG-020, wiring connector jobs into the `TechieDeskScheduler` helper so connector schedules run with the window closed. The connector half worked end to end — the helper booted, found the `Connector` handler, resolved the saved connector, reached GitHub, listed and fetched all three files — and every ingest then failed on this. The run was honestly recorded as `Partial` with three per-item failures naming the exception, so nothing was silently lost, but nothing was ingested either.
- **Workaround:** configure a non-embedded embedding provider; the helper then uses the same saved `techierag-config.json` as the window. Not exercised here — no cloud key is available in this environment — so it is a reasoned expectation, not a verified one.
- **Suggested fix:** ship a `DllImportResolver` (or `NativeLibrary.SetDllImportResolver`) in `TechieRag.Embedded` that maps the `onnxruntime` import to the platform's real file name, and add one test that actually embeds a string through `EmbeddedEmbeddingProvider` on the CI host. The absence of that single test is why a platform-blocking defect in the *default* provider survived to be found by a connector cluster.

### TR-RAG-026 — `IngestTextAsync` has no upsert, so any repeatedly-synced source duplicates its documents — **OPEN**
- **Severity:** major (silent index corruption for every incremental ingestion route)
- **Repro:**
  ```csharp
  var first  = await rag.IngestTextAsync("Runbook, first edition.",  "docs/runbook.md", metadata);
  var second = await rag.IngestTextAsync("Runbook, second edition.", "docs/runbook.md", metadata);
  // first != second, and BOTH documents are now in the catalogue and both are searchable.
  ```
- **Expected:** a caller re-ingesting the same logical item can say so. Everything the framework needs is already on the item — `ConnectorItem.Id` is documented as "stable identifier within the source; survives across runs" — and the metadata it writes already carries `ItemId`.
- **Actual:** every call mints a new document id, and nothing in the connector framework maps a source item to the document it became. So the second sync of a repository whose three files had changed took the catalogue from 9 documents to 12 (verified by REQ-FN-020's cluster) and every search returned the superseded text alongside the current text, with nothing to distinguish them. Incremental sync makes this *worse*, not better: unchanged items are skipped, so the only items that ever duplicate are the ones the user actually edited.
- **Encountered in:** REQ-RAG-019 / BRD-63. The library's own `IngestConnectorAsync` has the same defect — it calls `IngestTextAsync` per fetched document with no supersede step — so any caller using the supported batch path gets it too.
- **Workaround (shipped, app-side):** a `ConnectorItemDocument` table (`ConnectorId`, `ItemId` → `DocumentId`) owned by TechieDesk, consulted by `RagConnectorDocumentSink` before each ingest; the replacement is written first and `DeleteDocumentAsync` removes the superseded document immediately after. Regression test `ConnectorEndToEndTests.ReSyncingAChangedFileReplacesItsDocumentInsteadOfDuplicatingIt`, mutation-proved RED (3 documents become 6 when the map is bypassed). It is a workaround, not a fix: every future consumer of `IngestConnectorAsync` has to build the same table.
- **Suggested fix:** an `externalId` (or `documentKey`) parameter on `IngestTextAsync` — unique per store, replacing the existing document's chunks in place when supplied — and have `ConnectorIngestionExtensions` pass `ConnectorItem.Id` scoped by `SourceName`. That makes the supported batch path correct by default and removes the per-caller table. Deleting-then-reingesting inside the library is an acceptable first implementation; the important part is that the decision stops being every caller's to rediscover.

### TR-RAG-027 — IMAP command injection: any caller-supplied string reaching a command line could become a second IMAP command
- **Severity:** major (security — arbitrary IMAP commands executed with the mailbox's credentials; defeats the connector's read-only guarantee)
- **Repro:**
  ```csharp
  var transport = new ImapMailTransport(() => connection, new ImapMailboxOptions { Host = "imap.example.test", ... });
  await transport.SearchAsync("INBOX\"\r\nT0099 STORE 1:* +FLAGS (\\Deleted)\r\nT0098 EXPUNGE", new MailSearchCriteria(), 0, 5);
  ```
  What went on the wire (captured from a recording `IImapConnection`):
  ```text
  T0002 SELECT "INBOX\"\r\nT0099 STORE 1:* +FLAGS (\Deleted)\r\nT0098 EXPUNGE"
  ```
  IMAP frames commands by line, so the server reads three lines. Line 1 is a malformed `SELECT` and is answered `BAD`. **Line 2 is a syntactically perfect `STORE` that marks every message in the folder deleted, and line 3 expunges them.** The same hole existed on four other paths, one of which needs no line break at all:
  ```text
  T0001 LOGIN "ada\r\nT0099 LOGOUT" "hunter2"                  // ImapMailboxOptions.Username
  T0003 UID SEARCH FROM "legal@\r\nT0099 ..."                   // MailSearchCriteria.SenderContains
  T0003 UID SEARCH SUBJECT "renewal\r\nT0099 ..."               // MailSearchCriteria.SubjectContains
  T0003 UID FETCH 5\r\nT0099 LOGOUT (BODY.PEEK[])               // MailHeader.Uid — unquoted, so not even escaped
  ```
  And, with no control character involved at all, `MailHeader.Uid = "1:*"` produced `UID FETCH 1:* (BODY.PEEK[])` — a valid sequence set meaning *every message in the folder*, which turns one fetch into a download of the whole mailbox past every scope filter that decided what to fetch. A `Username` containing U+0001 likewise forged a field inside the `XOAUTH2` SASL payload, where that character is the separator the server splits the decoded blob on (`user=ada<U+0001>auth=Bearer attacker-token`).
- **Expected:** nothing a caller supplies can add a command. `Quote()` escaped `\` and `"`, which keeps a quoted string well-formed and does nothing whatever about a line break — and a line break was the whole attack.
- **Actual:** the values above were interpolated straight into command text. The connector's read-only promise is documented as "read-only by construction — every fetch uses `BODY.PEEK` rather than `BODY`", and that promise only covers commands *this* connector sends; an injected `STORE` or `EXPUNGE` is not one of them. The reachable inputs are connector *configuration* — folder list, sender/subject filters, account name — i.e. supplied by whoever fills in the connector form, not by the mailbox owner and not by the server. In TechieDesk that is a workspace user.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135, status `Not Started` — unreviewed pre-existing code). Invisible to the 8 pre-existing email tests because every one of them drives a *cooperative* scripted server with well-formed configuration.
- **Workaround:** none available to a caller — the interpolation was inside the transport. Callers could only have sanitised every string themselves, without being told they had to.
- **Suggested fix (applied):** `Quote(value, what)` now refuses any control character (`char.IsControl`) with an operator-facing reason naming which value was wrong, and is used for the folder name, account name, password and both search filters. Control characters rather than CR/LF alone, because U+0001 is the XOAUTH2 field separator and no folder, mailbox, account name or search term legitimately contains any of them — so refusing outright loses nothing and needs no escaping rules to be correct. `RequireUid` additionally requires a bare positive integer of at most 20 digits before a UID is spliced in, since UIDs are unquoted numbers in the protocol and no escaping would make an arbitrary string safe there. `RunAsync` re-checks the fully composed command line as a last resort, so a future call site that forgets cannot reintroduce the hole. Pinned by `ImapCommandInjectionTests` (10 cases), which assert the invariant — *no line put on the wire contains a line break* — rather than the absence of the word `STORE`, plus `StillSendsAnOrdinaryFolderName` so the guard is not simply refusing everything. Mutation-proved: neutering the control-character check turns the class red; separately neutering only `RequireUid` turns 3 of the 4 identifier cases red (the CRLF case survives on the choke-point guard, which is the point of having both).

### TR-RAG-028 — a server-declared IMAP literal length was honoured without a bound, so the server chose how much memory to allocate
- **Severity:** major (security — remote denial of service against the host application)
- **Repro:**
  ```csharp
  // A hostile server's response to UID FETCH:
  "* 1 FETCH (UID 5 BODY[HEADER] {2000000000}"
  // ImapMailTransport.RunAsync -> TryReadLiteralLength -> pipe.ReadExactAsync(2_000_000_000)
  // SocketImapConnection.ReadExactAsync begins:  var result = new byte[count];
  ```
  Measured with a fake that records the requested count instead of allocating it: `largest ReadExactAsync request = 2,000,000,000 bytes`.
- **Expected:** the length is the server's *claim*, and it is acted on by allocating before a single byte has been read. A claim that large is a request to refuse, not to honour.
- **Actual:** allocated unconditionally. `{4294967295}` happens to be harmless only because `int.TryParse` fails and the line is then mis-framed instead; `{2147483647}` is accepted. The connector runs in-process in the host application, so this is that application's memory. Reaching it needs only a mailbox whose server is attacker-run — an operator pointing the connector at an address someone sent them — or legitimate and compromised.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135). The pre-existing test `ReadsLiteralLengths` asserted `{2048}` parses to 2048; nothing asserted anything about an absurd value.
- **Workaround:** none — the read is inside the transport.
- **Suggested fix (applied):** new `ImapMailboxOptions.MaxMessageBytes` (default 64 MiB, comfortably above the largest message any provider BRD-135 names will deliver) is spent as a *per-response* budget in `RunAsync`, checked before `ReadExactAsync` and decremented per literal so a stream of merely-large literals cannot add up past it either. Over-limit fails with an operator-facing reason that names the limit and the option to raise, rather than skipping the message silently. Pinned by `ImapHostileServerTests.RefusesALiteralLargerThanTheMessageBudget` (asserts nothing was ever *requested*: `LargestRead == 0`) and `RefusesLiteralsThatExceedTheBudgetInAggregate`. Mutation-proved RED.

### TR-RAG-029 — `ImapMailboxOptions.Timeout` had no effect whatsoever, so a hung mail server hung the run indefinitely
- **Severity:** major (availability — a false promise in a configuration knob)
- **Repro:**
  ```csharp
  // Loopback server accepts the TCP connection and then sends nothing, ever.
  var connection = new SocketImapConnection("localhost", port, TimeSpan.FromSeconds(2)); // 2s timeout
  using var rescue = new CancellationTokenSource(TimeSpan.FromSeconds(12));
  await connection.OpenAsync(rescue.Token);
  // D1 after 12.0s threw OperationCanceledException  <- the 2s timeout never fired;
  //                                                     only the outer token ended it
  ```
- **Expected:** a configured timeout bounds the operation. BRD-135's whole reason for a timeout is that a hung server must not hang the app.
- **Actual:** the constructor set `TcpClient.ReceiveTimeout` / `SendTimeout`, **which have no effect on an asynchronous read or write** — they apply only to the synchronous socket APIs, and every read here is `SslStream.ReadAsync`. Nothing else bounded the connect, the TLS handshake, or any read. Every method on `IMailTransport` defaults its `CancellationToken` to `None`, so on the default call path there was nothing to end the wait at all. `SslStream.AuthenticateAsClientAsync` against a server that never sends a TLS hello blocked for the full 12s of the rescue token, which is as long as anyone is willing to wait.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** callers can pass their own `CancellationToken` with a deadline to every transport method. That works, but it is invisible from the option that claims to do it, and the option's XML doc said "connection timeout".
- **Suggested fix (applied):** every connect, handshake, read and write is now wrapped in a linked `CancellationTokenSource` with `CancelAfter(timeout)`. A deadline that fires becomes a `ConnectorException` naming the host, the elapsed budget and the option to raise; the caller's *own* cancellation is still rethrown as `OperationCanceledException`, distinguished by `when (!cancellationToken.IsCancellationRequested)`, so a cancelled run is not misreported as a slow server. Pinned by `ImapHostileServerTests.StopsWaitingOnAServerThatSaysNothing` and `PropagatesTheCallersCancellation` (which pins the distinction), plus `ImapTransportSecurityTests.StopsWaitingOnASilentServer` against a real loopback socket. Both use a rescue token an order of magnitude beyond the configured timeout, so the test terminates when the fix is removed instead of hanging the suite. Mutation-proved RED at both sites.

### TR-RAG-030 — IMAP response accumulation was unbounded in both directions: line length and untagged-line count
- **Severity:** major (security — remote denial of service)
- **Repro:**
  ```csharp
  // (a) a server that never terminates a line: ReadLineAsync accumulates into a List<byte> forever.
  // (b) a server that never sends the tagged completion, only untagged lines:
  var flood = new FloodConnection(budget: 300_000);   // "* LIST (\HasNoChildren) "/" "Folder""  x300k
  await new ImapMailTransport(() => flood, options).ListFoldersAsync();
  // C1 lines consumed = 300,001 in 121 ms   <- all accumulated in List<ImapLine>; a real server
  //                                            would not stop at 300k
  ```
- **Expected:** a command ends when its tagged completion arrives. How long the server takes to get there is the server's choice, so it needs a bound.
- **Actual:** `SocketImapConnection.ReadLineAsync` grew a `List<byte>` until a `\n` arrived, with no length cap; `RunAsync`'s `while (true)` accumulated untagged `ImapLine` records with no count cap. At the measured ~40M lines/minute, either one exhausts memory well before anything else notices. This is the same shape as TR-RAG-028 but a different code path, so capping the literal length does not cover it.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none.
- **Suggested fix (applied):** `SocketImapConnection.MaxLineBytes` (64 KiB — RFC 3501 caps a *command* line at 8192 octets and no real response line approaches it) and `ImapMailTransport.MaxResponseLines` (100,000, two orders of magnitude above any real LIST/SEARCH/FETCH response). Exceeding either drops the connection with an operator-facing reason. The buffering was extracted into a new `internal ImapByteReader` so these behaviours can be driven over a stream a test owns — `SocketImapConnection` keeps the TLS invariant and delegates byte handling to a type that knows nothing about TLS and so cannot weaken it. Pinned by `ImapHostileServerTests.DropsAnEndlessResponseLine`, `GivesUpOnAServerThatNeverCompletesACommand` and `StillReadsAnOrdinaryLineAndLiteral` (so the bounds did not break ordinary framing). Both hostile fakes run dry eventually *only* so the tests terminate when the bound is removed. Mutation-proved RED at both sites.

### TR-RAG-031 — a 39 MB message decoded to 400,000 retained attachments, roughly ten times its own size in live objects
- **Severity:** minor (availability; bounded once TR-RAG-028 landed, but a 10x amplification an attacker picks)
- **Repro:**
  ```text
  P4 parts=1000     raw=98KB    -> 1000 attachments,   0 ms,  +2 MB
  P4 parts=50000    raw=4931KB  -> 50000 attachments,  66 ms, +54 MB
  P4 parts=400000   raw=39453KB -> 400000 attachments, 364 ms, +400 MB
  ```
  A `multipart/mixed` body of one-byte `application/octet-stream` parts. Each becomes a `MailAttachment` record with a `byte[]` and three strings, all retained by `ParsedMailMessage.Attachments`.
- **Expected:** a bound. Anyone with the address can send a message, and inboxes — unlike spam folders — are not excluded from ingestion.
- **Actual:** `MimeParser.ReadPart` appended without limit. The message size itself was unbounded until TR-RAG-028 was fixed; even with that 64 MiB cap the worst case is ~650 MB of retained objects for one message.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** `EmailConnectorOptions.IncludeAttachments` is off by default, which stops attachment *text extraction* but not the parse — `MimeParser.Parse` materialises the list either way.
- **Suggested fix (applied):** `MimeParser.MaxAttachments = 1000`, far above any message a person composes. Where it bites, the body and the first 1000 files still parse, and `EmailConnector` records `AttachmentsSkipped = "attachment list truncated at 1000 (the message declared more parts than are read)"` on the item — BRD-65 asks for a reason an operator can act on for every skip, and a document quietly shorter than the message it came from is a skip nobody was told about. Pinned by `MimeParserHardeningTests.BoundsTheNumberOfAttachmentsOneMessageYields` and `EmailIngestionHardeningTests.ReportsWhenTheAttachmentListWasTruncated` / `ReportsNoTruncationForAnOrdinaryMessage`. Mutation-proved RED at both sites.

### TR-RAG-032 — the IMAP `SINCE` search key was formatted in the machine's culture, so incremental sync would be rejected outright on a non-English machine
- **Severity:** minor (correctness; would present as "incremental sync silently returns nothing", locale-dependent)
- **Repro:**
  ```csharp
  CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
  ImapMailTransport.BuildSearchKeys(new MailSearchCriteria(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
  // was: "SINCE 02-janv.-2026"   -> RFC 3501 date-text requires English month abbreviations; server answers BAD
  ```
- **Expected:** `SINCE 02-Jan-2026` on every machine. The protocol's date form is fixed and is not a display format.
- **Actual:** `keys.Add($"SINCE {since.UtcDateTime:dd-MMM-yyyy}")` used `CurrentCulture`. The code's own comment said "IMAP dates are day-granular and must be in this exact form", which is exactly the thing the interpolation did not guarantee. `Ensure()` would turn the server's `BAD` into a run-level failure, so this fails loudly rather than silently — but only on machines whose culture is not English, which is why no existing test saw it.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135). Same family as the culture-formatted byte sizes noted in TR-RAG-023.
- **Workaround:** none available to a caller.
- **Suggested fix (applied):** `string.Create(CultureInfo.InvariantCulture, $"SINCE {since.UtcDateTime:dd-MMM-yyyy}")`. Pinned by `EmailIngestionHardeningTests.FormatsTheSinceKeyInTheProtocolsOwnCulture`, which swaps `CurrentCulture` to `fr-FR` for the assertion and restores it in a `finally`. Mutation-proved RED.

### TR-RAG-033 — mbox `>From ` unescaping handled only one level of quoting, so deeper quoted replies were indexed with a body the sender did not write
- **Severity:** minor (correctness — silent content corruption)
- **Repro:**
  ```text
  in the file:  >From the desk of Ada        >>From the desk of Bob
  came back:    From the desk of Ada         >>From the desk of Bob     <- one level too deep
  wanted:       From the desk of Ada          >From the desk of Bob
  ```
- **Expected:** mboxrd escapes *any* run of `>` before `From `, so a reader removes one `>` from any such run. `">>From ".StartsWith(">From ")` is false, so the deeper case was never unescaped.
- **Actual:** `line.StartsWith(">From ")` only. A quoted reply containing `>From the desk of…` is written out as `>>From …`, and every deeper quote came back one level too deep. It fails quietly: nothing errors, the archive just indexes text nobody wrote.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none.
- **Suggested fix (applied):** `IsEscapedFromLine` counts the leading `>` run and matches `From ` after it, removing exactly one `>`. Pinned by `MboxHardeningTests.UnescapesEveryDepthOfAnEscapedFromLine`, plus `SplitsOnAnUnescapedFromLine` which records that an *unescaped* `From ` line still splits the archive — that is the format's own ambiguity, not a defect here, and worth a test so nobody "fixes" it later. Mutation-proved RED.

### TR-RAG-034 — an attachment name kept its NTFS alternate-data-stream colon
- **Severity:** minor (security hardening; no exploitable path in the current tree)
- **Repro:** `Content-Disposition: attachment; filename="report.pdf:evil.exe"` → `MailAttachment.FileName == "report.pdf:evil.exe"`.
- **Expected:** the name is reduced to something inert. Path traversal was already handled correctly — `../../etc/passwd` → `passwd`, `..\..\Windows\win.ini` → `win.ini`, `/etc/shadow` → `shadow`, `C:\Users\ada\secrets.pdf` → `secrets.pdf`, `....` → `attachment` — the colon was the one survivor.
- **Actual:** kept. Nothing in TechieRag writes attachments to disk, so this is not exploitable *here*: the value is handed to `IDocumentProcessor.ProcessAsync` as the document name and shown to a user. It is logged because on Windows a downstream component that does open the name by path opens the stream rather than the file, and the difference is invisible in every listing that shows the name — and because the extension allow-list is keyed off this string.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** the `AttachmentExtensions` allow-list already rejects `report.pdf:evil.exe`, because `Path.GetExtension` returns `.pdf:evil.exe`, which is not in the list. That is luck rather than intent.
- **Suggested fix (applied):** `SafeFileName` now replaces `:`, `/` and any control character with `_` after reducing to the last path segment. Pinned as a `[Theory]` case in `MimeParserHardeningTests.ReducesAnAttachmentNameToABareFileName` alongside the five traversal cases. Mutation-proved RED.

### TR-RAG-035 — `MimeParser` silently discards content past its depth cap and has nowhere to report it — **OPEN, needs a product decision**
- **Severity:** minor (correctness/observability; not a security defect — the cap itself is correct and works)
- **Repro:**
  ```csharp
  // 500 nested multiparts with distinct boundaries, a text/plain part at the bottom:
  var parsed = MimeParser.Parse(deeplyNested);
  // No exception, no stack overflow, no attachments, and an empty body.
  // Nothing anywhere says "there was content and it was not read".
  ```
- **Expected:** BRD-65's rule — an operator-facing reason for every skip — applied to the parser's own bounds, the way TR-RAG-031's attachment truncation now is.
- **Actual:** `ReadPart` returns silently at `depth > 10`. The cap is *right* and it works (verified: 500 levels deep costs the nested content, not the stack, and no input in an 11-case malformed corpus made `Parse` throw). But the message comes back with an empty body and no indication why, and the connector reports a successful document.
- **Why this is not fixed here:** `ParsedMailMessage` is a public positional record with no diagnostics field, and `MimeParser` is a static class with no logger. Adding either is a breaking API change to a published SDK, and choosing between them — a `Notes`/`Diagnostics` collection on the record, an `out` parameter, an overload taking a callback, or accepting the silence for genuinely-rare input — is a product decision about the SDK's shape rather than a security fix. The TR-RAG-031 truncation reason was reportable only because `EmailConnector` could *infer* it from `Attachments.Count == MaxAttachments`; depth truncation leaves no such observable.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none. Nesting past 10 levels does not occur in mail people send; the realistic trigger is a malformed or deliberately-constructed message, which is the case where knowing would matter most.
- **Suggested fix:** give `ParsedMailMessage` a `IReadOnlyList<string> Notes` (default empty, so existing positional construction keeps compiling only if it is added last and defaulted) and have `MimeParser` record "content nested deeper than 10 multiparts was not read" and any other bound it hits, then have `EmailConnector` fold `Notes` into the item's skip reasons the way it now folds attachment truncation. If that is too much surface, the alternative is to state the silence in the XML docs as a known limit — but it should be a decision, not an omission.

### TR-RAG-036 — informational: the HTTP connectors' SSRF guard must NOT be applied to the mail transport, and here is what was verified instead
- **Severity:** informational (no defect — recorded so a future audit does not "fix" this wrongly)
- **Context:** TR-RAG-017 rightly required an SSRF guard on `HttpConnectorTransport`, and the audit that found it flagged that the email connector "has its own transport that does NOT go through the guarded handler". That is true, and copying the guard here would be a regression.
- **Why blanket blocking would be wrong:** an HTTP connector's target is derived from a URL that may have come from a response body, so an operator never intended `169.254.169.254`. A *mail host* is named directly by the operator and is legitimately arbitrary — corporate IMAP lives on private and link-local-adjacent addresses far more often than not (`mail.corp.internal`, `10.x`), and `localhost:993` is how anyone tests a local Dovecot. Refusing RFC 1918 targets would break the ordinary enterprise case while protecting nothing, because there is no untrusted party choosing the address.
- **The property that actually matters, and is now pinned:** the target cannot be changed by anything the *server* says. There is no redirect, no response-supplied host, and no second connection — verified by `ImapTransportSecurityTests.ConnectsOnlyToTheConfiguredHost`, which drives a whole run (authenticate → list → select → search → fetch) against a server whose greeting and tagged completion both carry `[REFERRAL imap://evil.example.invalid/]`, and asserts the connection factory was invoked exactly once and `MailboxName` is still the configured host. An IMAP `REFERRAL` is the closest thing this protocol has to a redirect and this client ignores it.
- **What carries the security instead:** TLS with platform certificate validation against the configured host name. Verified: `RefusesAPlaintextImapServer` points the real `SocketImapConnection` at a real loopback socket that answers with a genuine plaintext `* OK [CAPABILITY IMAP4rev1 STARTTLS] ready` — exactly what port 143 sends — and asserts the connection is refused at the handshake and that the bytes the client sent contain no `LOGIN`, no `STARTTLS` and no `CAPABILITY`. There is no STARTTLS code path to strip, no `RemoteCertificateValidationCallback`, no accept-all, and `ExposesNoWayToDisableCertificateValidation` reflects over every public property in the namespace to assert none is a certificate callback, an `SslClientAuthenticationOptions`, or a bool named for SSL/TLS/certificate/insecure — so the regression of someone adding an "ignore certificate errors" switch is caught by a test rather than by a review.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Suggested fix:** none. If a guard is ever wanted here it should be an operator-set *allow*-list of mail hosts, not an address-class deny-list.

### TR-RAG-037 — `EmailConnector` item identity embeds the **archive's file name**, so relocating an `.mbox` re-ingests the entire archive — ✅ **FIXED 2026-08-03**

- **Severity:** major (silent duplicate ingestion — costs re-embedding of a whole archive and pollutes the document library; the user sees every message twice with no error).
- **Repro:** ingest a local `.mbox` through the email connector, then rename or move the archive file and let the next scheduled sync run.
- **Expected:** message identity is stable across a rename/move, so the second sync reports every message unchanged and ingests nothing.
- **Actual:** `EmailConnector` gives every item the id `{folder}/{uidValidity}/{uid}`, and for `MboxMailTransport` **`folder` is the archive's file name without extension** (`MboxMailTransport.FolderName`). Renaming or relocating the archive therefore changes the identity of every message in it, so the next sync re-ingests all of them and the document library gains a duplicate of every message.
- **Why it is clearly a bug, not a design choice:** `MboxMailTransport` **already prefers the stable `Message-ID` as the UID**, precisely to avoid this class of problem — the file-derived folder segment then throws that stability away.
- **Observed, not theorised:** moving a fixture from `techiedesk-smoke.mbox` to `req-fn-042-smoke.mbox` between two runs turned *"the previous run recorded 4 item version(s)"* + 0 fetched into *"fetched 5, skipped 0 unchanged"*, producing **5 duplicate documents** in the vector store.
- **Encountered in:** `apps/TechieDeskScheduler` end-to-end scheduled-ingest smoke, 2026-07-29 (REQ-FN-042). The 5 duplicates are still in the smoke data directory and are noted on that REQ's row.
- **Workaround:** none applied — the ids are computed inside the library. Consumers can only avoid renaming archives, which is not something a product can guarantee its users.
- **Suggested fix:** for the mbox transport, key the item on the `Message-ID` alone and keep the file-derived folder as display metadata only. Consider asserting in the connector that the identity function does not depend on a mutable path for any file-backed transport.
- ✅ **FIXED 2026-08-03 (`*build-phase`, RAG cluster), following the suggested fix.** Three changes, all in this repo's library:
  1. **`MailHeader` gains an additive nullable `StableId`.** The TRANSPORT supplies an identity when its own coordinates are not a safe basis for one. This is the seam the connector was missing: it cannot see whether a `Folder` value came from a server or from a file path, and it should not guess.
  2. **`EmailConnector.ToItem` prefers it** — `header.StableId ?? $"{Folder}/{UidValidity}/{Uid}"`. **IMAP passes null and is completely unchanged**, deliberately: `folder/uidvalidity/uid` is *correct* for IMAP because the server owns all three and announces when they stop meaning what they meant. Keying IMAP on `Message-ID` instead would have broken incremental sync for every existing IMAP user to fix a file-backed bug.
  3. **`MboxMailTransport` supplies `mbox/{Message-ID}`**, falling back to `mbox/sha256:{hash of the raw message}` when a message carries none. The fallback matters: the previous one was the message's POSITION in the file, so an archive that gained a message at the front renumbered everything after it — the same defect in a second costume, and now closed too.
- **Tests (2, both passing):** `MessageIdentitySurvivesRenamingTheArchive` is this entry's own reproduction — the same bytes under two names — asserted **through `EmailConnector`** rather than the transport, because the id the connector composes is the one `ConnectorRunner` keys `ItemVersions` on and therefore the value that actually decides re-ingestion. `IdentityOfAMessageWithoutAMessageIdSurvivesReordering` covers the hash fallback.
- **Known consequence, accepted:** two byte-identical messages with no `Message-ID` in one archive now collapse to a single identity. That is correct — they are the same message, and ingesting it twice is the defect this fixes.

### TR-RAG-038 — no ingestion route recorded the source artefact's byte size, so `Document.Metadata` could never answer "how big is this?" — ✅ **FIXED 2026-07-30**

- **Severity:** minor in the library, but it made a required product column permanently unfillable (REQ-UI-021 / BRD-46 names *Size* as a library column).
- **Note on the ID.** `docs/TechieDesk-Checklist.md` recorded this defect against **TR-RAG-004**, which in this file is an unrelated Qdrant scroll-pagination note. The size gap had never actually been logged here. It is logged as TR-RAG-038, and TR-RAG-004 now carries a pointer so the mislabel does not send the next reader to the wrong entry.
- **Repro (before the fix):**
  ```csharp
  var id = await rag.IngestAsync("/path/to/notes.md");          // a 7,080-byte file
  var document = (await rag.ListDocumentsAsync()).Single(d => d.Id == id);
  document.Metadata.TryGetValue("FileSize", out _);              // false — the dictionary was empty
  ```
- **Actual (before the fix):** the size existed nowhere in the pipeline. `TechieRagClient.IngestAsync` opened the file, read it, and never recorded its length; `IngestTextAsync` recorded nothing about the text's size either. Even if they had, `SqliteVecStore` — the desktop default — wrote the document row's `Metadata` column as the literal string `"{}"`, so nothing could have survived the round trip. The consuming screen's probe was correct the whole time and rendered its em-dash fallback on every row of every library, forever.
- **Encountered in:** REQ-UI-021 (BRD-46). Confirmed live at `/workspace/default/documents`, 3/3 rows showing `—` while every other column was populated.
- ✅ **FIXED 2026-07-30 (`*build-phase`, cluster D).** Three changes, all in this repo's library:
  1. **`src/TechieRag/Models/DocumentMetadataKeys.cs` (new).** Names the well-known document-level metadata keys — `FileSize`, `SourceUrl`, `SourceType`, `SourceName`, `ContentType`, `ItemId`, `IngestedAtUtc` — and owns `ExtractDocumentScoped`, the single rule deciding which chunk metadata is a fact about the whole document. An **allowlist**, deliberately: a denylist would let a genuinely chunk-local key (a PDF page number, an audio segment offset) start being published as a document-level fact the moment a processor added one.
  2. **`src/TechieRag/TechieRagClient.cs`.** `IngestAsync` records `FileSize` = the file's length on disk, read from the stream before the processor consumes it — the only point in the pipeline where the source artefact's own size is still available (chunk lengths are inflated by overlap). `IngestTextAsync` records `FileSize` = the UTF-8 byte count of the text, which for pasted text, a fetched page and a transcript *is* the artefact; a caller that knows a truer number overrides it through the existing `metadata` argument.
  3. **`src/TechieRag/Connectors/ConnectorIngestionExtensions.cs`.** `BuildMetadata` records `ConnectorItem.SizeBytes` — the size the SOURCE reports — as `FileSize` when the source reports one, overriding the text-derived default. A connector item's extracted text is not its size when the source file carries markup or a different encoding.
  4. **`SqliteVecStore` / `QdrantStore` / `PgVectorStore`.** SqliteVec no longer writes a hardcoded `{}` — it lifts the document-scoped keys onto the document row. Qdrant carries them in a single `Metadata` payload string and reads them back (it previously returned a `Document` with no metadata at all). PgVector already persisted the whole chunk dictionary, so only its **read** path changed.
- **The third trap, which is the one worth remembering.** Deserializing a metadata column into `Dictionary<string, object>` produces `JsonElement` values, and `JsonElement` does not implement `IConvertible`. The size would then be stored correctly, returned correctly, and still be unreadable to `Convert.ToInt64` — which on a guarded caller is indistinguishable on screen from the original bug. `DocumentMetadataKeys.FromJson` unwraps to CLR primitives, and all three stores now use it. (A follow-on of the same shape: the obvious `TryGetInt64(out var n) ? n : GetDouble()` has the natural type `double`, so the `long` was widened before boxing. Caught by the live smoke printing `Double`, not by any assertion — there is now one.)
- **Tests:** `tests/TechieRag.Tests/Ingestion/DocumentSizeMetadataTests.cs` (7) drives file, text and folder ingestion through a real `SqliteVecStore`; `tests/TechieRag.Tests/Connectors/ConnectorIngestionTests.cs` (+2) covers the source-reported size and its absence; `tests/TechieDesk.Tests/Workspaces/DocumentSizeDisplayTests.cs` (5) drives the real `WorkspaceManager` into a real store and asserts the shipping display probe renders `4.0 KB` / `640 B` / `—`.
- **Routes that deliberately still show `—`, and why:** (a) any document ingested before this change — the source artefact is not retained, so there is nothing to read; (b) a connector item whose source reports no `SizeBytes` falls back to the ingested text's byte count, which is a real number but is the *stored text's* size, not the remote artefact's — stated here rather than presented as equivalent; (c) the same caveat applies to a crawled web page and a YouTube transcript: what is recorded is the size of the readable text that was stored, because the HTTP payload (or the video) is not the thing the library holds and reporting it would describe something the user cannot retrieve.
- **Live proof (real production database, `~/Library/Application Support/TechieDesk/techierag.db`):** a 7,080-byte file ingested through `WorkspaceManager.IngestFileAsync` stored `{"FileSize":7080}` on its document row and rendered `6.9 KB`; a 90-byte file rendered `90 B`. The 12 documents ingested before the fix still render `—` and neither throw nor get backfilled — which is the correct answer for them, since their source artefacts were never retained.
- **Known limits, stated rather than papered over:** (a) there is **no backfill** — a size that was never recorded cannot be recovered honestly, and reconstructing one from stored chunk text would be inflated by chunk overlap; (b) PgVector persists a **superset** (the whole first chunk's metadata, its long-standing behaviour) while SqliteVec and Qdrant persist the allowlist, so document metadata is not byte-identical across stores. Narrowing PgVector would delete data an existing deployment may be reading, so it was left alone.


### TR-RAG-039 — `ToolRegistry.Register` de-duplicates the handler but not the tool definition, so re-registering a name advertises the same tool twice to the model

- **Severity:** minor (correctness of the tool list sent to the LLM; no data loss, but the model is shown a duplicate function and providers differ in how they react — some reject a `tools` array with repeated names outright).
- **Repro:**
  ```csharp
  var registry = new ToolRegistry();
  registry.Register("rag-search", "first",  "{\"type\":\"object\"}", (_, _) => Task.FromResult("a"));
  registry.Register("rag-search", "second", "{\"type\":\"object\"}", (_, _) => Task.FromResult("b"));
  registry.ToolDefinitions.Count;   // 2 — expected 1
  // ExecuteToolAsync correctly runs only the second handler.
  ```
- **Expected:** registering an existing name replaces that tool — one definition, one handler — because the handler dictionary already has exactly those replace semantics (`handlers[name] = handler`).
- **Actual:** `src/TechieRag/Services/ToolRegistry.cs:45` does `definitions.Add(...)` unconditionally while line 51 does `handlers[name] = handler`. The two collections therefore disagree after any repeat registration: the definition list grows, the handler map does not. `ToolDefinitions` is what `IToolHandler` publishes to the provider, so the duplicate reaches the wire.
- **Encountered in:** REQ-RAG-022 (BRD-84), wiring the six workspace skills into `AgentToolPlanner.BuildRegistry`. Not hit in production — the planner composes its implementation list from `WorkspaceSkillTools.All`, which yields each catalogue name once — but the guarantee is the library's to make, not each caller's. A consumer that concatenates two implementation sources (say, workspace skills plus MCP tools that happen to share a name) gets a malformed tool list with no error.
- **Workaround applied in TechieDesk:** none needed; `AgentToolPlanner` filters by a permitted-name `HashSet` and the implementation list is built by a single factory, so a name cannot appear twice. Callers assembling tools from more than one source would need to de-duplicate themselves.
- **Suggested fix:** in `Register`, replace an existing definition rather than appending — e.g. find the index by `OrdinalIgnoreCase` name (the same comparer `handlers` uses) and overwrite, else append. Worth a test asserting `ToolDefinitions.Count == 1` after a repeat registration and that the *second* description is the one published.


### TR-RAG-040 — every SQLite-backed default in the library is a bare relative path, so a hosted consumer silently stores user data in whatever the process working directory happens to be

- **Severity:** major for any consumer that is not a console app run from its own working directory. In TechieDesk it corrupted the shipped artefact: `codesign --verify --deep --strict` on the Release `.app` failed with *"unsealed contents present in the bundle root"* and the signed bundle could not be produced until the file was deleted by hand.
- **Repro:**
  ```csharp
  var config = new TechieRagConfig();
  config.VectorStore.ConnectionString;      // "Data Source=techierag.db"  — relative
  new TechieRagBuilder().UseSqliteVec();    // databasePath = "techierag.db" — relative
  ```
  Build the client from a host whose working directory is not writable-by-intent, initialize it, and a live SQLite database appears there.
- **Expected:** a default that cannot silently land somewhere the consumer did not choose. Either no default at all (make the path a required argument on `UseSqliteVec`, which fails loudly at the call site) or a default anchored to a location the library can defend (`Environment.SpecialFolder.LocalApplicationData`), with the relative form still accepted when a caller passes one explicitly.
- **Actual:** `src/TechieRag/TechieRagConfig.cs:214` defaults `VectorStoreConfig.ConnectionString` to `"Data Source=techierag.db"` and `src/TechieRag/TechieRagBuilder.cs:233` defaults `UseSqliteVec(string databasePath = "techierag.db")`. Both resolve through SQLite against `Environment.CurrentDirectory` at open time. Because `ConnectionString` is **non-nullable and pre-populated**, a consumer's defensive `savedConfig.VectorStore.ConnectionString ?? MyDefault()` can never fire — the relative literal is not null, so it flows straight through. That is precisely how it survived in TechieDesk: the null-coalesce read as a guard and was dead code.
- **Why it is worse than it looks on a GUI host.** On Mac Catalyst, UIKit **resets the process working directory to the `.app` bundle root** at launch, discarding whatever the parent process set (measured with `lsof -d cwd`: cwd = `…/TechieDesk.app`). So the relative default did not depend on how the app was started — it resolved into the signed bundle on *every* launch.
- **Encountered in:** REQ-FN-048 (BRD-130).
- **Workaround applied in TechieDesk:** the app never lets a relative path reach the library. `TechieDeskDb.DataDirectory.ResolveSqliteConnectionString` rewrites any relative `Data Source` into the per-user data directory, and it is applied at all three seams — reading the saved configuration, writing it, and building the `TechieRagBuilder` in `TechieRagManager`. Documented in `apps/TechieDesk.Core/Services/TechieRagConfigService.cs`.
- **Suggested fix:** at minimum, document on `UseSqliteVec` and `VectorStoreConfig.ConnectionString` that the default is working-directory relative and unsuitable for GUI or service hosts. Better: drop the default argument on `UseSqliteVec` so the choice has to be made, and make `ConnectionString` nullable so a consumer's `?? MyDefault()` works as it reads. The same note applies to `SqliteConversationStore` and `SqliteWorkspaceStore`, whose XML docs advertise the same relative example.


### TR-RAG-041 — `McpWorkspaceTools` loses the tool→server mapping, so a host cannot apply a per-server policy to MCP tools without re-deriving it from the qualified name — ✅ **FIXED 2026-08-01**

- **Severity:** minor-to-moderate. Nothing is incorrect; the information a host needs to make a *security* decision per server is simply not returned, so the host has to reconstruct it from a string and reconstruct it conservatively.
- **Repro:**
  ```csharp
  var tools = await registry.BuildWorkspaceToolsAsync(workspaceId, policy, localTools, loggerFactory);
  tools.StartedServers;                 // ["ledger", "local-index"]  — names only
  tools.ToolHandler.ToolDefinitions;    // ["ledger-lookup", "local-index-search"] — qualified names only
  // Nothing relates the two, and nothing returns the raw McpToolDescriptor list per server.
  ```
- **Expected:** the result object exposes which tools came from which server — e.g.
  `IReadOnlyDictionary<string, IReadOnlyList<McpToolDescriptor>> ToolsByServer` on `McpWorkspaceTools`.
  The extension already holds exactly this: its local `discovered` list is
  `(McpClient Client, IReadOnlyList<McpToolDescriptor> Tools)` pairs, and it is thrown away after
  being handed to `McpToolHandler.FromDiscovered`.
- **Actual:** `McpWorkspaceTools` exposes `ToolHandler`, `StartedServers` and `Failures`. `McpToolHandler` keeps its `bindings` dictionary (qualified name → client + tool name) private, and `FromDiscovered` is `internal`. So a host with a per-server rule has two options: call `tools/list` a second time per server (an extra round trip, and for an HTTP server an extra outbound request), or match on the `{server}-` prefix `McpToolHandler.QualifyToolName` produces.
- **Encountered in:** REQ-RAG-023 (BRD-86), TechieDesk. The app must apply its `EgressGate` (REQ-NFR-013) to MCP tools **hosted off the machine** — i.e. tools belonging to an `McpTransportKind.Http` server — and must *not* apply it to a stdio server, whose tools run locally and for which the prompt's "sends a request off this machine" wording would be false. That is a per-server decision, and the mapping to make it is exactly what is missing.
- **Workaround applied in TechieDesk:** `apps/TechieDesk.Core/Services/Agents/Mcp/McpEgressGuard.cs` matches the `{server}-` prefix. This is sound but only because of two facts that are not contractual: `QualifyToolName` puts the server name first, and it truncates from the RIGHT with a hash suffix, so with `Name` capped at 48 characters the `{server}-` prefix always survives the 64-character limit. Ambiguity between a server named `acme` and one named `acme-eu` is resolved towards gating, so the failure mode is an extra confirmation rather than a silent egress — but "we deduced the security boundary from a string" is not where a host wants its trust decision to live.
- **Suggested fix:** add `ToolsByServer` to `McpWorkspaceTools` (populated from the `discovered` list the extension already builds — no extra round trip and no behaviour change), or make `McpToolHandler` expose `ServerNameFor(string qualifiedToolName)`. Either turns the prefix heuristic into a lookup. A secondary benefit: a host caching "what this server advertises" for its admin screen currently has to make its own `tools/list` call, because the discovery the agent turn already performed is not returned either.

- ✅ **FIXED 2026-08-01 (`*build-phase`, cluster C, REQ-RAG-042).** Both halves of the suggested fix landed, because the guardrail seam for agent orchestration needs exactly this fact:
  1. **`src/TechieRag/Mcp/McpToolHandler.cs`** gained `public string? ServerNameFor(string qualifiedToolName)`, a lookup against the `bindings` dictionary the handler already keeps. Null for a name it does not expose.
  2. **`src/TechieRag/Mcp/McpAgentExtensions.cs`** gained `McpWorkspaceTools.ToolsByServer` (`IReadOnlyDictionary<string, IReadOnlyList<McpToolDescriptor>>`) populated from the `discovered` list `BuildWorkspaceToolsAsync` was already building and throwing away, plus a delegating `ServerNameFor`. **No extra round trip and no behaviour change** — the same `tools/list` results, returned instead of discarded.
- **Why it was fixed here rather than filed onward.** REQ-RAG-042's guardrail contract puts a `GuardrailStage.ToolCall` check in front of every tool a flow dispatches, and hands the guardrail the tool's name and description. A host deciding "does this leave the machine?" for an MCP tool needs the tool's SERVER, and that is a security boundary. Shipping a new guardrail seam whose only route to that fact was a string-prefix guess would have baked the workaround into a second consumer.
- **What the app can now do.** `apps/TechieDesk.Core/Services/Agents/Mcp/McpEgressGuard.cs` can replace its `{server}-` prefix match with `tools.ServerNameFor(name)` and look the transport up from the registration. That app-side change is **not** made here (this cluster owns library work only) and is left for the owning cluster; the prefix heuristic remains correct in the meantime, so nothing is broken by deferring it.
- **Tests:** `tests/TechieRag.Tests/Mcp/McpToolProvenanceTests.cs` (4). One of them is the `acme` / `acme-eu` pair the entry above called out — under the prefix heuristic `acme-eu-search` also matches the `acme-` prefix, and the test asserts the two resolve to different servers. RED-proven: reverting `ServerNameFor` to a `StartsWith` scan turns that test and the unknown-name test red.

### TR-RAG-042 — `FlowNodeCatalog.CreateNode` pre-fills `Name` from the English descriptor, so a localized builder gets English step names that do not match its own palette (minor, found 2026-08-01 during REQ-UI-040)

- **Severity:** minor, but it lands in stored user data rather than only on screen.
- **Repro:**
  ```csharp
  var node = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition);
  Console.WriteLine(node.Name);   // "Condition"
  ```
- **Expected:** either the node arrives with `Name` empty (so the host names it), or the pre-filled
  name is documented as an English placeholder the host is expected to replace.
- **Actual:** `CreateNode` sets `Name = Describe(kind).DisplayName`, which is the catalogue's
  English label. Two consequences, both observed on TechieDesk's Mac Catalyst head on 2026-08-01:
  1. **It disagrees with the host's own palette word.** TechieDesk's palette calls
     `FlowNodeKind.Condition` a *"Branch"* — a branch point is what it is, and "Condition" is
     already taken on that screen by the edge predicate editor. So pressing the button labelled
     **Branch** added a step labelled **Condition**. Screenshotted.
  2. **In Hindi it is silently English**, and it is then serialized into `DefinitionJson` and stored,
     so the English default outlives the session in which the language was wrong. The library's
     English-only strings are correct by design (`FlowNodeKindDescriptor` documents this); the
     problem is only that one of them is written into a *data* field rather than shown as a label.
- **Encountered in:** REQ-UI-040 (BRD-92), 2026-08-01.
- **Workaround (applied):** the builder overwrites `Name` with its own localized palette label
  immediately after `CreateNode` returns, in both the new-flow seed and the add-step path.
- **Suggested fix:** leave `Name` empty in `CreateNode` and let `FlowNode.DisplayName` — which already
  falls back to `Id` — carry the "unnamed" case, or add an optional `name` parameter alongside the
  existing `id` one. The XML doc's "with its kind's display name pre-filled" should then say whether
  a host is expected to replace it.

### TR-RAG-043 — a builder cannot tell whether a flow will call a model without re-deriving it from the catalogue (nice-to-have, found 2026-08-01 during REQ-UI-040)

- **Severity:** nice-to-have. Nothing is wrong; the fact is derivable and the derivation is two lines.
  Filed because getting it *wrong* costs a false refusal, and TechieDesk shipped one for an afternoon.
- **Repro:** a host wants to answer "can I run this flow on an install with no LLM provider
  configured?". `FlowDefinition` exposes nodes and edges; `FlowNodeKindDescriptor.UsesLlm` exposes the
  per-kind answer. Joining them is the host's job.
- **What went wrong here:** TechieDesk's first cut demanded a provider for *every* run and refused a
  branch-and-end flow — which costs no tokens and makes no request — with "no LLM provider is
  configured". Found by running it on the Catalyst head, not by the suite.
- **Suggested fix:** a `bool UsesLlm` (or `IReadOnlyList<FlowNode> LlmNodes`) computed property on
  `FlowDefinition`, derived from the catalogue exactly as a host would derive it. It belongs next to
  `ResolveStartNode()` and `EdgesFrom()`, which are the same shape of convenience — facts about the
  graph the library already knows and every host would otherwise re-implement. The same accessor would
  let a builder show a "this flow costs tokens" badge without enumerating kinds.
- **Workaround (applied):** `FlowCapabilities.NeedsLlmProvider(flow)` app-side, derived from
  `FlowNodeCatalog.Kinds` so a future node kind brings its own answer.

### TR-RAG-044 — raw SentencePiece ids are fed to an XLM-RoBERTa graph, so every token is off by one — ✅ **FIXED in the reranker 2026-08-03, and in the embedder 2026-08-04**

- **Severity:** major. In the reranker it made cross-lingual ranking worse than random. In `EmbeddedEmbeddingProvider` — the desktop default, and the provider behind every stored vector — it is **still present**.
- **The mistake:** the SentencePiece model and XLM-RoBERTa do not share a vocabulary. SentencePiece numbers `<unk>=0`, `<s>=1`, `</s>=2` then its pieces; XLM-R's fairseq vocabulary is `<s>=0`, `<pad>=1`, `</s>=2`, `<unk>=3` then the same pieces **one slot later**. Hugging Face's `XLMRobertaTokenizer` reconciles them with `spm_id + 1` (and spm `0` → `<unk>` = 3). Both of this library's ONNX components passed `tokenizer.EncodeToIds(...)` straight through with no shift.
- **Why it hid for the entire life of the code:** the shift is **consistent**, so a query and a passage that share a word still share its (wrong) id. Lexical-overlap relevance therefore survives, and every same-language test looks fine. What does not survive is anything needing the embeddings to actually mean something.
- **Observed, not theorised (2026-08-03):**
  - **Reranker, before the fix:** Hindi query `फ्रांस की राजधानी क्या है?` against three English passages ranked *"Bicycles should have their chains oiled regularly"* **above** *"Paris is the capital city of France."* — the worst candidate winning, not a near miss. After adding the offset: correct, and all six same-language tests still pass. `tests/TechieRag.Tests/Reranking/Live/LiveOnnxRerankerTests.cs` — **7/7**.
  - **Embedder, still open:** same query, cosine similarity **0.3536 to the relevant passage vs 0.3642 to the irrelevant one** — wrong passage wins, and both sit at noise level (a genuine BGE-M3 cross-lingual match is ~0.7+). English ranks correctly, exactly as the reranker did while broken.
- ✅ **Reranker fix:** `OnnxCrossEncoderReranker.ToModelId` applies `FairseqOffset` to model-produced pieces only; the `<s>`/`</s>` ids the pair encoding adds are already fairseq ids and are left alone.
- ✅ **Embedder fix, 2026-08-04, on the owner's explicit decision to re-ingest.** `EmbeddedEmbeddingProvider.GenerateEmbedding` now applies its own `ToModelId` and wraps the sequence in `<s>` … `</s>` (it previously had **neither** — raw ids AND no special tokens at all). Two slots are reserved before truncation so a maximum-length input cannot overflow the sequence limit, and the reported `tokenCount` now counts what the model actually processed, wrapper included.
- **Result, measured on the same probe:** cross-lingual similarity went from **0.3536 (losing to 0.3642)** to **0.7182** — from noise-with-the-wrong-winner to a genuine multilingual match. Same-language retrieval unchanged, as expected.
- **Executable evidence, now ON by default:** `LiveEmbeddedTokenizerDiagnosticTests` was opt-in while the defect was open — so a machine with BGE-M3 cached would not go red over a decision that belonged to the owner. The decision is made, so it runs whenever the weights are staged; a regression here is now a real failure. The two multi-gigabyte ONNX test classes were also moved into one non-parallel collection (`OnnxModelCollection`), since ~4.5 GB of concurrently-resident model weights is its own hazard.
- 🔶 **CONSEQUENCE THE OWNER MUST ACT ON: every vector embedded before 2026-08-04 is incompatible with every vector embedded after.** They are not merely lower quality — they are in a different space, and cosine similarity between the two is meaningless. **Re-ingest the whole corpus; do not top up.** A partial re-ingest silently mixes both, and the failure mode is diffuse bad retrieval with nothing in the logs.
- ✅ **Detection added 2026-08-04 (the follow-up this entry asked for).** `IEmbeddingProvider.EmbeddingSignature` publishes `{provider}/{model}/r{revision}`; ingestion stamps it on every document via `DocumentMetadataKeys.EmbeddingSignature`; `EmbeddingStaleness.Analyze` compares a stored corpus against it, and `ITechieRag.DetectStaleEmbeddingsAsync` exposes the result. **The revision is the load-bearing part** — this defect changed neither the provider nor the model, so a provider/model comparison would not have caught it.
  - **An UNSTAMPED document is reported as stale, not as "probably fine"** — the pre-2026-08-04 corpus is exactly the population this exists for, and a missing stamp is the only evidence it leaves.
  - **A provider that publishes no signature reports `IsDeterminable = false`**, never a clean result. Returning an empty stale list for a check that never ran would be the same class of lie the parent defect was about.
  - **`IsMixed` is distinguished from `IsEntirelyStale`.** All-stale is the ordinary state after an encoding change; mixed means a re-ingest was started and not finished, leaving two incomparable spaces in one store — the worse condition, and the one nobody can see without this.
  - Added as **default interface members** so implementations outside this repository keep compiling (REQ-NFR-007), and implemented explicitly on `TechieRagClient` and `TechieRagManager` because a default member is unreachable from the concrete type.
  - **18 tests**, including an end-to-end pass against a real `SqliteVecStore` — the round trip is where `FileSize` was silently dropped once (TR-RAG-038), so asserting the chunk dictionary alone would have proved nothing.
  - **It has a consumer**, deliberately: the document library shows a localized warning (en + hi) naming how many documents are affected, with an extra line for the mixed case. A detection API nobody calls detects nothing — the mistake TR-RAG-050 made and REQ-UI-058 is still paying for.

### TR-RAG-045 — the reranker's first-run download could never succeed: 404 URL, missing external-data file, and a completeness check that was false by construction — ✅ **FIXED 2026-08-03**

- **Severity:** major — `OnnxCrossEncoderReranker` was **dead code on any machine without a pre-staged model**, which is why REQ-RAG-025 could record that it "has never been executed".
- **Three independent faults, all in the download path:**
  1. **404.** `DefaultBaseUrl` pointed at `BAAI/bge-reranker-v2-m3`, which publishes PyTorch weights and **no ONNX export at all** — `onnx/model.onnx` returns HTTP 404. Now `onnx-community/bge-reranker-v2-m3-ONNX` (Hugging Face's own conversion org), whose layout matches what the file list already expected: `onnx/model.onnx` at 2,271,088,656 bytes against the 2,270,000,000 the code already carried as its approximate size. The code was written against that repo; only the URL pointed elsewhere.
  2. **The weights were never downloaded.** An ONNX graph above 2 GB cannot fit in one protobuf, so the export is a 656 KB `model.onnx` stub plus a 2.27 GB `model.onnx_data` sidecar. `ModelFiles` listed only the stub. `EmbeddedEmbeddingProvider` has always fetched its own `model.onnx_data`; this list simply omitted it.
  3. **`IsModelDownloaded()` was false by construction.** It required `model.onnx` to exceed 1 GB — never true for an external-data export, where that file is the stub. Even a perfect download reported "not downloaded", so the class would have re-fetched 2.27 GB on every call. Now checks the sidecar's size, as the embedder always has.
- **Also fixed:** the resume check accepted any non-empty file as complete, so a truncated 2.27 GB download was treated as finished and failed later inside `InferenceSession` with nothing pointing back at the download. It now requires ~the expected size.
- **A second repo is required and that is not a mistake:** the ONNX export ships `tokenizer.json` but no `sentencepiece.bpe.model`, which this class needs; that one file comes from BAAI's official repo. Neither repo alone can satisfy the download.
- **Regression cover:** `LiveRerankerDownloadUrlTests` range-requests the first byte of each composed URL — the whole class costs a few kilobytes and would have caught all of this immediately. A mirror override (`TECHIERAG_RERANKER_BASE_URL`) was added for parity with the embedder, per REQ-NFR-008.
