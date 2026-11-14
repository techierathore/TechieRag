# TechieRag feedback — found while building Sevak (TechieDesk until 2026-09-24)

| | |
|---|---|
| App | Sevak |
| Upstream | TechieRag |
| Updated | 2026-11-13, later (TR-RAG-064 copied in from Sevak's file and answered: fixed for TechieRag 1.1.10); 2026-11-13 (TR-RAG-059 and TR-RAG-060 copied in from Sevak's file and answered: fixed for TechieRag 1.1.10); 2026-11-11 (TR-RAG-061 to TR-RAG-063 added from Sevak's 9 November report, relayed by the owner, and answered: fixed for TechieRag 1.1.10); 2026-11-07 (TR-RAG-055 to TR-RAG-058 added: four requests for Sevak's assistant phases, written as TechieRag BRD-210 to BRD-213; evening: TR-RAG-055 blocks nothing, the live tests take a settings file, 056 and 057 name package 1.1.9) |

## Summary

Nothing is blocked: no entry is open. 61 entries: 0 open, 46 fixed upstream and not yet re-checked here, 15 closed (as `tf-feedback.sh` counts them). TR-RAG-059 to TR-RAG-064 are fixed upstream for TechieRag 1.1.10, not yet published: 059, 060 and 064 on 2026-11-13 (060 proven on a real PostgreSQL 18 with pgvector 0.8.6; 064 against Anthropic's pricing page of that day), 061 to 063 on 2026-11-11. TR-RAG-055 to TR-RAG-058 (Sevak's phase 4 and 5 requests) were built for 1.1.9 on 2026-11-08 and ship with 1.1.10; TR-RAG-056 and TR-RAG-057 hold Sevak's REQ-RAG-116 and REQ-UI-087 until that package is on the feed. TR-RAG-053 and TR-RAG-049 are worked around in Sevak until it moves to the new package.

These are the application's findings against `src/TechieRag`, filed while it lived in this repository (TechieRagWeb, then TechieDesk, now Sevak), restructured 2026-09-24 into the feedback template with the original text unchanged; the earlier summary is under "Replies from TechieRag". Accepted items are ledgered as TechieRag requirements (BRD-121, 126, 127, 142, 143, 148, 158; phase 3; BRD-210 to 213).

## Entries

<!-- Append entries as gaps are found. IDs are append-only, never renumbered.
     TrBlazeUI → TR-NNN · TechieRag → TR-RAG-NNN -->

### TR-RAG-001 — Streaming RAG cannot return sources or accept pre-fetched context (forces a second vector search)
- **Severity:** major
- **Blocks:** no — the Chat sample searches once and streams through `ILlmProvider.ChatStreamAsync` meanwhile; the work carried on.
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
- **Blocks:** no — the Chat sample estimates and records the usage when the streamed delta is zero; the work carried on.
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
- **Blocks:** no — the admin grid's "Vectors" column binds to `IndexedVectorsCount`; the work carried on.
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
- **Blocks:** no
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
- **Blocks:** no
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
- **Blocks:** no
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
- **Blocks:** no — the workspace path already reports truncation (REQ-RAG-048), and this gap is outside that requirement's acceptance; the work carried on.
- **Repro:** call `ITechieRag.AskAsync` / `AskStreamWithSourcesAsync` directly (not via `WorkspaceManager`) with more than `PromptConfig.MaxContextChunks` search results.
- **Expected:** after REQ-RAG-048, callers can detect that context was truncated.
- **Actual:** REQ-RAG-048 fixed the **workspace** seam only. `PromptTemplateEngine.FormatContext` still does `searchResults.Take(config.MaxContextChunks)` with no flag, event, or log, so non-workspace consumers remain blind to truncation.
- **Encountered in:** REQ-RAG-048 (scoped to the merged pinned+retrieved workspace context, so this is out of that REQ's acceptance).
- **Workaround:** none.
- **Suggested fix:** apply the same `WorkspaceContext`-style diagnostics (or at minimum the warning log) at the `PromptTemplateEngine` seam.

### TR-RAG-010 — Pinned-chunk retrieval issues one vector search per pinned document (minor, found 2026-07-25)

- **Severity:** minor (scales badly, correct today)
- **Blocks:** no — retrieval is correct today and fine at small scale; the work carried on.
- **Repro:** ask one question in a workspace with several pinned documents and count the vector searches.
- **Expected:** one search for the pinned documents, however many are pinned.
- **Actual:** `WorkspaceManager.CollectPinnedChunksAsync` runs one vector search **per pinned document** — observed live: 6 pinned documents produced 7 searches for a single question.
- **Encountered in:** `WorkspaceManager.CollectPinnedChunksAsync`, observed live 2026-07-25.
- **Impact:** fine at small scale; O(n) round-trips at 50+ pinned documents, against a remote vector store this is the dominant latency.
- **Workaround:** none.
- **Suggested fix:** a single filtered query over the pinned document-id set.

### TR-RAG-011 — Pinned chunks bypass reranking (nice-to-have, found 2026-07-25)

- **Severity:** nice-to-have (needs a product decision)
- **Blocks:** no — pinned chunks stay merged ahead of the reranked results, as REQ-RAG-047 kept them deliberately; the work carried on.
- **Repro:** turn on the per-workspace rerank switch (REQ-RAG-047) in a workspace with pinned documents and ask a question.
- **Expected:** a stated rule for whether reranking orders pinned chunks.
- **Actual:** with the new per-workspace rerank switch (REQ-RAG-047) enabled, retrieved chunks are reranked but **pinned** chunks are not — they are merged ahead of the reranked results by position.
- **Rationale for current behaviour:** kept deliberately stable/low-risk in REQ-RAG-047 so pinning semantics did not change.
- **Encountered in:** REQ-RAG-047, 2026-07-25.
- **Workaround:** none.
- **Suggested fix:** decide explicitly whether reranking should order *within* pinned documents, and document whichever way it lands.

### TR-RAG-012 — `RerankConfig.Enabled` has changed meaning (informational, 2026-07-25) — **fixed upstream 2026-10-08, ships in the next TechieRag package after 1.1.2, not yet re-checked here**

- **Severity:** informational — **needs a release note before the next NuGet publish**
- **Blocks:** no — informational; it needs a release note before the next NuGet publish, and the work carried on.
- **Repro:** set `RerankConfig.Enabled = false` with a usable rerank source and credentials, then build the client.
- **Expected:** a consumer reading `Enabled: false` as "build no reranker" is told that this is no longer what it means.
- **Actual:** the reranker is constructed anyway; since REQ-RAG-047, `Enabled` only sets the default for calls that do not specify. The full change is under Detail.
- **Encountered in:** REQ-RAG-047, 2026-07-25.
- **Consumer impact:** anyone relying on `Enabled: false` to prevent reranker *construction* (e.g. to avoid an ONNX model download or an API key check) will see different behaviour.
- **Workaround:** none needed in the app.
- **Suggested fix:** a release note before the next NuGet publish stating the changed meaning.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Change (REQ-RAG-047):** `Enabled` previously meant "build and use a reranker". It now means "rerank by **default** for calls that do not specify". The reranker is constructed whenever a usable source + credentials exist, because a workspace must be able to force rerank ON while the instance default is OFF — impossible if no `IReranker` was ever instantiated.

#### Fixed upstream 2026-10-08 (TechieRag)

- **Documented, under `TechieRag#REQ-RAG-098` (re-Verified 2026-10-08).** No behaviour changed.
  - **XML docs:** `RerankConfig.Enabled` now reads "whether a search reranks **by default**". Its remarks give the changed meaning, say a reranker is built whenever `Source` is usable even with `Enabled = false`, and say that to build no reranker (no model download, no key check) you leave `Source = RerankSource.None`. A consumer's IDE shows this.
  - **Release note:** `RELEASE-NOTES.md` in the TechieRag repository, section "Next release after 1.1.2", under "Behaviour, stated plainly". The AI reference section 8 (Reranking) says the same.
  - **Tests:** `REQ-RAG-098 EnabledFalseStillBuildsAUsableReranker`, `MissingKeyFailsOnlyWhenEnabled` and `EnabledDocumentationStatesChangedMeaning` (reads the shipped XML documentation file).
  - **Verify from here:** after moving Sevak to the new package, hover `RerankConfig.Enabled` in the IDE and read the remarks, then close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-012 "<what you ran and what it showed>"`.

### TR-RAG-013 — `WorkspaceManager` is a sealed-in-practice concrete class with no interface, so app services that compose it cannot be unit-tested in isolation (minor, found 2026-07-26)

- **Severity:** minor
- **Blocks:** no — tests build a real `WorkspaceManager` through `TechieRagBuilder` meanwhile; the work carried on.
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
- **Workaround:** build a real `WorkspaceManager` via `TechieRagBuilder` with a stub embedding provider, `SqliteVec` and SQLite persistence; `TechieRagManager.GetWorkspaceManagerAsync()` was made `virtual` app-side. Full text under Detail.
- **Suggested fix:** extract an `IWorkspaceManager` interface covering the CRUD surface (`List`/`Get`/`Create`/`Update`/`Delete`), or mark those members virtual. A separate, smaller win: let `TechieRagBuilder.Build()` skip vector-store construction when only persistence is requested.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Workaround:** build a real `WorkspaceManager` via `TechieRagBuilder` with `UseCustomEmbeddingProvider(() => stub)` + `SqliteVec` + `WithPersistence(StoreProvider.Sqlite, temp)`. This works and is arguably better coverage (a real SQLite store), but it means every such test pays for a vector-store construction it does not use, and it only works because a stub embedding provider can be injected. `TechieDesk.Core`'s own `TechieRagManager.GetWorkspaceManagerAsync()` had to be made `virtual` app-side to let the manager be substituted at all.

### TR-RAG-014 — `SqliteVecStore` discards document-level metadata, so `ListDocumentsAsync` always returns `Metadata = {}`
- **Severity:** major
- **Blocks:** no — web ingestion writes the URL to both `SourceUrl` and `SourcePath` meanwhile; the work carried on.
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
- **Actual:** `Metadata` is always an empty dictionary: `src/TechieRag/VectorStores/SqliteVecStore.cs` writes the `Documents` row with a hardcoded `Metadata = "{}"` (lines 169 and 246). Nothing warns or fails.
- **Encountered in:** REQ-RAG-016 / REQ-RAG-017 (TechieDesk "Add from web"): every ingested web document came back with an empty `SourceUrl`.
- **Workaround:** web ingestion writes the URL to both `SourceUrl` and `SourcePath`, and `Document.WebSourceUrl()` reads whichever survived. Files and regression tests are under Detail.
- **Suggested fix:** serialize the document's metadata into `Documents.Metadata` instead of `"{}"`, check `PgVectorStore` and `QdrantStore` for the same stub, and add a store-contract test that metadata round-trips.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** `Metadata` is always an empty dictionary. `src/TechieRag/VectorStores/SqliteVecStore.cs` writes the `Documents` row with a hardcoded `Metadata = "{}"` (lines 169 and 246); the caller's metadata is persisted only onto the `Chunks` rows. Nothing warns, and nothing fails — the ingestion reports success and the documents are fully searchable. Only the metadata is silently gone.
- **Encountered in:** REQ-RAG-016 / REQ-RAG-017 (TechieDesk "Add from web"). Every ingested web document came back with an empty `SourceUrl`, so the results list rendered a blank source column for every row while reporting "Ingested 3 documents". This is exactly the render-empty failure the smoke policy exists to catch, and it survived a 135-test suite because every one of those tests asserts the service's return value against a scripted store.
- **Workaround:** web ingestion now writes the URL to **both** `SourceUrl` and `SourcePath` in the ingestion metadata (`src/TechieRag/Web/WebIngestionExtensions.cs`), because `SourcePath` *is* lifted from chunk metadata onto the document row. A new `Document.WebSourceUrl()` extension reads whichever survived, and `WebIngestionService.ReadCatalogueAsync` calls it instead of indexing `Metadata` directly. Regression tests: `IngestedDocumentsReportTheSourceUrlAfterAStoreRoundTrip` and `EachCrawledDocumentReportsItsOwnSourceUrl` in `tests/TechieDesk.Tests/Web/WebIngestionServiceTests.cs`, which run through the real SQLite-vec store.
- **Suggested fix:** serialize the document's metadata into the `Documents.Metadata` column instead of `"{}"`. The column, the JSON round-trip in `DocumentRow.ToDocument()`, and the deserialization are all already there and correct — only the write is stubbed. Worth checking `PgVectorStore` and `QdrantStore` for the same stub, and worth a store-contract test asserting metadata round-trips, since a consumer cannot tell from the API that the guarantee does not hold.

### TR-RAG-015 — YouTube transcript ingestion is non-functional: timed-text URLs return HTTP 200 with a zero-byte body
- **Severity:** major (external platform change; the library cannot currently deliver REQ-RAG-018)
- **Blocks:** no — the failure is now reported honestly and nothing is ingested, with the live canary kept red; the work carried on.
- **Repro:**
  ```csharp
  var reader = new YouTubeTranscriptReader(new HttpClient());
  await reader.ReadAsync("https://www.youtube.com/watch?v=aircAruvnKk");
  ```
  Reproduced without TechieRag on 2026-07-27 against four videos and every output format; details under Detail.
- **Expected:** the timed-text URL named in the watch page returns the caption track.
- **Actual:** **HTTP 200, `Content-Length: 0`, empty body** for every video, format and client shape. Caption discovery still works; the InnerTube fallbacks are closed too. Consistent with YouTube requiring a proof-of-origin token.
- **Encountered in:** REQ-RAG-018. Live canary `TranscriptIsReadFromARealVideoWithCaptions` in `tests/TechieRag.Tests/Web/Live/LiveYouTubeTranscriptTests.cs` — **currently and knowingly RED.**
- **Workaround:** none for the transcript itself; the reporting was fixed so the failure is honest and nothing empty is ingested. Details under Detail.
- **Suggested fix:** a product decision: (a) the YouTube Data API v3 `captions` endpoint, (b) a caller-supplied cookie/PO-token, or (c) stop offering video ingestion as if it worked. Keep the live canary red.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Repro:**
  ```csharp
  var reader = new YouTubeTranscriptReader(new HttpClient());
  await reader.ReadAsync("https://www.youtube.com/watch?v=aircAruvnKk");
  ```
  Reproduced independently of TechieRag with plain `curl`/`urllib` on 2026-07-27, from a residential connection, against four unrelated videos (`dQw4w9WgXcQ`, `jNQXAC9IVRw`, `aircAruvnKk`, `8S0FDjFBj8o`) and every output format (`fmt` absent, `json3`, `srv1`, `srv3`, `ttml`, `vtt`), with both a bot user-agent and a full browser user-agent plus `CONSENT` cookie.
- **Actual:** **HTTP 200, `Content-Length: 0`, empty body — every video, every format, every client shape.** Caption *discovery* still works: the watch page still contains `"captionTracks"` with valid-looking signed `baseUrl` values (31 tracks for `aircAruvnKk`). It is only the download that is dead. The InnerTube fallbacks are closed too: `/youtubei/v1/player` returns `UNPLAYABLE` for `WEB`/`MWEB`, `LOGIN_REQUIRED` for `ANDROID_VR`, and HTTP 400 for `ANDROID`/`IOS`. This is consistent with YouTube requiring a proof-of-origin token for caption downloads.
- **Encountered in:** REQ-RAG-018. Live canary `TranscriptIsReadFromARealVideoWithCaptions` in `tests/TechieRag.Tests/Web/Live/LiveYouTubeTranscriptTests.cs` — **currently and knowingly RED.**
- **Workaround:** none possible for the transcript itself. What was fixed is the *reporting*, so the failure is honest and cheap to diagnose:
  - The empty-body case previously said *"The caption track for this video was empty."*, which blames the video and sends the operator to try a different one that will fail identically. It now names the restriction and states that nothing was ingested.
  - Discovery and download are now separately observable: the live suite asserts the watch page still exposes `captionTracks` independently of the reader, so when this file is next read it is immediately clear which half is broken.
  - The end-to-end path is asserted to add **nothing** rather than an empty document (`RealVideoIngestionEitherYieldsATranscriptOrAddsNothingAtAll`), so a blank, unretrievable "video" document never reaches the library.
- **Suggested fix:** decide the product answer, because the technical one is not in the library's gift. Either (a) integrate the YouTube Data API v3 `captions` endpoint, which needs an API key and OAuth for most videos; (b) accept a caller-supplied cookie/PO-token so an authenticated operator can still ingest; or (c) surface the restriction in the UI and stop offering video ingestion as if it worked. The live canary should be kept red rather than deleted — it is the only instrument that will report the day this starts working again.

### TR-RAG-016 — `TechieRag.Embedded.UseEmbedded()` throws `DllNotFoundException` on macOS/Linux for any plain `net10.0` consumer
- **Severity:** major
- **Blocks:** no — a test-side `[ModuleInitializer]` resolver maps the library name meanwhile; the work carried on.
- **Repro:** from a `net10.0` (non-MAUI) project on macOS or Linux:
  ```csharp
  var rag = new TechieRagBuilder().UseEmbedded().UseSqliteVec().Build();
  await rag.IngestTextAsync("hello", "Doc");
  // System.TypeInitializationException -> DllNotFoundException:
  //   Unable to load shared library 'onnxruntime.dll'
  ```
- **Expected:** `UseEmbedded()` works on every platform the package restores native assets for. The correct file is present in the output at `runtimes/osx-arm64/native/libonnxruntime.dylib`.
- **Actual:** `Microsoft.ML.OnnxRuntime.Managed` 1.24.1's `net8.0` assembly hardcodes its `DllImport` name as `"onnxruntime.dll"`, which never matches `libonnxruntime.dylib` on Unix. The MAUI heads are unaffected, so the failure is invisible from the app and total for tests, console tools and server-side consumers.
- **Encountered in:** REQ-RAG-016/017/018 end-to-end verification; `tests/TechieDesk.Tests` targets plain `net10.0`.
- **Workaround:** `tests/TechieDesk.Tests/Support/OnnxRuntimeNativeLibraryResolver.cs` — a `[ModuleInitializer]` installing a `NativeLibrary.SetDllImportResolver` that maps `"onnxruntime.dll"` to the per-platform file under `runtimes/{rid}/native/`.
- **Suggested fix:** ship the same resolver inside `TechieRag.Embedded`, and consider a per-machine model cache instead of one relative to the assembly directory.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** `Microsoft.ML.OnnxRuntime.Managed` 1.24.1's `net8.0` assembly — the one a plain `net10.0` project resolves — hardcodes its `DllImport` name as the literal string `"onnxruntime.dll"`. On Unix .NET probes `onnxruntime.dll`, `libonnxruntime.dll`, `onnxruntime.dll.dylib` and `libonnxruntime.dll.dylib`, none of which matches `libonnxruntime.dylib`. The MAUI heads are unaffected because `net10.0-maccatalyst` resolves the `net9.0-maccatalyst18.0` managed assembly instead. So the failure is invisible from the shipping app and total for tests, console tools, and any server-side consumer on macOS/Linux.
- **Encountered in:** REQ-RAG-016/017/018 end-to-end verification. `tests/TechieDesk.Tests` targets plain `net10.0`, so nothing could exercise the real embedding model until this was worked around — which is why the "does ingested web content actually come back out of search" question had never been answered.
- **Workaround:** `tests/TechieDesk.Tests/Support/OnnxRuntimeNativeLibraryResolver.cs` — a `[ModuleInitializer]` that installs a `NativeLibrary.SetDllImportResolver` mapping `"onnxruntime.dll"` to the correct per-platform file under `runtimes/{rid}/native/`. It only ever answers a name the runtime has already failed to resolve, so it cannot mask a different problem. With it in place the live end-to-end suite runs the real BGE-M3 model and passes.
- **Suggested fix:** ship the same resolver inside `TechieRag.Embedded` rather than leaving it to every consumer to discover — a static constructor on `EmbeddedEmbeddingProvider` registering a `DllImportResolver` for the ONNX Runtime assembly would fix it once for everyone, and would also make the package honest about supporting non-Windows hosts. Separately, `EmbeddedEmbeddingProvider.GetModelDirectory()` resolves the 2.3 GB model cache relative to the *assembly* directory, so it is re-downloaded per output folder and wiped by a clean; a per-machine cache (e.g. under the user profile) with the assembly directory as a fallback would be a meaningful improvement for anyone running more than one consumer.

### TR-RAG-017 — `HttpConnectorTransport` shipped a weaker SSRF guard than the one this codebase had already learned to build, and defaulted it off
- **Severity:** major (security; found and fixed within REQ-RAG-032)
- **Blocks:** no — found and fixed within REQ-RAG-032; the work carried on.
- **Repro:**
  ```csharp
  using var client = HttpConnectorTransport.CreateDefaultClient(); // plain HttpClientHandler
  var transport = new HttpConnectorTransport(client);              // blockPrivateTargets defaulted to FALSE
  await transport.GetAsync(new ConnectorHttpRequest("http://127.0.0.1.nip.io/admin"));
  // request lands on loopback, with the connector's Authorization header attached
  ```
- **Expected:** the connector transport enforces the same connect-time guard `HttpWebContentFetcher.CreateGuardedHandler` already provides — decide on the RESOLVED address, then connect only to the address that was checked.
- **Actual:** three weaknesses in one class: `blockPrivateTargets` defaulted to **false**; when enabled, the only check was the textual `WebCrawlOptions.IsPrivateNetworkHost`, which `127.0.0.1.nip.io` walks through; and `CreateDefaultClient()` built a bare `HttpClientHandler` with no connect-time guard. There were no tests. The transport attaches an `Authorization` header, so this leaks a token.
- **Encountered in:** REQ-RAG-032 / BRD-113 (`IDataConnector` framework).
- **Workaround:** (fix applied) `CreateDefaultClient(bool blockPrivateTargets = true)` now builds `HttpWebContentFetcher.CreateGuardedHandler(...)`, the constructor defaults to `true`, and the final URL is re-checked after redirects. Tests are under Detail.
- **Suggested fix:** one library-wide factory for every outbound `HttpClient` to a caller-supplied address, with the guard built in.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** three independent weaknesses in one class. (1) The constructor's `blockPrivateTargets` defaulted to **false**, with a comment arguing that an operator-typed base URL is not attacker-influenced. (2) When it *was* enabled, the only check was `WebCrawlOptions.IsPrivateNetworkHost(uri.Host)` — the textual check whose docstring in this very repo says "It is not the enforcement point, and must not be treated as one", and which `127.0.0.1.nip.io` walks straight through. (3) `CreateDefaultClient()` built a bare `HttpClientHandler`, so no `ConnectCallback` ran at all and there was no connect-time guard to fall back on. There were no tests for any of it — `tests/TechieRag.Tests/Connectors/` had no transport test file.
- **Why it matters more here than for the crawler:** this transport attaches an `Authorization` header to every request. A URL steered at an internal endpoint does not merely read it, it presents the source's credential to it. The crawler's equivalent bug leaks a response; this one leaks a token.
- **Encountered in:** REQ-RAG-032 / BRD-113 (`IDataConnector` framework). The reasoning in the original comment is the interesting part: it was not an oversight but a deliberate argument that this component's threat model differed. It did not.
- **Workaround / fix applied:** `CreateDefaultClient(bool blockPrivateTargets = true)` now builds `HttpWebContentFetcher.CreateGuardedHandler(...)`; the constructor default is `true`; the textual check is retained only as a fast path and documented as such; connect-time refusals are unwrapped so the operator sees the accurate reason instead of "could not be reached"; and the final URL is re-checked after redirects for callers who supply their own unguarded `HttpClient`. Regression tests: `tests/TechieRag.Tests/Connectors/HttpConnectorTransportTests.cs` (17 hermetic, incl. a real loopback listener asserting the request never arrives) plus a live proof against `127.0.0.1.nip.io` in `Connectors/Live/LiveRepositoryConnectorTests.cs`. Mutation-proved: reverting the handler, the default, or the redirect re-check each turns a named test RED.
- **Suggested fix (library-wide):** the guard should not be something each new outbound component re-derives. Anything in this library that opens a socket to a caller-supplied address should be required to obtain its `HttpClient` from one factory that has the guard built in, so "did this component remember?" stops being a per-component question. A second component reimplementing a security control weakly, six months after the first one learned to do it properly, is a structural signal rather than a one-off defect.

### TR-RAG-018 — `ConfluenceConnector` followed URLs supplied by the response body, with the site's API token attached
- **Severity:** major (security; credential exfiltration; found and fixed within REQ-RAG-032)
- **Blocks:** no — found and fixed within REQ-RAG-032 with a same-origin check on every response-supplied link; the work carried on.
- **Repro:** a Confluence site (or anything able to influence its responses) answers a listing with:
  ```json
  { "results": [], "_links": { "base": "https://attacker.example/", "next": "https://attacker.example/collect" } }
  ```
  The connector used `_links.next` verbatim as the next request URL, and `BuildHeaders()` attaches `Authorization: Basic base64(email:apiToken)` to every request.
- **Expected:** a URL derived from a response is still a URL, and must be constrained to the site the operator configured before a credential is sent to it.
- **Actual:** `ReadNextLink` returned any absolute `_links.next` unchanged, and `ReadLinkBase` accepted any `_links.base`. A hostile or compromised response could (a) receive the caller's Atlassian API token, (b) redirect the entire walk, and (c) set every citation link in RAG answers to an address of its choosing.
- **Encountered in:** REQ-RAG-020 / BRD-64.
- **Workaround:** (fix applied) `RequireSameOrigin` (scheme + host + port) now gates the paging cursor, the `next` link and the stated link base; a foreign absolute page link is discarded; refusal messages name the URL and never the token. Regression tests are listed under Detail.
- **Suggested fix:** any future connector that follows a link stated by its source (a `Link:` header, a HAL `_links`, a GraphQL cursor URL) needs the same origin pin. Worth making it a shared helper on the connector framework rather than per-connector, and worth stating in `IDataConnector`'s remarks that response-supplied URLs are untrusted input.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** `ReadNextLink` returned any absolute `_links.next` unchanged, and `ReadLinkBase` accepted any `_links.base` and used it to resolve relative links and to build the citation URLs shown to users. So a hostile or compromised response could (a) receive the caller's Atlassian API token, (b) redirect the entire walk, and (c) set every citation link in the RAG answers to an address of its choosing. `_links` was followed precisely *because* Cloud sites answer on a `/wiki` prefix the caller may not have configured — a real requirement that made the input load-bearing.
- **Encountered in:** REQ-RAG-020 / BRD-64. This one is worth noting as a class: the SSRF review that produced TR-RAG-017 was aimed at the *base URL* as the attacker-influenced input. The response body being an equally good injection point for a "next URL" was a second, separate hole in a different file, and the transport-level fix does not close it — an attacker-named URL on a public host is not a private-network target.
- **Workaround / fix applied:** `RequireSameOrigin` (scheme + host + port) now gates the paging cursor at the point of request, the `next` link at the point of production, and the stated link base; a foreign absolute page link is discarded in favour of a constructed one. Refusal messages name the offending URL and never the token. Regression tests in `ConfluenceConnectorTests`: `RefusesAPagingLinkThatLeavesTheConfiguredSite`, `RefusesAForeignCursorSuppliedByTheCaller`, `RefusalNamesTheUrlAndNeverTheToken`, `IgnoresALinkBaseThatLeavesTheConfiguredSite`, `IgnoresAPageLinkThatLeavesTheConfiguredSite`, plus `StillFollowsAnAbsoluteSameSitePagingLink` so the guard does not break paging. All mutation-proved RED.

### TR-RAG-019 — a connector run had no aggregate size budget, only a per-item one
- **Severity:** minor (found and fixed within REQ-RAG-032)
- **Blocks:** no — found and fixed within REQ-RAG-032 by adding an aggregate byte budget; the work carried on.
- **Repro:** run any connector over a documentation repository of ~500 files that are each 100 KB. Every file passes `MaxItemBytes` (2 MB) and the count is within `MaxItems` (500), so the run completes holding ~50 MB of text in `ConnectorRunResult.Documents`. At the defaults the theoretical worst case was `MaxItems × MaxItemBytes` = **1 GB**, and `ConnectorRunner`'s own docstring cited that product as the memory bound.
- **Expected:** the bound a caller reasons about should be the aggregate, since that is what occupies memory.
- **Actual:** no aggregate cap existed. A per-item cap stops one bad file and can never stop the sum of good ones, which is the shape every real source has.
- **Encountered in:** REQ-RAG-032 / BRD-113, design requirement "caps on item count and total bytes".
- **Workaround:** (fix applied) `ConnectorRunOptions.MaxTotalBytes` (default 64 MB), enforced in `ConnectorRunner` on real UTF-8 byte counts; the item that crosses the budget is kept and its version recorded. Tests are listed under Detail.
- **Suggested fix:** none further — the aggregate cap above is the fix.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Workaround / fix applied:** `ConnectorRunOptions.MaxTotalBytes` (default 64 MB), enforced in `ConnectorRunner` on real UTF-8 byte counts of fetched text. The item that crosses the budget is kept and its version recorded — discarding it would re-fetch it forever and a source larger than the budget could never converge. Tests: `StopsAtMaxTotalBytes`, `KeepsTheItemThatReachedTheByteBudget`, `CountsMultiByteTextByItsBytes`, `DoesNotReportALimitWhenTheBudgetWasNotReached`.

### TR-RAG-020 — `ConnectorRunner` materialises every fetched document in memory; there is no streaming run API — **OPEN**
- **Severity:** minor (design limit, bounded but not removed)
- **Blocks:** no — `MaxTotalBytes` caps peak memory meanwhile and a truncated run resumes on the next run; the work carried on.
- **Repro:**
  ```csharp
  var result = await new ConnectorRunner().RunAsync(connector);
  // result.Documents holds the full text of every item fetched, all at once
  ```
- **Expected:** for the "large repository must not be materialised in memory" requirement to hold end to end, a caller should be able to consume documents as they are fetched.
- **Actual:** listing is incremental, fetching is not: `RunAsync` collects `List<ConnectorDocument>` and returns it whole, so peak memory is the whole run, capped by `MaxTotalBytes` (TR-RAG-019) rather than by the design.
- **Encountered in:** REQ-RAG-032 / BRD-113; left open so the REQ-FN-020 background-job consumer defines the streaming seam.
- **Workaround:** set `MaxTotalBytes` to what the host can afford; `ConnectorRunResult.ReachedLimit` plus the returned `Sync` make a truncated run resumable, so a source larger than the budget converges over several runs rather than failing.
- **Suggested fix:** an `IAsyncEnumerable<ConnectorRunEvent>` overload yielding fetched-document / item-failure / progress events, with the sync state available at completion.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** *listing* is genuinely incremental — paged by cursor, filtered before fetch, oversized items skipped from the listing without downloading. *Fetching* is not: `RunAsync` collects `List<ConnectorDocument>` and returns it whole. The reason is defensible and documented — the failures and the sync state come out of the same walk, and handing them back through a side channel is how per-item failures get ignored — but it does mean peak memory is the whole run, now capped by `MaxTotalBytes` (TR-RAG-019) rather than by the design.
- **Encountered in:** REQ-RAG-032 / BRD-113. Called out here rather than fixed because the concurrent REQ-FN-020 background-job cluster is the consumer that would define what a streaming shape needs to yield, and guessing at it now would likely produce the wrong seam.
- **Suggested fix:** an `IAsyncEnumerable<ConnectorRunEvent>` overload yielding a discriminated union of fetched-document / item-failure / progress, with the sync state available at completion. That shape keeps failures un-ignorable (they are in the same stream) while removing the aggregate memory cost, and it is also exactly what a progress-reporting background job wants to consume.

### TR-RAG-021 — `IConnectorTransport` is GET-only, which forecloses the search APIs these sources offer — **OPEN**
- **Severity:** minor
- **Blocks:** no — every shipped connector works over GET, and tree truncation is reported as a partial run meanwhile; the work carried on.
- **Repro:** `IConnectorTransport` exposes a single `GetAsync(ConnectorHttpRequest, CancellationToken)`. There is no POST, no request body, and no method selector.
- **Expected:** enough of an HTTP seam to reach the endpoints a connector may need.
- **Actual:** GET only. This is sufficient for everything shipped today (repository trees and blobs, Confluence content listings are all GET), so it is not currently blocking. It does foreclose: Confluence CQL search via POST, GitHub's GraphQL API — which is the documented way to avoid the recursive-tree truncation `RepositoryConnector` currently reports as a per-item failure — and any future connector whose listing is a query rather than a path.
- **Encountered in:** REQ-RAG-032 / BRD-113, while handling GitHub tree truncation. A repository whose tree exceeds the host's single-response cap is currently reported honestly as a partial run, but cannot be *fixed* without either GraphQL or per-subtree walking.
- **Workaround:** none needed yet; the truncation is reported rather than silently ingesting a prefix of the repository.
- **Suggested fix:** widen to `SendAsync(ConnectorHttpRequest, CancellationToken)` with `Method` and optional `Body` on the request record, keeping `GetAsync` as a default-implemented convenience so no existing implementation breaks. Worth doing before a fourth connector rather than after.

### TR-RAG-022 — the answer to TR-RAG-020: a streaming run API also has to make cancellation non-lossy — **OPEN**
- **Severity:** major (data loss on cancellation; TR-RAG-020 filed the memory half of the same gap as minor)
- **Blocks:** no — the app-side `ObservedConnector` ingests each item as it is fetched, so a cancelled run keeps what it did; the work carried on.
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
- **Expected:** cancelling keeps what the run already achieved, as BRD-65 requires of a connector run and as `WebIngestionService` already promises for a crawl.
- **Actual:** `ConnectorRunner.RunAsync` returns documents, failures and sync state in one value at the end, so cancellation or a run-level `ConnectorException` throws all three away. `IngestConnectorAsync` ingests only after the walk, so a run stopped at item 480 of 500 ingests **zero** documents.
- **Encountered in:** REQ-FN-020 / BRD-65 (TechieDesk connector background jobs) — the consumer TR-RAG-020 said it was waiting for.
- **Workaround:** (shipped) `apps/TechieDesk.Core/Services/Connectors/ObservedConnector.cs` decorates `IDataConnector` and hands each fetched document to a sink that ingests it immediately; the caller rebuilds sync state for a cancelled run. Full description under Detail.
- **Suggested fix:** the `IAsyncEnumerable<ConnectorRunEvent>` overload from TR-RAG-020, carrying `ItemFetched`, `ItemFailed`, `ItemSkipped` and `Progress` events, with sync state readable incrementally and pruning kept as a completion-only step.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Expected:** cancelling keeps what the run already achieved. This is what `WebIngestionService` promises for a crawl ("cancelling keeps what was already ingested") and it is what BRD-65 requires of a connector run, because a cancelled import that ingested nothing is indistinguishable from one that never ran.
- **Actual:** `ConnectorRunner.RunAsync` returns its whole outcome — documents, failures and sync state — in one value at the end, so cancellation and any run-level `ConnectorException` throw all three away. `ConnectorIngestionExtensions.IngestConnectorAsync` compounds it: it calls `IngestTextAsync` only after `RunAsync` has returned, so a run stopped at item 480 of 500 ingests **zero** documents despite having downloaded 479 of them.
- **Workaround (shipped, and it is the seam TR-RAG-020 asked for):** `apps/TechieDesk.Core/Services/Connectors/ObservedConnector.cs` decorates `IDataConnector` itself rather than wrapping the runner. `ListAsync` reports the listing; `FetchAsync` reports the attempt, hands the fetched document straight to a per-item sink that ingests it immediately, records the per-item result with its reason, and rethrows any failure so the runner's own failure list and consecutive-failure breaker stay in step. Decorating the connector is the only seam that exists per item, and it is the same trick `ProgressReportingWebContentFetcher` plays on `IWebContentFetcher` for the crawler. Sync state for a cancelled run is reconstructed by the caller from the item versions it saw ingested and merged — never pruned — onto the previous state.
- **Suggested fix:** the `IAsyncEnumerable<ConnectorRunEvent>` overload TR-RAG-020 proposes is the right shape, and this entry adds two requirements to it that a memory-only framing would miss. (1) The stream must carry `ItemFetched` (document), `ItemFailed` (id, name, reason), `ItemSkipped` (id, name, reason — *distinct* from failed, see TR-RAG-023) and `Progress` (listed-so-far / attempted-so-far), so a caller can render live progress and per-item results without decorating anything. (2) Sync state must be readable **incrementally**, not only at completion — e.g. each `ItemFetched` carrying the id/version pair it would contribute — so a consumer that stops the stream keeps a correct, unpruned sync state. Pruning stale entries must remain a completion-only step, since only a full walk can tell "deleted at the source" from "not reached yet". With those two, `IngestConnectorAsync` can also become genuinely incremental instead of ingesting after the fact.

### TR-RAG-023 — `ConnectorItemFailure` conflates "we could not read this" with "we chose not to read this", and its message is culture-formatted
- **Severity:** minor
- **Blocks:** no — the app matches the `"Skipped:"` prefix meanwhile; the work carried on.
- **Repro:**
  ```csharp
  var options = new ConnectorRunOptions { MaxItemBytes = 2 * 1024 * 1024 };
  var result = await new ConnectorRunner().RunAsync(connector, null, options);
  // An oversized item and a 403 both arrive as ConnectorItemFailure. The only thing that tells
  // them apart is that ConnectorRunner prefixed one message with the literal string "Skipped: ".
  ```
- **Expected:** a deliberate policy skip and a genuine failure are different outcomes. A run that passed over one 400 MB database dump is a clean run; a run that got a 403 is a partial one. A consumer classifying run outcomes needs to tell them apart without parsing English.
- **Actual:** an oversized item and a 403 are both `ConnectorItemFailure` in one `Failures` list, told apart only by the literal prefix `"Skipped: "`; `Unchanged` is a third, differently-typed list. The size message uses the ambient culture (`90,00,000 bytes` on `en-IN`), and `ConnectorIngestionExtensions.BuildMetadata` is `private`.
- **Encountered in:** REQ-FN-020 / BRD-65. `ConnectorJobHandler.LibrarySkipPrefix` is a literal `"Skipped:"` match, which is exactly the kind of coupling that breaks on a wording change nobody thinks of as a breaking change.
- **Workaround:** match the prefix, and record anything else as a failure. It is correct today and will stay correct only by luck.
- **Suggested fix:** add a `ConnectorItemOutcome` enum (or one `Items` list of `ConnectorItemResult`), format sizes with `CultureInfo.InvariantCulture`, and make `BuildMetadata` public as `ConnectorIngestionExtensions.BuildDocumentMetadata(IDataConnector, ConnectorItem)`.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** both are `ConnectorItemFailure`, and `ConnectorRunResult` has one `Failures` list holding both. `ConnectorRunResult.Unchanged` is a third shape again (`IReadOnlyList<ConnectorItem>`, no reason attached, and empty unless `ReportUnchanged` is set), so a caller wanting "every item with the reason it was not ingested" has to merge two differently-typed lists and string-match a prefix on one of them. Two smaller things in the same message: `$"Skipped: {size:N0} bytes exceeds the {options.MaxItemBytes:N0}-byte limit"` uses the ambient culture, so on an `en-IN` machine an operator is told `90,00,000 bytes exceeds the 20,97,152-byte limit`; and `ConnectorIngestionExtensions.BuildMetadata` is `private`, so any caller that ingests per item (see TR-RAG-022) must re-implement the `SourceType`/`SourceName`/`SourceUrl`/`ItemId`/`Version`/`ModifiedUtc` key contract by hand and will silently drift from it.
- **Suggested fix:** add a `ConnectorItemOutcome` enum (`Failed` / `SkippedTooLarge` / `SkippedUnchanged` / `SkippedEmpty`) to `ConnectorItemFailure` — or, better, rename it `ConnectorItemResult` and give `ConnectorRunResult` a single `Items` list carrying every attempted item with its outcome and reason, which is the shape BRD-65 asks for and the shape every consumer builds by hand today. Format sizes with `CultureInfo.InvariantCulture` (or the caller's culture, explicitly chosen — not the ambient one). Make `BuildMetadata` public as `ConnectorIngestionExtensions.BuildDocumentMetadata(IDataConnector, ConnectorItem)` so per-item ingestion produces byte-identical metadata to the batch path.

### TR-RAG-024 — the vector store does not round-trip a document's metadata, so every ingestion route has to smuggle its source URL through `SourcePath` — ✅ **FIXED 2026-07-30**
- **Severity:** major (silent: the data is accepted, the count is right, and the field the user reads is blank)
- **Blocks:** no
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
- **Blocks:** no
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
- **Blocks:** no — an app-side item-to-document table replaces a superseded document on re-sync meanwhile; the work carried on.
- **Repro:**
  ```csharp
  var first  = await rag.IngestTextAsync("Runbook, first edition.",  "docs/runbook.md", metadata);
  var second = await rag.IngestTextAsync("Runbook, second edition.", "docs/runbook.md", metadata);
  // first != second, and BOTH documents are now in the catalogue and both are searchable.
  ```
- **Expected:** a caller re-ingesting the same logical item can say so. Everything the framework needs is already on the item — `ConnectorItem.Id` is documented as "stable identifier within the source; survives across runs" — and the metadata it writes already carries `ItemId`.
- **Actual:** every call mints a new document id and nothing maps a source item to the document it became, so a re-sync with three changed files took the catalogue from 9 documents to 12, with old and new text both searchable.
- **Encountered in:** REQ-RAG-019 / BRD-63. The library's own `IngestConnectorAsync` has the same defect — it calls `IngestTextAsync` per fetched document with no supersede step — so any caller using the supported batch path gets it too.
- **Workaround:** (shipped, app-side) a `ConnectorItemDocument` table (`ConnectorId`, `ItemId` → `DocumentId`) consulted by `RagConnectorDocumentSink`; the replacement is written first and `DeleteDocumentAsync` removes the superseded document. Full description under Detail.
- **Suggested fix:** an `externalId` (or `documentKey`) parameter on `IngestTextAsync` that replaces the existing document's chunks in place, with `ConnectorIngestionExtensions` passing `ConnectorItem.Id` scoped by `SourceName`.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** every call mints a new document id, and nothing in the connector framework maps a source item to the document it became. So the second sync of a repository whose three files had changed took the catalogue from 9 documents to 12 (verified by REQ-FN-020's cluster) and every search returned the superseded text alongside the current text, with nothing to distinguish them. Incremental sync makes this *worse*, not better: unchanged items are skipped, so the only items that ever duplicate are the ones the user actually edited.
- **Workaround (shipped, app-side):** a `ConnectorItemDocument` table (`ConnectorId`, `ItemId` → `DocumentId`) owned by TechieDesk, consulted by `RagConnectorDocumentSink` before each ingest; the replacement is written first and `DeleteDocumentAsync` removes the superseded document immediately after. Regression test `ConnectorEndToEndTests.ReSyncingAChangedFileReplacesItsDocumentInsteadOfDuplicatingIt`, mutation-proved RED (3 documents become 6 when the map is bypassed). It is a workaround, not a fix: every future consumer of `IngestConnectorAsync` has to build the same table.
- **Suggested fix:** an `externalId` (or `documentKey`) parameter on `IngestTextAsync` — unique per store, replacing the existing document's chunks in place when supplied — and have `ConnectorIngestionExtensions` pass `ConnectorItem.Id` scoped by `SourceName`. That makes the supported batch path correct by default and removes the per-caller table. Deleting-then-reingesting inside the library is an acceptable first implementation; the important part is that the decision stops being every caller's to rediscover.

### TR-RAG-027 — IMAP command injection: any caller-supplied string reaching a command line could become a second IMAP command
- **Severity:** major (security — arbitrary IMAP commands executed with the mailbox's credentials; defeats the connector's read-only guarantee)
- **Blocks:** no — the transport was fixed in the library during the same audit; the work carried on.
- **Repro:** a folder name carrying CRLF reaches `SELECT` unescaped:
  ```csharp
  var transport = new ImapMailTransport(() => connection, new ImapMailboxOptions { Host = "imap.example.test", ... });
  await transport.SearchAsync("INBOX\"\r\nT0099 STORE 1:* +FLAGS (\\Deleted)\r\nT0098 EXPUNGE", new MailSearchCriteria(), 0, 5);
  ```
  ```text
  T0002 SELECT "INBOX\"\r\nT0099 STORE 1:* +FLAGS (\Deleted)\r\nT0098 EXPUNGE"
  ```
  The server reads three lines; line 2 deletes every message and line 3 expunges them. Four more paths are under Detail.
- **Expected:** nothing a caller supplies can add a command.
- **Actual:** the values were interpolated straight into command text; `Quote()` escaped only `\` and `"`. The inputs come from connector configuration (folder list, filters, account name) — in TechieDesk, a workspace user — and an injected `STORE` defeats the read-only `BODY.PEEK` promise.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none available to a caller — the interpolation was inside the transport. Callers could only have sanitised every string themselves, without being told they had to.
- **Suggested fix:** (applied) `Quote(value, what)` refuses any control character; `RequireUid` requires a bare positive integer of at most 20 digits; `RunAsync` re-checks the composed command line. Pinned by `ImapCommandInjectionTests` (10 cases).

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

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
- **Suggested fix (applied):** `Quote(value, what)` now refuses any control character (`char.IsControl`) with an operator-facing reason naming which value was wrong, and is used for the folder name, account name, password and both search filters. Control characters rather than CR/LF alone, because U+0001 is the XOAUTH2 field separator and no folder, mailbox, account name or search term legitimately contains any of them — so refusing outright loses nothing and needs no escaping rules to be correct. `RequireUid` additionally requires a bare positive integer of at most 20 digits before a UID is spliced in, since UIDs are unquoted numbers in the protocol and no escaping would make an arbitrary string safe there. `RunAsync` re-checks the fully composed command line as a last resort, so a future call site that forgets cannot reintroduce the hole. Pinned by `ImapCommandInjectionTests` (10 cases), which assert the invariant — *no line put on the wire contains a line break* — rather than the absence of the word `STORE`, plus `StillSendsAnOrdinaryFolderName` so the guard is not simply refusing everything. Mutation-proved: neutering the control-character check turns the class red; separately neutering only `RequireUid` turns 3 of the 4 identifier cases red (the CRLF case survives on the choke-point guard, which is the point of having both).

### TR-RAG-028 — a server-declared IMAP literal length was honoured without a bound, so the server chose how much memory to allocate
- **Severity:** major (security — remote denial of service against the host application)
- **Blocks:** no — the transport was fixed in the library during the same audit; the work carried on.
- **Repro:**
  ```csharp
  // A hostile server's response to UID FETCH:
  "* 1 FETCH (UID 5 BODY[HEADER] {2000000000}"
  // ImapMailTransport.RunAsync -> TryReadLiteralLength -> pipe.ReadExactAsync(2_000_000_000)
  // SocketImapConnection.ReadExactAsync begins:  var result = new byte[count];
  ```
  Measured with a fake that records the requested count instead of allocating it: `largest ReadExactAsync request = 2,000,000,000 bytes`.
- **Expected:** the length is the server's *claim*, and it is acted on by allocating before a single byte has been read. A claim that large is a request to refuse, not to honour.
- **Actual:** allocated unconditionally (`{2147483647}` is accepted), in the host application's own memory, by any server that is attacker-run or compromised.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135). The pre-existing test `ReadsLiteralLengths` asserted `{2048}` parses to 2048; nothing asserted anything about an absurd value.
- **Workaround:** none — the read is inside the transport.
- **Suggested fix:** (applied) `ImapMailboxOptions.MaxMessageBytes` (default 64 MiB), spent as a per-response budget in `RunAsync` before `ReadExactAsync`. Tests are listed under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** allocated unconditionally. `{4294967295}` happens to be harmless only because `int.TryParse` fails and the line is then mis-framed instead; `{2147483647}` is accepted. The connector runs in-process in the host application, so this is that application's memory. Reaching it needs only a mailbox whose server is attacker-run — an operator pointing the connector at an address someone sent them — or legitimate and compromised.
- **Suggested fix (applied):** new `ImapMailboxOptions.MaxMessageBytes` (default 64 MiB, comfortably above the largest message any provider BRD-135 names will deliver) is spent as a *per-response* budget in `RunAsync`, checked before `ReadExactAsync` and decremented per literal so a stream of merely-large literals cannot add up past it either. Over-limit fails with an operator-facing reason that names the limit and the option to raise, rather than skipping the message silently. Pinned by `ImapHostileServerTests.RefusesALiteralLargerThanTheMessageBudget` (asserts nothing was ever *requested*: `LargestRead == 0`) and `RefusesLiteralsThatExceedTheBudgetInAggregate`. Mutation-proved RED.

### TR-RAG-029 — `ImapMailboxOptions.Timeout` had no effect whatsoever, so a hung mail server hung the run indefinitely
- **Severity:** major (availability — a false promise in a configuration knob)
- **Blocks:** no — the transport was fixed in the library during the same audit; the work carried on.
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
- **Actual:** the constructor set `TcpClient.ReceiveTimeout` / `SendTimeout`, which have no effect on asynchronous reads and writes. Nothing bounded the connect, the TLS handshake or any read, and every `IMailTransport` method defaults its `CancellationToken` to `None`.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** callers can pass their own `CancellationToken` with a deadline to every transport method. That works, but it is invisible from the option that claims to do it, and the option's XML doc said "connection timeout".
- **Suggested fix:** (applied) every connect, handshake, read and write is wrapped in a linked `CancellationTokenSource` with `CancelAfter(timeout)`; a fired deadline becomes a `ConnectorException`, while the caller's own cancellation is still rethrown. Tests are listed under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** the constructor set `TcpClient.ReceiveTimeout` / `SendTimeout`, **which have no effect on an asynchronous read or write** — they apply only to the synchronous socket APIs, and every read here is `SslStream.ReadAsync`. Nothing else bounded the connect, the TLS handshake, or any read. Every method on `IMailTransport` defaults its `CancellationToken` to `None`, so on the default call path there was nothing to end the wait at all. `SslStream.AuthenticateAsClientAsync` against a server that never sends a TLS hello blocked for the full 12s of the rescue token, which is as long as anyone is willing to wait.
- **Suggested fix (applied):** every connect, handshake, read and write is now wrapped in a linked `CancellationTokenSource` with `CancelAfter(timeout)`. A deadline that fires becomes a `ConnectorException` naming the host, the elapsed budget and the option to raise; the caller's *own* cancellation is still rethrown as `OperationCanceledException`, distinguished by `when (!cancellationToken.IsCancellationRequested)`, so a cancelled run is not misreported as a slow server. Pinned by `ImapHostileServerTests.StopsWaitingOnAServerThatSaysNothing` and `PropagatesTheCallersCancellation` (which pins the distinction), plus `ImapTransportSecurityTests.StopsWaitingOnASilentServer` against a real loopback socket. Both use a rescue token an order of magnitude beyond the configured timeout, so the test terminates when the fix is removed instead of hanging the suite. Mutation-proved RED at both sites.

### TR-RAG-030 — IMAP response accumulation was unbounded in both directions: line length and untagged-line count
- **Severity:** major (security — remote denial of service)
- **Blocks:** no — the transport was fixed in the library during the same audit; the work carried on.
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
- **Suggested fix:** (applied) `SocketImapConnection.MaxLineBytes` (64 KiB) and `ImapMailTransport.MaxResponseLines` (100,000); exceeding either drops the connection with an operator-facing reason. Tests are listed under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Suggested fix (applied):** `SocketImapConnection.MaxLineBytes` (64 KiB — RFC 3501 caps a *command* line at 8192 octets and no real response line approaches it) and `ImapMailTransport.MaxResponseLines` (100,000, two orders of magnitude above any real LIST/SEARCH/FETCH response). Exceeding either drops the connection with an operator-facing reason. The buffering was extracted into a new `internal ImapByteReader` so these behaviours can be driven over a stream a test owns — `SocketImapConnection` keeps the TLS invariant and delegates byte handling to a type that knows nothing about TLS and so cannot weaken it. Pinned by `ImapHostileServerTests.DropsAnEndlessResponseLine`, `GivesUpOnAServerThatNeverCompletesACommand` and `StillReadsAnOrdinaryLineAndLiteral` (so the bounds did not break ordinary framing). Both hostile fakes run dry eventually *only* so the tests terminate when the bound is removed. Mutation-proved RED at both sites.

### TR-RAG-031 — a 39 MB message decoded to 400,000 retained attachments, roughly ten times its own size in live objects
- **Severity:** minor (availability; bounded once TR-RAG-028 landed, but a 10x amplification an attacker picks)
- **Blocks:** no — the parser was fixed in the library during the same audit; the work carried on.
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
- **Suggested fix:** (applied) `MimeParser.MaxAttachments = 1000`, with `EmailConnector` recording `AttachmentsSkipped` on the item when the list is truncated. Tests are listed under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Suggested fix (applied):** `MimeParser.MaxAttachments = 1000`, far above any message a person composes. Where it bites, the body and the first 1000 files still parse, and `EmailConnector` records `AttachmentsSkipped = "attachment list truncated at 1000 (the message declared more parts than are read)"` on the item — BRD-65 asks for a reason an operator can act on for every skip, and a document quietly shorter than the message it came from is a skip nobody was told about. Pinned by `MimeParserHardeningTests.BoundsTheNumberOfAttachmentsOneMessageYields` and `EmailIngestionHardeningTests.ReportsWhenTheAttachmentListWasTruncated` / `ReportsNoTruncationForAnOrdinaryMessage`. Mutation-proved RED at both sites.

### TR-RAG-032 — the IMAP `SINCE` search key was formatted in the machine's culture, so incremental sync would be rejected outright on a non-English machine
- **Severity:** minor (correctness; would present as "incremental sync silently returns nothing", locale-dependent)
- **Blocks:** no — the transport was fixed in the library during the same audit; the work carried on.
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
- **Suggested fix:** (applied) `string.Create(CultureInfo.InvariantCulture, $"SINCE {since.UtcDateTime:dd-MMM-yyyy}")`. Pinned by `EmailIngestionHardeningTests.FormatsTheSinceKeyInTheProtocolsOwnCulture`, which swaps `CurrentCulture` to `fr-FR` for the assertion and restores it in a `finally`. Mutation-proved RED.

### TR-RAG-033 — mbox `>From ` unescaping handled only one level of quoting, so deeper quoted replies were indexed with a body the sender did not write
- **Severity:** minor (correctness — silent content corruption)
- **Blocks:** no — the mbox reader was fixed in the library during the same audit; the work carried on.
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
- **Suggested fix:** (applied) `IsEscapedFromLine` counts the leading `>` run and matches `From ` after it, removing exactly one `>`. Pinned by `MboxHardeningTests.UnescapesEveryDepthOfAnEscapedFromLine`, plus `SplitsOnAnUnescapedFromLine` which records that an *unescaped* `From ` line still splits the archive — that is the format's own ambiguity, not a defect here, and worth a test so nobody "fixes" it later. Mutation-proved RED.

### TR-RAG-034 — an attachment name kept its NTFS alternate-data-stream colon
- **Severity:** minor (security hardening; no exploitable path in the current tree)
- **Blocks:** no — the extension allow-list already rejects the name, and the parser was hardened in the same audit; the work carried on.
- **Repro:** `Content-Disposition: attachment; filename="report.pdf:evil.exe"` → `MailAttachment.FileName == "report.pdf:evil.exe"`.
- **Expected:** the name is reduced to something inert. Path traversal was already handled correctly — `../../etc/passwd` → `passwd`, `..\..\Windows\win.ini` → `win.ini`, `/etc/shadow` → `shadow`, `C:\Users\ada\secrets.pdf` → `secrets.pdf`, `....` → `attachment` — the colon was the one survivor.
- **Actual:** kept. Nothing in TechieRag writes attachments to disk, so it is not exploitable here, but a downstream component opening the name by path on Windows opens the stream rather than the file, and the extension allow-list is keyed off this string.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** the `AttachmentExtensions` allow-list already rejects `report.pdf:evil.exe`, because `Path.GetExtension` returns `.pdf:evil.exe`, which is not in the list. That is luck rather than intent.
- **Suggested fix:** (applied) `SafeFileName` now replaces `:`, `/` and any control character with `_` after reducing to the last path segment. Pinned as a `[Theory]` case in `MimeParserHardeningTests.ReducesAnAttachmentNameToABareFileName` alongside the five traversal cases. Mutation-proved RED.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** kept. Nothing in TechieRag writes attachments to disk, so this is not exploitable *here*: the value is handed to `IDocumentProcessor.ProcessAsync` as the document name and shown to a user. It is logged because on Windows a downstream component that does open the name by path opens the stream rather than the file, and the difference is invisible in every listing that shows the name — and because the extension allow-list is keyed off this string.

### TR-RAG-035 — `MimeParser` silently discards content past its depth cap and has nowhere to report it — **OPEN, needs a product decision**
- **Severity:** minor (correctness/observability; not a security defect — the cap itself is correct and works)
- **Blocks:** no — the depth cap itself works and nesting past 10 levels does not occur in ordinary mail, so the silence is recorded here pending a product decision; the work carried on.
- **Repro:**
  ```csharp
  // 500 nested multiparts with distinct boundaries, a text/plain part at the bottom:
  var parsed = MimeParser.Parse(deeplyNested);
  // No exception, no stack overflow, no attachments, and an empty body.
  // Nothing anywhere says "there was content and it was not read".
  ```
- **Expected:** BRD-65's rule — an operator-facing reason for every skip — applied to the parser's own bounds, the way TR-RAG-031's attachment truncation now is.
- **Actual:** `ReadPart` returns silently at `depth > 10`. The cap is right, but the message comes back with an empty body and no indication why, and the connector reports a successful document. Why it is not fixed here is under Detail.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none. Nesting past 10 levels does not occur in mail people send; the realistic trigger is a malformed or deliberately-constructed message, which is the case where knowing would matter most.
- **Suggested fix:** give `ParsedMailMessage` an `IReadOnlyList<string> Notes` (added last, defaulted empty) that `MimeParser` fills when it hits a bound, and have `EmailConnector` fold it into the item's skip reasons — or state the silence in the XML docs as a decided limit.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Actual:** `ReadPart` returns silently at `depth > 10`. The cap is *right* and it works (verified: 500 levels deep costs the nested content, not the stack, and no input in an 11-case malformed corpus made `Parse` throw). But the message comes back with an empty body and no indication why, and the connector reports a successful document.
- **Why this is not fixed here:** `ParsedMailMessage` is a public positional record with no diagnostics field, and `MimeParser` is a static class with no logger. Adding either is a breaking API change to a published SDK, and choosing between them — a `Notes`/`Diagnostics` collection on the record, an `out` parameter, an overload taking a callback, or accepting the silence for genuinely-rare input — is a product decision about the SDK's shape rather than a security fix. The TR-RAG-031 truncation reason was reportable only because `EmailConnector` could *infer* it from `Attachments.Count == MaxAttachments`; depth truncation leaves no such observable.
- **Suggested fix:** give `ParsedMailMessage` a `IReadOnlyList<string> Notes` (default empty, so existing positional construction keeps compiling only if it is added last and defaulted) and have `MimeParser` record "content nested deeper than 10 multiparts was not read" and any other bound it hits, then have `EmailConnector` fold `Notes` into the item's skip reasons the way it now folds attachment truncation. If that is too much surface, the alternative is to state the silence in the XML docs as a known limit — but it should be a decision, not an omission.

### TR-RAG-036 — informational: the HTTP connectors' SSRF guard must NOT be applied to the mail transport, and here is what was verified instead — **fixed upstream 2026-10-08, ships in the next TechieRag package after 1.1.2, not yet re-checked here**
- **Severity:** informational (no defect — recorded so a future audit does not "fix" this wrongly)
- **Blocks:** no — informational, nothing needed changing; the audit carried on.
- **Repro:** point the email connector at a mail host on a private address — `mail.corp.internal`, `10.x`, or `localhost:993` for a local Dovecot.
- **Expected:** the connection is allowed, because the operator names the mail host directly; what must hold is that nothing the server says can change the target.
- **Actual:** as expected — the transport connects only to the configured host, ignores IMAP `REFERRAL`, and requires TLS with platform certificate validation. The verifying tests are under Detail.
- **Encountered in:** security audit of `src/TechieRag/Connectors/Email/**` (REQ-RAG-049 / BRD-135).
- **Workaround:** none needed.
- **Suggested fix:** none. If a guard is ever wanted here it should be an operator-set *allow*-list of mail hosts, not an address-class deny-list.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Context:** TR-RAG-017 rightly required an SSRF guard on `HttpConnectorTransport`, and the audit that found it flagged that the email connector "has its own transport that does NOT go through the guarded handler". That is true, and copying the guard here would be a regression.
- **Why blanket blocking would be wrong:** an HTTP connector's target is derived from a URL that may have come from a response body, so an operator never intended `169.254.169.254`. A *mail host* is named directly by the operator and is legitimately arbitrary — corporate IMAP lives on private and link-local-adjacent addresses far more often than not (`mail.corp.internal`, `10.x`), and `localhost:993` is how anyone tests a local Dovecot. Refusing RFC 1918 targets would break the ordinary enterprise case while protecting nothing, because there is no untrusted party choosing the address.
- **The property that actually matters, and is now pinned:** the target cannot be changed by anything the *server* says. There is no redirect, no response-supplied host, and no second connection — verified by `ImapTransportSecurityTests.ConnectsOnlyToTheConfiguredHost`, which drives a whole run (authenticate → list → select → search → fetch) against a server whose greeting and tagged completion both carry `[REFERRAL imap://evil.example.invalid/]`, and asserts the connection factory was invoked exactly once and `MailboxName` is still the configured host. An IMAP `REFERRAL` is the closest thing this protocol has to a redirect and this client ignores it.
- **What carries the security instead:** TLS with platform certificate validation against the configured host name. Verified: `RefusesAPlaintextImapServer` points the real `SocketImapConnection` at a real loopback socket that answers with a genuine plaintext `* OK [CAPABILITY IMAP4rev1 STARTTLS] ready` — exactly what port 143 sends — and asserts the connection is refused at the handshake and that the bytes the client sent contain no `LOGIN`, no `STARTTLS` and no `CAPABILITY`. There is no STARTTLS code path to strip, no `RemoteCertificateValidationCallback`, no accept-all, and `ExposesNoWayToDisableCertificateValidation` reflects over every public property in the namespace to assert none is a certificate callback, an `SslClientAuthenticationOptions`, or a bool named for SSL/TLS/certificate/insecure — so the regression of someone adding an "ignore certificate errors" switch is caught by a test rather than by a review.

#### Fixed upstream 2026-10-08 (TechieRag)

- **Pinned by two tests, under `TechieRag#REQ-RAG-082` (re-Verified 2026-10-08).** No code changed; the mail transport already behaved as this entry says.
  - `REQ-RAG-082 MailHostOnPrivateAddressIsNotRefusedByTheWebGuard`: the real `SocketImapConnection` reaches a mail host on 127.0.0.1. The TCP connection is made and the client sends its TLS ClientHello (first byte `0x16`). The only refusal is the TLS one, never the web connectors' "private-network address" refusal.
  - `REQ-RAG-082 MailTransportNeverUsesTheWebSsrfGuard`: no source file under `Connectors/Email/` uses `CreateGuardedHandler`, `IsPrivateNetworkHost`, `IsPrivateNetworkAddress`, `HttpConnectorTransport` or `blockPrivateTargets`. A change that puts the deny-list on the mail transport fails this test, and its summary says why the right guard there is an operator-set allow-list.
  - **Verify from here:** nothing to change in Sevak. After moving to the new package, close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-036 "<what you ran and what it showed>"`.

### TR-RAG-037 — `EmailConnector` item identity embeds the **archive's file name**, so relocating an `.mbox` re-ingests the entire archive — ✅ **FIXED 2026-08-03**

- **Severity:** major (silent duplicate ingestion — costs re-embedding of a whole archive and pollutes the document library; the user sees every message twice with no error).
- **Blocks:** no
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
- **Blocks:** no
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

- **Severity:** minor (correctness of the tool list sent to the LLM; some providers reject a `tools` array with repeated names).
- **Blocks:** no — not hit in production, because the planner builds each tool name once; the work carried on.
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
- **Encountered in:** REQ-RAG-022 (BRD-84), wiring the six workspace skills into `AgentToolPlanner.BuildRegistry`.
- **Workaround:** (applied in TechieDesk) none needed; `AgentToolPlanner` filters by a permitted-name `HashSet` and the implementation list is built by a single factory, so a name cannot appear twice. Callers assembling tools from more than one source would need to de-duplicate themselves.
- **Suggested fix:** in `Register`, replace an existing definition rather than appending — e.g. find the index by `OrdinalIgnoreCase` name (the same comparer `handlers` uses) and overwrite, else append. Worth a test asserting `ToolDefinitions.Count == 1` after a repeat registration and that the *second* description is the one published.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Severity:** minor (correctness of the tool list sent to the LLM; no data loss, but the model is shown a duplicate function and providers differ in how they react — some reject a `tools` array with repeated names outright).
- **Encountered in:** REQ-RAG-022 (BRD-84), wiring the six workspace skills into `AgentToolPlanner.BuildRegistry`. Not hit in production — the planner composes its implementation list from `WorkspaceSkillTools.All`, which yields each catalogue name once — but the guarantee is the library's to make, not each caller's. A consumer that concatenates two implementation sources (say, workspace skills plus MCP tools that happen to share a name) gets a malformed tool list with no error.

### TR-RAG-040 — every SQLite-backed default in the library is a bare relative path, so a hosted consumer silently stores user data in whatever the process working directory happens to be

- **Severity:** major for any consumer not run from its own working directory; in TechieDesk it broke `codesign --verify` on the Release `.app`.
- **Blocks:** no — the app rewrites any relative `Data Source` into the per-user data directory meanwhile; the work carried on.
- **Repro:**
  ```csharp
  var config = new TechieRagConfig();
  config.VectorStore.ConnectionString;      // "Data Source=techierag.db"  — relative
  new TechieRagBuilder().UseSqliteVec();    // databasePath = "techierag.db" — relative
  ```
  Build the client from a host whose working directory is not writable-by-intent, initialize it, and a live SQLite database appears there.
- **Expected:** a default that cannot silently land somewhere the consumer did not choose: no default, or one under `Environment.SpecialFolder.LocalApplicationData`.
- **Actual:** `TechieRagConfig.cs:214` and `TechieRagBuilder.cs:233` default to `techierag.db`, resolved against `Environment.CurrentDirectory`. `ConnectionString` is non-nullable and pre-populated, so a consumer's `?? MyDefault()` never fires. On Mac Catalyst the working directory is the `.app` bundle root.
- **Encountered in:** REQ-FN-048 (BRD-130).
- **Workaround:** (applied in TechieDesk) `TechieDeskDb.DataDirectory.ResolveSqliteConnectionString` rewrites any relative `Data Source` at all three seams. Full description under Detail.
- **Suggested fix:** drop the default argument on `UseSqliteVec`, make `ConnectionString` nullable, and at minimum document the working-directory default here and on `SqliteConversationStore` / `SqliteWorkspaceStore`.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Severity:** major for any consumer that is not a console app run from its own working directory. In TechieDesk it corrupted the shipped artefact: `codesign --verify --deep --strict` on the Release `.app` failed with *"unsealed contents present in the bundle root"* and the signed bundle could not be produced until the file was deleted by hand.
- **Expected:** a default that cannot silently land somewhere the consumer did not choose. Either no default at all (make the path a required argument on `UseSqliteVec`, which fails loudly at the call site) or a default anchored to a location the library can defend (`Environment.SpecialFolder.LocalApplicationData`), with the relative form still accepted when a caller passes one explicitly.
- **Actual:** `src/TechieRag/TechieRagConfig.cs:214` defaults `VectorStoreConfig.ConnectionString` to `"Data Source=techierag.db"` and `src/TechieRag/TechieRagBuilder.cs:233` defaults `UseSqliteVec(string databasePath = "techierag.db")`. Both resolve through SQLite against `Environment.CurrentDirectory` at open time. Because `ConnectionString` is **non-nullable and pre-populated**, a consumer's defensive `savedConfig.VectorStore.ConnectionString ?? MyDefault()` can never fire — the relative literal is not null, so it flows straight through. That is precisely how it survived in TechieDesk: the null-coalesce read as a guard and was dead code.
- **Why it is worse than it looks on a GUI host.** On Mac Catalyst, UIKit **resets the process working directory to the `.app` bundle root** at launch, discarding whatever the parent process set (measured with `lsof -d cwd`: cwd = `…/TechieDesk.app`). So the relative default did not depend on how the app was started — it resolved into the signed bundle on *every* launch.
- **Workaround applied in TechieDesk:** the app never lets a relative path reach the library. `TechieDeskDb.DataDirectory.ResolveSqliteConnectionString` rewrites any relative `Data Source` into the per-user data directory, and it is applied at all three seams — reading the saved configuration, writing it, and building the `TechieRagBuilder` in `TechieRagManager`. Documented in `apps/TechieDesk.Core/Services/TechieRagConfigService.cs`.
- **Suggested fix:** at minimum, document on `UseSqliteVec` and `VectorStoreConfig.ConnectionString` that the default is working-directory relative and unsuitable for GUI or service hosts. Better: drop the default argument on `UseSqliteVec` so the choice has to be made, and make `ConnectionString` nullable so a consumer's `?? MyDefault()` works as it reads. The same note applies to `SqliteConversationStore` and `SqliteWorkspaceStore`, whose XML docs advertise the same relative example.

### TR-RAG-041 — `McpWorkspaceTools` loses the tool→server mapping, so a host cannot apply a per-server policy to MCP tools without re-deriving it from the qualified name — ✅ **FIXED 2026-08-01**

- **Severity:** minor-to-moderate — the server each tool came from, needed for a per-server security decision, is not returned.
- **Blocks:** no — the app matched the `{server}-` prefix meanwhile, resolving ambiguity towards gating; the work carried on.
- **Repro:**
  ```csharp
  var tools = await registry.BuildWorkspaceToolsAsync(workspaceId, policy, localTools, loggerFactory);
  tools.StartedServers;                 // ["ledger", "local-index"]  — names only
  tools.ToolHandler.ToolDefinitions;    // ["ledger-lookup", "local-index-search"] — qualified names only
  // Nothing relates the two, and nothing returns the raw McpToolDescriptor list per server.
  ```
- **Expected:** `McpWorkspaceTools` exposes which tools came from which server, e.g. `ToolsByServer`.
- **Actual:** only `ToolHandler`, `StartedServers` and `Failures`; `McpToolHandler` keeps its bindings private and `FromDiscovered` is `internal`.
- **Encountered in:** REQ-RAG-023 (BRD-86), TechieDesk — applying `EgressGate` (REQ-NFR-013) only to tools of `McpTransportKind.Http` servers.
- **Workaround:** (applied in TechieDesk) `apps/TechieDesk.Core/Services/Agents/Mcp/McpEgressGuard.cs` matches the `{server}-` prefix. Full reasoning under Detail.
- **Suggested fix:** add `ToolsByServer` to `McpWorkspaceTools`, or expose `McpToolHandler.ServerNameFor(string qualifiedToolName)`.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Severity:** minor-to-moderate. Nothing is incorrect; the information a host needs to make a *security* decision per server is simply not returned, so the host has to reconstruct it from a string and reconstruct it conservatively.
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
- **Blocks:** no — the builder overwrites `Name` with its own localized label meanwhile; the work carried on.
- **Repro:**
  ```csharp
  var node = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition);
  Console.WriteLine(node.Name);   // "Condition"
  ```
- **Expected:** either the node arrives with `Name` empty (so the host names it), or the pre-filled
  name is documented as an English placeholder the host is expected to replace.
- **Actual:** `CreateNode` sets `Name = Describe(kind).DisplayName`, the English label. TechieDesk's **Branch** button added a step labelled **Condition**, and in Hindi the English name is stored in `DefinitionJson`.
- **Encountered in:** REQ-UI-040 (BRD-92), 2026-08-01.
- **Workaround:** (applied) the builder overwrites `Name` with its own localized palette label
  immediately after `CreateNode` returns, in both the new-flow seed and the add-step path.
- **Suggested fix:** leave `Name` empty in `CreateNode` and let `FlowNode.DisplayName` — which already
  falls back to `Id` — carry the "unnamed" case, or add an optional `name` parameter alongside the
  existing `id` one. The XML doc's "with its kind's display name pre-filled" should then say whether
  a host is expected to replace it.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

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

### TR-RAG-043 — a builder cannot tell whether a flow will call a model without re-deriving it from the catalogue (nice-to-have, found 2026-08-01 during REQ-UI-040)

- **Severity:** nice-to-have. Nothing is wrong; the fact is derivable and the derivation is two lines.
- **Blocks:** no — the app derives the answer itself with `FlowCapabilities.NeedsLlmProvider(flow)`; the work carried on.
- **Repro:** a host wants to answer "can I run this flow on an install with no LLM provider
  configured?". `FlowDefinition` exposes nodes and edges; `FlowNodeKindDescriptor.UsesLlm` exposes the
  per-kind answer. Joining them is the host's job.
- **Expected:** a `FlowDefinition` can say directly whether running it calls a model.
- **Actual:** the host has to join the flow's nodes with `FlowNodeKindDescriptor.UsesLlm` itself; TechieDesk's first cut got it wrong and refused a branch-and-end flow with "no LLM provider is configured".
- **Encountered in:** REQ-UI-040, 2026-08-01.
- **Workaround:** (applied) `FlowCapabilities.NeedsLlmProvider(flow)` app-side, derived from
  `FlowNodeCatalog.Kinds` so a future node kind brings its own answer.
- **Suggested fix:** a `bool UsesLlm` (or `IReadOnlyList<FlowNode> LlmNodes`) computed property on `FlowDefinition`, next to `ResolveStartNode()` and `EdgesFrom()`.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- Filed because getting it *wrong* costs a false refusal, and TechieDesk shipped one for an afternoon.
- **What went wrong here:** TechieDesk's first cut demanded a provider for *every* run and refused a
  branch-and-end flow — which costs no tokens and makes no request — with "no LLM provider is
  configured". Found by running it on the Catalyst head, not by the suite.
- **Suggested fix:** a `bool UsesLlm` (or `IReadOnlyList<FlowNode> LlmNodes`) computed property on
  `FlowDefinition`, derived from the catalogue exactly as a host would derive it. It belongs next to
  `ResolveStartNode()` and `EdgesFrom()`, which are the same shape of convenience — facts about the
  graph the library already knows and every host would otherwise re-implement. The same accessor would
  let a builder show a "this flow costs tokens" badge without enumerating kinds.

### TR-RAG-044 — raw SentencePiece ids are fed to an XLM-RoBERTa graph, so every token is off by one — ✅ **FIXED in the reranker 2026-08-03, and in the embedder 2026-08-04**

- **Severity:** major. In the reranker it made cross-lingual ranking worse than random. In `EmbeddedEmbeddingProvider` — the desktop default, and the provider behind every stored vector — it is **still present**.
- **Blocks:** no
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
- **Blocks:** no — nothing depended on the reranker, which had never been executed (REQ-RAG-025), and the download path was repaired in the library on 2026-08-03; the work carried on.
- **Repro:** on a machine without a pre-staged model, use `OnnxCrossEncoderReranker` so it downloads its model on first run.
- **Expected:** the first run downloads a complete, usable ONNX model once, and later runs see it as downloaded.
- **Actual:** it could never succeed: `onnx/model.onnx` under `DefaultBaseUrl` returned HTTP 404, the 2.27 GB `model.onnx_data` sidecar was never in `ModelFiles`, and `IsModelDownloaded()` was false by construction. A truncated download was also accepted as complete.
- **Encountered in:** REQ-RAG-025 (the reranker "has never been executed").
- **Workaround:** none — the download path is inside the library.
- **Suggested fix:** (applied 2026-08-03) point `DefaultBaseUrl` at `onnx-community/bge-reranker-v2-m3-ONNX`, add `model.onnx_data` to `ModelFiles`, check the sidecar's size in `IsModelDownloaded()`, and require about the expected size on resume. The full account is under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Three independent faults, all in the download path:**
  1. **404.** `DefaultBaseUrl` pointed at `BAAI/bge-reranker-v2-m3`, which publishes PyTorch weights and **no ONNX export at all** — `onnx/model.onnx` returns HTTP 404. Now `onnx-community/bge-reranker-v2-m3-ONNX` (Hugging Face's own conversion org), whose layout matches what the file list already expected: `onnx/model.onnx` at 2,271,088,656 bytes against the 2,270,000,000 the code already carried as its approximate size. The code was written against that repo; only the URL pointed elsewhere.
  2. **The weights were never downloaded.** An ONNX graph above 2 GB cannot fit in one protobuf, so the export is a 656 KB `model.onnx` stub plus a 2.27 GB `model.onnx_data` sidecar. `ModelFiles` listed only the stub. `EmbeddedEmbeddingProvider` has always fetched its own `model.onnx_data`; this list simply omitted it.
  3. **`IsModelDownloaded()` was false by construction.** It required `model.onnx` to exceed 1 GB — never true for an external-data export, where that file is the stub. Even a perfect download reported "not downloaded", so the class would have re-fetched 2.27 GB on every call. Now checks the sidecar's size, as the embedder always has.
- **Also fixed:** the resume check accepted any non-empty file as complete, so a truncated 2.27 GB download was treated as finished and failed later inside `InferenceSession` with nothing pointing back at the download. It now requires ~the expected size.
- **A second repo is required and that is not a mistake:** the ONNX export ships `tokenizer.json` but no `sentencepiece.bpe.model`, which this class needs; that one file comes from BAAI's official repo. Neither repo alone can satisfy the download.
- **Regression cover:** `LiveRerankerDownloadUrlTests` range-requests the first byte of each composed URL — the whole class costs a few kilobytes and would have caught all of this immediately. A mirror override (`TECHIERAG_RERANKER_BASE_URL`) was added for parity with the embedder, per REQ-NFR-008.

### TR-RAG-046 — the mail connector can only read; it cannot move, label or put a message in Trash — **fixed upstream 2026-10-03, not yet re-checked here**

> ✅ **Closed 2026-10-07** — re-checked here: 2026-10-07 on TechieRag 1.1.2: Sevak.Tests MailManageTests (8 tests named REQ-RAG-056) pass — the morning pass in manage mode runs ImapMailActions.DryRunAsync then ApplyAsync over the mailbox's saved IMAP connector and credential-store password against a real in-process IMAP server over TLS: move to an existing folder, Gmail label (X-GM-LABELS), Trash to [Gmail]/Trash or the \Trash folder; dry run sent only EXAMINE/SEARCH; LabelsNotSupported and TrashFolderNotFound came back per message with their codes; message count unchanged (nothing deleted). Full suite 1885 passed, 0 failed.

- **Severity:** major
- **Blocks:** no — Sevak's "advise" mode works on today's read-only connector; only "manage" (Sevak REQ-RAG-056, BRD-161) waits for this.
- **Repro:** the connector's settings in Sevak carry host, port, user, folders and filters, and nothing that acts on a message:
  ```text
  apps/Sevak.Core/Services/Connectors/ConnectorSettings.cs:106-155  (ImapHost … MailMaxAttachmentBytes; read options only)
  ```
- **Expected:** on the same IMAP login used for reading (password, Gmail app password, or XOAUTH2 token), a caller can move a message to a folder, add or remove a Gmail label, and move a message to Trash, and get back what was done per message.
- **Actual:** reading only.
- **Encountered in:** `*amend-docs Sevak` of 2026-10-02 (brief line 16; Sevak ADR-024).
- **Workaround:** none needed for v0.1 advise mode; manage mode is not built until this ships.
- **Suggested fix:** add move, label and trash operations to the mail connector over the existing IMAP session, with a dry-run that returns the planned actions; ledger it as a TechieRag requirement paired with `Sevak#REQ-RAG-056`.
- **Fixed upstream 2026-10-03 in TechieRag (`TechieRag#REQ-RAG-110` / BRD-169, `TechieRag#REQ-RAG-111` / BRD-170), both Verified, including a live run against a real Gmail account.** Ships in the next TechieRag package after 1.0.8; manage mode can start once Sevak moves to that version.
  - **What it is:** a new type, `TechieRag.Connectors.Email.ImapMailActions`, separate from the reader. `EmailConnector` / `ImapMailTransport` stay read-only (they now refuse any mailbox-changing command before it is sent), so advise mode cannot change a mailbox even by mistake. Manage mode uses `ImapMailActions` on the same login settings.
  - **How Sevak calls it:**
    ```csharp
    using TechieRag.Connectors.Email;

    // Same ImapMailboxOptions the reader uses. Gmail: app password. Outlook / Microsoft 365 or a Gmail
    // OAuth token: UseOAuthBearer = true and the access token in Password (XOAUTH2).
    var mailActions = ImapMailActions.Create(new ImapMailboxOptions
    {
        Host = "imap.gmail.com", Username = user, Password = appPasswordOrToken, UseOAuthBearer = false,
    });

    // A message the reader ingested: ConnectorItem.Id → reference (or MailMessageRef.FromHeader(header)).
    var message = MailMessageRef.FromConnectorItemId(item.Id);

    MailAction[] actions =
    [
        MailAction.Move(message, "Receipts"),          // the folder must already exist; never created
        MailAction.AddLabel(message, "Sevak/Bills"),   // Gmail only
        MailAction.RemoveLabel(message, "Sevak/Todo"), // Gmail only
        MailAction.Trash(otherMessage),                // to the Trash folder; never a permanent delete
    ];

    // Show the owner first: one plan per message, and nothing on the server changes.
    IReadOnlyList<MailActionPlan> plans = await mailActions.DryRunAsync(actions, cancellationToken);
    // plan.Outcome, plan.Strategy (Move | CopyAndExpunge), plan.TargetFolder (e.g. "[Gmail]/Trash"),
    // plan.Commands (the exact IMAP commands), plan.Code / plan.Reason when it would be refused.

    // After the owner approves:
    IReadOnlyList<MailActionResult> results = await mailActions.ApplyAsync(actions, cancellationToken);
    foreach (var result in results)
    {
        // result.Outcome: Done | Skipped | Failed, one per action, in order. result.Code is a
        // MailActionCodes constant: render it in Sevak's language, do not show result.Reason raw.
    }
    ```
    Shortcuts for one kind of action on many messages: `MoveAsync(messages, folder)`, `TrashAsync(messages)`, `AddLabelAsync(messages, label)`, `RemoveLabelAsync(messages, label)`.
  - **What the codes mean for the screen** (`MailActionCodes`): `LabelsNotSupported` — not Gmail (Outlook has no labels over IMAP; offer Move instead). `MoveNotSupported` — the server has neither MOVE nor UIDPLUS; nothing was changed. `TrashFolderNotFound`, `TargetFolderNotFound`, `FolderNotFound` — a folder is missing. `MessageNotFound` — already moved or deleted elsewhere. `UidValidityChanged` — the folder was rebuilt on the server; re-sync before acting. `AlreadyInFolder` (Skipped) — nothing to do. `CopiedNotRemoved` — copied to the target but the original stayed (tell the owner; no mail is lost). `ServerRefused`, `ConnectionLost`, `InvalidArgument` — the rest.
  - **Safety Sevak can rely on:** one failing message never stops the others; plain `EXPUNGE` is never sent, so other messages the user marked deleted are never purged; folder and label names carrying line breaks are refused before they reach the server; each call opens and closes its own connection.
  - **Verify from here:** after moving Sevak to the new package, run Sevak's REQ-RAG-056 manage-mode test (or one `DryRunAsync` + `ApplyAsync` on a test mailbox), then close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-046 "<what you ran and what it showed>"`.

### TR-RAG-047 — a host cannot tell before the download whether the local model will run or fit here, and the memory check runs only after the download (minor, found 2026-10-05 during Sevak#REQ-RAG-054) — **fixed upstream 2026-10-07, ships in the next TechieRag package after 1.1.2, not yet re-checked here**

- **Severity:** minor
- **Blocks:** no — Sevak's model check offers the built-in model on its own whole-computer memory figures (8 / 4 / 2 GB, the figures in welcome.html) and shows the library's refusal with its reason when the load fails.
- **Repro:** in TechieRag.Local 1.0.8 the memory estimate, the free-memory reading and the platform check are all `internal`, and `LocalLlmProvider.LoadAsync` runs its memory gate after the download. The member list is under Detail.
- **Expected:** before any byte is requested, a host can read whether this platform has a local runtime, the free memory a model needs and the free memory the library measures; `LoadAsync` refuses a model that cannot fit before downloading it; download progress is reported per provider.
- **Actual:** an Intel Mac learns it cannot run the model from `PlatformNotSupportedException` after pressing accept; a machine short of free memory learns it from `LocalModelMemoryException` after the full download.
- **Encountered in:** `Sevak#REQ-RAG-054` (BRD-156), 2026-10-05, moving Sevak to TechieRag 1.0.9 / TechieRag.Local 1.0.8 (`apps/Sevak.Core/Services/LifeManager/Assistant/LocalModelService.cs`, `ModelCheckService.cs`).
- **Workaround:** none built. Sevak does not re-implement the memory estimate or the platform list; it shows the catalogue's whole-computer figure before the download and the library's reason after a refusal.
- **Suggested fix:** make the estimate, the runtime check and the memory reading public; gate memory before `EnsureAsync`; add a per-provider download progress option. Ledger it as a TechieRag requirement paired with `Sevak#REQ-RAG-054`. The exact members are under Detail.

#### Detail

Moved here unchanged from the report above, which keeps a short form of each field.

- **Repro:** TechieRag.Local 1.0.8, everything a host would need to answer "can this computer run this model?" before pressing download is `internal`:
  ```text
  LocalModel.EstimateMemoryBytes(long, int)   internal   (weights + context cache + 256 MB runtime)
  LocalModel.KvBytesPerToken / Variants       internal   (so the weight size is not readable either)
  AvailableMemory.Read() / MemoryGate         internal   (free memory as the library measures it)
  LocalRuntimeSelector.IsSupported(...)       internal   (no runtime on an Intel Mac)
  ```
  And in `LocalLlmProvider.LoadAsync` the memory gate runs after `store.EnsureAsync(...)`, so a catalogue model's 2.7 GB is downloaded before `LocalModelMemoryException` says it will not load.
- **Expected:** before any byte is requested, a host can read (a) whether this platform has a local runtime, (b) the free memory a model needs at a given context size (known up front for a catalogue model, after `GetTermsAsync` for a Hugging Face one), and (c) the free memory the library measures; and `LoadAsync` refuses a model that cannot fit before downloading it. Download progress for one provider, not only the process-wide `ModelDownloadService.Instance.ProgressChanged` that the embedder's own download also raises.
- **Actual:** an Intel Mac learns it cannot run the model from `PlatformNotSupportedException` after pressing accept; a machine short of free memory learns it from `LocalModelMemoryException` after the full download. Progress has to be filtered from the shared event by the model id.
- **Suggested fix:** make public `LocalModel.EstimateRequiredMemoryBytes(int? contextSize = null)`, `LocalLlmProvider.IsRuntimeAvailable` (or `LocalModel.IsSupportedOnThisPlatform`) and `AvailableMemory.Read()`; in `LoadAsync`, run the memory gate before `EnsureAsync` whenever the weight size is known; add `LocalLlmOptions.DownloadProgress` (`IProgress<ModelDownloadProgress>?`) raised only for this provider's download. Ledger it as a TechieRag requirement paired with `Sevak#REQ-RAG-054`.

#### Fixed upstream 2026-10-07 (TechieRag)

- **Built as suggested, as `TechieRag#REQ-RAG-125` (Verified 2026-10-07).** Ships in the next TechieRag package after 1.1.2, all five packages together.
  - `LocalLlmProvider.IsRuntimeAvailable` (static): false on an Intel Mac or in a browser, with nothing loaded or downloaded.
  - `LocalModel.EstimateRequiredMemoryBytes(int? contextSize = null)`: weights + context cache + 256 MB, the same figure the load checks. Pass `provider.ContextSize` to match a configured provider. It returns null when the weight size is not known yet (a Hugging Face model before `GetTermsAsync`, a host folder that does not exist). For a Hugging Face model the context cache size comes from its `genai_config.json`, which arrives with the download, so before then the figure is a lower bound.
  - `AvailableMemory.Read()` is public: the free memory the load itself checks, or null.
  - `LoadAsync` now refuses a model that cannot fit with `LocalModelMemoryException` **before the terms are asked and before the first byte**. It checks again after the download.
  - `LocalLlmOptions.DownloadProgress` (`IProgress<ModelDownloadProgress>?`) receives this provider's download alone, never the embedder's. Each report is a copy, because the shared service updates one object in place.
  - **Tests:** `REQ-RAG-125 CatalogueModelMemoryNeedIsKnownBeforeDownload`, `MissingFolderModelHasNoEstimate`, `RuntimeAndFreeMemoryArePublic`, `ShortMemoryRefusesBeforeFirstByte` (no request sent, terms callback never called), `DownloadProgressReportsOnlyThisProvider` (a second model downloading at the same time is never reported).
  - **Verify from here:** after moving Sevak to the new package, call `IsRuntimeAvailable`, `EstimateRequiredMemoryBytes(provider.ContextSize)` and `AvailableMemory.Read()` from `ModelCheckService` with the network off, and confirm a load on a machine short of memory throws before any download. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-047 "<what you ran and what it showed>"`.

### TR-RAG-048 — TechieRag 1.0.9's net10.0 assembly is versioned 1.0.0.0, so TechieRag.Local / TechieRag.Embedded 1.0.8 cannot load beside it (blocker, found 2026-10-05 during the phase-3 smoke) — **fixed upstream 2026-10-06, ships as 1.1.2, not yet re-checked here**

> ✅ **Closed 2026-10-07** — re-checked here: 2026-10-07: pinned TechieRag, TechieRag.Embedded, TechieRag.Local 1.1.2 (GitHub Packages). Read every lib/*/ dll with [Reflection.AssemblyName]::GetAssemblyName and ReflectionOnlyLoadFrom(...).GetReferencedAssemblies() on copies in tests/.artifacts/tr112/ (stamps.txt): TechieRag net10.0 and net8.0 1.1.2.0, TechieRag.Embedded net10.0 1.1.2.0 -> TechieRag 1.1.2.0, TechieRag.Local net10.0 1.1.2.0 -> TechieRag 1.1.2.0 and TechieRag.Embedded 1.1.2.0; file versions 1.1.2+a4c3bff8. tf-build build Sevak.slnx PASS (Windows rung); tests 1838 passed, 0 failed, 3 skipped (Linux rung). boot-safe.sh tr112 booted the Windows head on tests/.artifacts/smoke-data/20261007T135701Z-tr112/data; after 60 s the app log and data/logs/sevak-20261007.log hold no 'Could not load file or assembly' and no ERR/WRN line; CDP reached /today (today-page visible, no blazor-error-ui; today.png). Two 1.1.2 changes needed Sevak fixes, see TR-RAG-049.

- **Status:** fixed upstream 2026-10-06 in TechieRag (`TechieRag#REQ-FN-071`, Verified). It ships as 1.1.2 once the TechieRag owner publishes the `v1.1.2` release; until then Sevak stays at 1.0.8. The cause, the fix, the new pack-time check and the close step are under "Fixed upstream 2026-10-06" at the end of this entry.
- **Severity:** blocker
- **Blocks:** yes — Sevak#REQ-RAG-056 (manage mail) and Sevak#REQ-RAG-053 (agents) need 1.1.1, which cannot load on net10.0; the rest of the build carried on at 1.0.8.
- **Repro:** read the assembly versions in the packages:
  ```text
  techierag/1.0.9/lib/net10.0/TechieRag.dll   AssemblyVersion 1.0.0.0   (file 1.0.0+057b624f…)
  techierag/1.0.9/lib/net8.0/TechieRag.dll    AssemblyVersion 1.0.9.0
  techierag/1.0.8/lib/net10.0/TechieRag.dll   AssemblyVersion 1.0.8.0
  techierag.local/1.0.8, techierag.embedded/1.0.8   reference TechieRag 1.0.8.0
  ```
  A net10.0 host with TechieRag 1.0.9 + TechieRag.Local 1.0.8 + TechieRag.Embedded 1.0.8 starts and logs `System.IO.FileNotFoundException: Could not load file or assembly 'TechieRag, Version=1.0.8.0'` from `TechieRagManager.CreateInstanceFromConfigAsync` (the runtime refuses an assembly whose version is lower than the reference).
- **Expected:** every target in one package carries the package's version (1.0.9.0), and sibling packages that ship together bind to it.
- **Actual:** the net10.0 build of 1.0.9 was packed without the version property (1.0.0.0), so it is older than what its own siblings reference.
- **Encountered in:** Sevak phase-3 smoke, 2026-10-05, Windows head `apps/Sevak` (Debug, win-x64), fresh data folder `tests/.artifacts/smoke-data/20261005T094133Z/data` — log `logs/sevak-20261005.log` there. The app then exits with `0xc000027b` in `Microsoft.UI.Xaml.dll` (Windows Application log, 15:15:28, 15:16:28, 15:20:20, 15:21:28 local), each time within a second of the startup-failure notice being dispatched.
- **Workaround:** all three packages pinned at 1.0.8 in `Directory.Packages.props` (2026-10-05); the 1.0.9 changes Sevak wanted (REQ-RAG-088, REQ-RAG-107) wait for a correctly stamped release. See Detail on the crash.
- **Suggested fix:** republish as 1.0.9.x (or 1.0.10) with `AssemblyVersion` set for every target framework, and ship TechieRag.Local / TechieRag.Embedded at the same version; add a pack-time check that every `lib/*/TechieRag.dll` carries the package version.

#### Detail

- **The `0xc000027b` exit is not this library's defect.** The load failure only triggered it: Sevak showed its startup-failure notice before the window had a XamlRoot, and WinUI's dialog failure escaped MAUI's alert handler. Fixed in Sevak on 2026-10-05 (`StartupFailureNotice` waits for the window); a failed start now opens with the notice instead of exiting.


#### Still present in 1.1.1 (checked 2026-10-05)

Downloaded from GitHub Packages and read with .NET's assembly reader: `lib/net10.0/TechieRag.dll` is 1.0.0.0 and `lib/net10.0/TechieRag.Embedded.dll` is 1.0.0.0 (their net8.0 copies are 1.1.1.0), while `TechieRag.Local.dll` and `TechieRag.Agents.dll` 1.1.1.0 reference `TechieRag` 1.1.1.0. A net10.0 host therefore fails to load TechieRag the same way. This holds Sevak at 1.0.8 and keeps mail actions (TR-RAG-046, `ImapMailActions`, present in 1.1.1) and `TechieRag.Agents` (Sevak#REQ-RAG-053) out of reach until a correctly stamped release.

#### Fixed upstream 2026-10-06 (TechieRag)

- **Fixed in TechieRag's publishing workflows (`TechieRag#REQ-FN-071`, Verified 2026-10-06). The fix ships as 1.1.2. It reaches GitHub Packages when the TechieRag owner publishes the `v1.1.2` GitHub Release, and nuget.org after the manual dispatch of `publish-nuget.yml` on that tag.** Do not move Sevak until 1.1.2 is on the feed. 1.0.9, 1.1.0 and 1.1.1 stay broken on net10.0 and are not republished, because a published version is immutable.
  - **Cause:** the internal-feed workflow (`publish-github-packages.yml`, the feed Sevak restores from) built every project with `-p:Version`, then ran `dotnet test` *without* it. That test run rebuilt the net10.0 outputs of the projects the test project references (TechieRag, TechieRag.Embedded, TechieRag.Telemetry) at the csproj's standing 1.0.0, over the release build. `dotnet pack --no-build` then packed what was on disk. net8.0 and the projects the tests do not reference (TechieRag.Local, TechieRag.Agents) were not rebuilt, which is why they carried the right number and referenced a `TechieRag` 1.x.x.0 that the package did not contain. The package file names were right, so nothing in the run noticed.
  - **Also affected (not in the original report):** `lib/net10.0/TechieRag.Telemetry.dll` was 1.0.0.0 in the same releases.
  - **Fix:** both workflows now build the test project in the Build step with the release version and run `dotnet test --no-build`, so nothing rebuilds after the release build. Every target is now stamped with the package version.
  - **Pack-time check (new):** a `Check assembly versions` step runs after Pack and before any push or OIDC login, on both feeds. It runs `.github/workflows/scripts/check-package-assembly-versions.cs`, which opens every packed `lib/*/TechieRag*.dll` and fails the run unless its AssemblyVersion is `<version>.0` and every TechieRag-to-TechieRag reference binds to that same number. Two structural tests keep it in place: `REQ-FN-071 NoWorkflowStepRebuildsWithoutTheVersion` and `REQ-FN-071 BothWorkflowsCheckAssemblyVersionsBeforePush`.
  - **Evidence (replayed locally on 2026-10-06 at version 1.1.2, the workflow's exact build → test → pack sequence):** before the fix, the check printed `FAIL 5 mismatch(es) across 8 assemblies`. net10.0 TechieRag, Embedded and Telemetry were 1.0.0.0, and Embedded and Telemetry referenced TechieRag 1.0.0.0, which reproduces this entry. After the fix it printed `PASS 8 assemblies in 5 packages all carry 1.1.2.0`. All five packages (TechieRag, .Embedded, .Telemetry, .Agents, .Local) ship together at one version.
  - **Verify from here:** after 1.1.2 is on GitHub Packages, set all TechieRag pins in `Directory.Packages.props` to 1.1.2. Boot the Windows head on a fresh data folder and confirm startup logs no `Could not load file or assembly 'TechieRag'`. Optionally run `dotnet run <TechieRag repo>/.github/workflows/scripts/check-package-assembly-versions.cs -- <folder holding the downloaded .nupkg files> 1.1.2`. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-048 "<what you ran and what it showed>"`. TR-RAG-046 (mail actions) and Sevak#REQ-RAG-053 (agents) unblock with the same move.

### TR-RAG-049 — 1.1.2 silently moves a host's stores to the per-user default: the new `WithPersistence(provider, defaultUserId)` overload captures existing two-argument calls, and an unset vector-store connection string is now absolute (major, found 2026-10-07 moving Sevak to 1.1.2) — **fixed upstream 2026-10-07, ships in the next TechieRag package after 1.1.2, not yet re-checked here**

- **Severity:** major
- **Blocks:** no — fixed in Sevak on 2026-10-07 (see Workaround); the move to 1.1.2 carried on.
- **Repro:**
  ```csharp
  // Compiled against 1.0.8 this bound to WithPersistence(provider, connectionString, defaultUserId = "default").
  // Against 1.1.2 it binds to the new WithPersistence(provider, defaultUserId): the connection string
  // becomes the user id and the tables go to <per-user TechieRag folder>/data/<app name>/techierag.db.
  builder.WithPersistence(StoreProvider.Sqlite, $"Data Source={path}");

  // 1.0.8: "Data Source=techierag.db" (relative). 1.1.2: "Data Source=C:\Users\<u>\AppData\Local\TechieRag\data\Sevak\techierag.db".
  var unset = new TechieRagConfig().VectorStore.ConnectionString;
  ```
- **Expected:** a minor release keeps every existing call site bound to the overload it bound to before, and a host that already resolves the relative default into its own folder keeps doing so. The release note for REQ-RAG-122 names both effects.
- **Actual:** the build is clean, but four Sevak workspace tests failed because their tables went to the shared per-user TechieRag folder, and a fresh install's vector store path became the per-user one. In the app, threads, workspaces and vectors would have left Sevak's data folder.
- **Encountered in:** Sevak#REQ-RAG-054 (the 1.1.2 move), 2026-10-07; Linux test rung, then the Windows head boot `tests/.artifacts/smoke-data/20261007T135701Z-tr112/`.
- **Workaround:** every `WithPersistence` call in Sevak names `connectionString:` (guarded by a test), and `TechieRagConfigService.WithoutLibraryDefault` treats the library default as unset.
- **Suggested fix:** remove or rename the `(StoreProvider, string defaultUserId)` overload, expose `VectorStoreConfig.IsConnectionStringSet`, and add a release check for a new overload that an existing call would bind to.

#### Detail

**Actual, in full:** the build is clean (no error, no warning), but four Sevak workspace tests failed because every test's workspaces landed in one shared `~/.local/share/TechieRag/data/testhost/techierag.db`, and `ConfigEncryptionTests` found the absolute per-user path where Sevak's data folder was expected. In the app the same call (`TechieRagManager`) would have put threads and workspaces outside Sevak's data folder, and a fresh install's vector store too. The 2026-10-06 reply under TR-RAG-040 said "Sevak passes its own full path, so it is not affected"; it passes the path, but the overload no longer reaches it.

**Workaround, in full:** every `WithPersistence` call in Sevak names the argument (`connectionString: …`), held by the test `REQ-RAG-054 every WithPersistence call names connectionString`. `TechieRagConfigService.WithoutLibraryDefault` treats `DataRoot.DefaultConnectionString` as unset, so it resolves into the data folder (log line on the boot: `Data Source=C:\Users\<you>\AppData\Local\TechieRag\data\Sevak\techierag.db -> …\tr112\data\techierag.db`). After the boot `%LOCALAPPDATA%\TechieRag` holds only `models`.

**Suggested fix, in full:** remove the `(StoreProvider, string defaultUserId)` overload (keep `WithPersistence(StoreProvider)`, and let a caller wanting a user id with the default database name it: `WithPersistence(provider, defaultUserId: "x")` on a single method whose connection string is optional), or give it a distinct name such as `WithDefaultPersistence`. Expose `VectorStoreConfig.IsConnectionStringSet` publicly so a host can tell "unset" from a value. Add a public-API overload check to the release checks that flags a new overload a shorter existing call would bind to. List both behaviour changes in the 1.1.2 release notes.

#### Fixed upstream 2026-10-07 (TechieRag)

- **Fixed as suggested, under `TechieRag#REQ-RAG-122` (re-Verified 2026-10-07).** Ships in the next TechieRag package after 1.1.2.
  - **Cause:** REQ-RAG-122 added `WithPersistence(StoreProvider, string defaultUserId)` beside the 1.0.8 method. For a two-argument call with a string, C# prefers the overload that needs no default argument, so every existing `WithPersistence(provider, "Data Source=…")` re-bound to it when recompiled, cleanly and without a warning. The REQ-RAG-122 tests called only the new shapes and never compiled the old one, so the verifier passed it.
  - **Fix:** the `(StoreProvider, string defaultUserId)` overload is removed. One method takes both: `WithPersistence(StoreProvider provider, string? connectionString = null, string defaultUserId = "default")`. A second positional argument is always a connection string again. `WithPersistence(StoreProvider.Sqlite, defaultUserId: "x")` uses the per-app default database for user x. `WithPersistence(StoreProvider)` stays. Sevak's `connectionString:` names keep compiling and now bind to the same method.
  - **`VectorStoreConfig.IsConnectionStringSet` is public.** `TechieRagConfigService.WithoutLibraryDefault` can test it instead of comparing against `DataRoot.DefaultConnectionString`. The getter of an unset `ConnectionString` still returns the absolute per-app path; that default was the owner's 2026-10-06 decision for REQ-RAG-122 and stays.
  - **Release check (new):** a test, `PublicOverloadsNeverGiveOneCallTwoMeanings`, runs over the public surface of every TechieRag package (core, Embedded, Telemetry, Local, Agents). It fails when two overloads of one name accept the same number of positional arguments of the same types under different parameter names, which is exactly the shape that let one call change meaning. A sample of the 1.1.2 pair is kept to prove the check reports it. No other such pair exists in the five packages today.
  - **Binary note:** an assembly compiled against 1.1.2 that called the removed overload gets `MissingMethodException`, and a source call `WithPersistence(StoreProvider.Sqlite, "alice")` written against 1.1.2 now passes "alice" as a connection string. Sevak never called it that way. The AI reference (section "Phase-3 Additions") states both changes for every consumer.
  - **Verify from here:** after moving Sevak to the new package, the four workspace tests and `ConfigEncryptionTests` pass with Sevak's existing calls. Optionally remove one `connectionString:` name, build, and confirm the tables land in Sevak's data folder. Then close this entry with `bash .tfcore/utils/tf-feedback.sh Sevak --close TR-RAG-049 "<what you ran and what it showed>"`.

### TR-RAG-051 — a local model folder left by an earlier conversion counts as almost downloaded, and accepting the small "remaining" download then deletes the old files one per attempt (minor, found 2026-10-08 during MISS-Sevak-20261005-13)

> ✅ **Closed 2026-10-18** — fixed in TechieRag 1.1.7. `ModelDownloadService.IsFileComplete` now requires an exact size match; files larger or smaller than the pin are reported as pending at their full pinned size and overwritten in the next download.

- **Severity:** minor
- **Blocks:** no — Sevak shows the library's figure and the integrity failure as it is; the built-in provider works with any model whose files match the 1.1.2 pins (Phi-3 mini here; Qwen2.5 0.5B after a clean download).
- **Repro:** a `qwen2.5-0.5b-instruct-onnx` folder from an earlier conversion (`model.onnx` 169,847 bytes, `model.onnx.data` 861,939,200 bytes) under the model root, then `new LocalLlmProvider(new LocalLlmOptions { Model = LocalModel.Qwen25Instruct05B }).GetTermsAsync()`.
- **Expected:** the folder is reported as needing the pinned files (about 333 MB), or as not matching this release, before anything is downloaded or deleted.
- **Actual:** `DownloadBytes` is 191,725 (only `model.onnx`), because `ModelDownloadService.IsFileComplete` accepts any file at least 95 % of the pinned size, and the 861 MB data file is larger than the pinned 321 MB one. Accepting downloads the new `model.onnx`; `VerifyAsync` then finds the old data file's hash wrong, deletes it and throws `LocalModelIntegrityException`, and each further attempt repeats this for the next stale file. The user is offered "Download: 1 MB" and gets "a downloaded file was damaged".
- **Encountered in:** `Sevak#MISS-Sevak-20261005-13`, 2026-10-08, LLM settings' built-in list on the owner's Windows machine (`%LOCALAPPDATA%\TechieRag\models\qwen2.5-0.5b-instruct-onnx`, files dated 2026-09-24).
- **Workaround:** none built. Sevak's test of the cached Qwen loads that folder as it is through `LocalModel.FromFolder`; the app itself never touches the stale files unless the user accepts the download.
- **Suggested fix:** treat a file whose size differs from the pin (larger or smaller, beyond a resumable `.part`) as pending at its full size, and report a folder whose complete-looking files fail the pin as "different version" before the download, so `GetTermsAsync` shows the real cost and nothing is deleted piecemeal.

### TR-RAG-052 — `TechieRag.Local` cannot call tools, so no agent with tools runs on the built-in model (major, found 2026-10-08 during MISS-Sevak-20261005-13)

> ✅ **Closed 2026-10-18** — fixed in TechieRag 1.1.7. `LocalModel.Qwen25Instruct05B` and `LocalModel.Phi3Mini4kInstruct` now report `SupportsToolCalling == true`. `LocalLlmProvider` injects tool definitions into the model's own prompt format (Hermes-style `<tools>` / `<tool_call>` for Qwen2.5, JSON-mode for Phi-3), parses `<tool_call>` output into `LlmResponse.ToolCalls`, and formats tool results back into the conversation. The conformance suite proves the round-trip against the scripted runtime and `AgentLoopRunner`.

- **Severity:** major
- **Blocks:** no — Sevak refuses the run before anything is sent and says why (en + hi): "@agent uses tools, and the built-in model cannot call tools. Choose a model that can call tools in LLM settings …". Chat without the agent works on the built-in model.
- **Repro:**
  ```csharp
  var provider = new LocalLlmProvider(new LocalLlmOptions { Model = LocalModel.Qwen25Instruct05B, TermsAccepted = true });
  await provider.LoadAsync();
  provider.SupportsToolCalling;   // false
  var runner = new AgentLoopRunner(provider, toolHandler, logger, maxIterations: 5);
  await runner.RunAsync(messages, new LlmCompletionOptions());   // the tool definitions never reach the model
  ```
- **Expected:** the in-process provider calls tools like the HTTP providers do: the tool definitions go into the prompt in the model's own tool format (Qwen2.5's ChatML `<tools>` / `<tool_call>` blocks, Phi-3's function-calling convention), a `<tool_call>` in the output is parsed into `LlmResponse.ToolCalls`, and `SupportsToolCalling` is true for a catalogue model trained for it. A host then runs `AgentLoopRunner` on the built-in model with no network and no other program.
- **Actual:** `LocalLlmProvider.SupportsToolCalling` is false and `LlmCompletionOptions.Tools` is ignored. Sevak's workspace agents (rag-search, web search, file operations, SQL, charts, MCP tools) need tools, so on a computer whose only model is the built-in one, every agent turn — including composer Agent mode — can only be refused.
- **Encountered in:** `Sevak#MISS-Sevak-20261005-13`, 2026-10-08: the owner's computer runs Sevak on the built-in model (Qwen2.5 0.5B, under 1 GB of memory free); `@agent` in workspace chat.
- **Workaround:** `AgentToolsUnsupportedException.ThrowIfCannotCallTools` in Sevak (`BuiltInLlm.cs`) refuses an agent run that would offer tools while the configured provider is the built-in one or reports `SupportsToolCalling == false`; the message points to LLM settings.
- **Suggested fix:** tool calling in `TechieRag.Local` for the catalogue models that are trained for it (Qwen2.5 instruct supports Hermes-style `<tool_call>` JSON; Phi-3 mini through a JSON-mode prompt), behind `SupportsToolCalling`, with a conformance test running `AgentLoopRunner` against the real Qwen2.5 0.5B. If a model cannot do it reliably, a per-model `SupportsToolCalling` lets the host say which built-in models can run agents.

### TR-RAG-053 — disposing a `LocalLlmProvider` while a call is waiting to generate crashes the process with an access violation (major, found 2026-10-10 during the phase-3 verify)

> ✅ **Closed 2026-10-18** — fixed in TechieRag 1.1.7 (REQ-RAG-129). `LocalLlmProvider.Dispose()` now refuses new calls, cancels the ones inside, and returns without blocking; every call that resumes afterwards throws `ObjectDisposedException` before touching native handles, and `DisposeAsync()` completes once the memory is freed.

- **Severity:** major
- **Blocks:** no — Sevak wraps the provider so it is disposed only after the last call has left, and calls made after that are refused with `ObjectDisposedException` in managed code (`OneAtATimeLlmProvider`).
- **Repro:** on Windows, Phi-3 mini loaded through `LocalLlmProvider`; one call is generating and a second `CompleteAsync` is waiting; the host disposes the provider (swapping or unloading the model). The waiting call resumes and the process dies:
  ```text
  Fatal error.
  0xC0000005
     at Microsoft.ML.OnnxRuntimeGenAI.NativeMethods.OgaCreateTokenizerStream(IntPtr, IntPtr ByRef)
     at Microsoft.ML.OnnxRuntimeGenAI.Tokenizer.CreateStream()
     at TechieRag.Local.Runtime.OnnxGenAiModel+<GenerateAsync>d__9.MoveNext()
  ```
  Seen twice, identically, in Sevak's verify of 2026-10-10 (logs under `tests/.artifacts/smoke-data/20261010T110717Z-mail-speechfix/` and `20261010T112147Z-mail-speechfix2/`). A single call disposed mid-generation on Linux with Qwen2.5 0.5B finished cleanly and the next call got `ObjectDisposedException`, so the race needs a call that is already past the disposed check and waiting inside the provider.
- **Expected:** `Dispose` waits for, or cancels, the calls already inside the provider, and every call that resumes afterwards throws `ObjectDisposedException`; a managed host never sees a native access violation.
- **Actual:** the call that resumes after `Dispose` uses the freed tokenizer and the whole application terminates; no exception reaches the host.
- **Encountered in:** Sevak phase-3 verify, 2026-10-10, REQ-FN-056's second setup pass while the done-elsewhere check was using the model.
- **Workaround:** `OneAtATimeLlmProvider` (Sevak) lets one call reach the provider at a time, cancels the call in progress on `Dispose`, refuses new calls, and disposes the provider only once the gate is free; `LocalModelService` waits up to 10 s for that before loading another model.
- **Suggested fix:** in `LocalLlmProvider`/`OnnxGenAiModel`, take the generation lock in `Dispose`, cancel waiting and running generations, and check the disposed flag after every await before touching native handles; add a test that disposes while two calls are queued.

### TR-RAG-054 — the built-in embedder takes about 100 s for a 10-page PDF, so Sevak cannot meet its 60-second embedding budget (major, found 2026-10-13 during the phase-1 fix run)

> ✅ **Closed 2026-10-16** — re-checked here: Withdrawn by Sevak's owner on 2026-10-16: not a TechieRag defect. Sevak's 60-second budget did not fit BGE-M3 on a 16 GB laptop's processor (105 s measured alone on 1.1.6). REQ-NFR-003 now asks for embedding in the background with visible progress while the app stays usable. Filing it here was Sevak's miss, MISS-Sevak-20261016-07; nothing is asked of TechieRag.

- **Status:** open
- **Severity:** major
- **Blocks:** no — documents still embed, only slower than the budget; `Sevak#REQ-NFR-003` stays failed until this is fixed.
- **Re-measured 2026-10-16 on 1.1.6, alone and warm:** 105.0 s (152.7 s on 1.1.2); still over 60 s.
- **Repro:** `TechieRag.Embedded` 1.1.2, `EmbeddedEmbeddingProvider` with the bundled BGE-M3 model, on the 10-page PDF of Sevak's REQ-NFR-003 check. Sevak's log of 2026-10-11: reading the PDF 0.7 s, embedding 100 chunks 102.1 s, storing 0.04 s. A standalone run of the same package on 8 CPU cores (WSL), same text:

  | Chunk size / overlap | Chunks | Time |
  |---|---|---|
  | 500/50 | 85 | 131.0 s |
  | 1000/100 | 43 | 122.4 s |
  | 2000/200 | 22 | 128.4 s |

  Chunk size does not change it.
- **Expected:** a 10-page text PDF embeds in under 60 seconds on an ordinary 8-core laptop with no GPU.
- **Actual:** about 100–130 seconds. Read from the 1.1.2 package: the provider embeds one text at a time in a plain loop, runs the full-precision model (2.27 GB), and has no setting for batch size or threads.
- **Encountered in:** Sevak phase-1 verify, 2026-10-11 (`embedding took 109228 ms`), re-examined 2026-10-13.
- **Workaround:** none in Sevak. The app sends one call per file, and the time is spent inside the library.
- **Suggested fix:** an 8-bit (int8) BGE-M3, batching several chunks per run, and a thread-count setting.

### TR-RAG-055 — request: the PostgreSQL path proven end to end on a real PostgreSQL 18 with pgvector (major, asked 2026-11-07 for Sevak phase 4) — **fixed upstream 2026-11-08, ships in TechieRag 1.1.9 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-08 — TechieRag REQ-RAG-152 Verified; ships in 1.1.9, not yet published.
- **Severity:** minor (a proof in CI, not new code; owner, 2026-11-07 evening)
- **Blocks:** no — `Sevak#REQ-RAG-109` builds now: `PgVectorStore`, `PostgresConversationStore` and `PostgresWorkspaceStore` are in TechieRag 1.1.8.
- **Repro:** Sevak lets a user choose a PostgreSQL server of their own at first setup (owner, 5 and 7 November 2026). The three stores exist, but BRD-157 (`LivePgVectorStoreTests` against a real PostgreSQL) is Implemented 85%, and nothing proves them together in one database.
- **Expected:** one `UsePostgres(connectionString)` builder call puts vectors, conversations and workspaces in the same database; the live tests run in CI against a PostgreSQL 18 with pgvector that the test harness starts in Docker; upsert, search, delete and the store round-trips pass.
- **Actual:** the stores exist; no end-to-end proof, no harness-started server, no single builder call.
- **Encountered in:** Sevak phase 4 requirements, `docs/Sevak-P4-BRD.md` BRD-182 to BRD-185, 2026-11-07.
- **Workaround:** none; Sevak's SQLite path is unaffected.
- **Suggested fix:** TechieRag BRD-210 as written, with one change: `LivePostgresFactAttribute` reads the connection string from the environment variable `TechieRagTestPostgres` today. The owner's rule is a settings file beside the tests, never an environment variable: a git-ignored `tests/TechieRag.Tests/livetests.settings.json`, with a committed example beside it, read at discovery time and written by the harness-started server; absent, the tests skip with the reason.

### TR-RAG-056 — request: a host-data skill, so a host's assistant reads and changes the host's own data in plain words (major, asked 2026-11-07 for Sevak phase 4) — **fixed upstream 2026-11-08, ships in TechieRag 1.1.9 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-08 — TechieRag REQ-RAG-153 Verified; ships in 1.1.9, not yet published.
- **Severity:** major
- **Blocks:** yes — `Sevak#REQ-RAG-116` (the assistant reads and changes Sevak's data: tasks, the plan, projects, milestones, goals, areas, people, places, routines, memory, settings) waits for TechieRag BRD-211 (REQ-RAG-153) in package 1.1.9; the TechieRag build runs before the Sevak phase 4 build.
- **Repro:** a host registers its own entities (name, fields, and handlers to list, read, create, update and delete) and the agent gets one tool set for them, built once in the library rather than hand-written tool by tool in every host. Sevak's agent (`Sevak#BRD-138`) is the first consumer; every entity above is a Dapper repository in `apps/Sevak.Core`.
- **Expected:** `HostDataSkill` with `IHostEntity`; "add task call the bank at 3" runs the task entity's create handler with the fields the model extracted; the skill composes with the existing catalogue, egress gate and trace (TechieRag ADR-011, Sevak ADR-015).
- **Actual:** the agent runtime offers tools and MCP, but no entity-level skill a host can fill with its own data handlers.
- **Encountered in:** `docs/Sevak-P4-BRD.md` BRD-203, 2026-11-07.
- **Workaround:** none accepted; hand-writing one tool per entity in Sevak would be the private loop ADR-015 forbids.
- **Suggested fix:** TechieRag BRD-211 as written.

### TR-RAG-057 — request: every host-data change comes back as a typed change record the host can undo (major, asked 2026-11-07 for Sevak phase 4) — **fixed upstream 2026-11-08, ships in TechieRag 1.1.9 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-08 — TechieRag REQ-RAG-154 Verified; ships in 1.1.9, not yet published.
- **Severity:** major
- **Blocks:** yes — `Sevak#REQ-UI-087` (every change is a card with Undo) waits for TechieRag BRD-212 (REQ-RAG-154) in package 1.1.9; Sevak stores the record in `AssistantChange`; the TechieRag build runs before the Sevak phase 4 build.
- **Repro:** each change the skill of TR-RAG-056 makes returns (entity, id, before, after, the turn that caused it), and the skill reverses a record on request through the host's own handlers, so the host shows a card with Undo and never guesses what the model changed.
- **Expected:** `HostDataChange` returned per change; `Undo(change)` restores the "before" state through the host's update or delete handler.
- **Actual:** nothing of the kind exists.
- **Encountered in:** `docs/Sevak-P4-BRD.md` BRD-204, 2026-11-07.
- **Workaround:** none.
- **Suggested fix:** TechieRag BRD-212 as written.

### TR-RAG-058 — request: image generation through the library for OpenAI, Gemini and Venice (minor, asked 2026-11-07 for Sevak phase 5) — **fixed upstream 2026-11-08, ships in TechieRag 1.1.9 (not yet published), not yet re-checked here**

- **Status:** built upstream 2026-11-08 — TechieRag REQ-RAG-155 PARTIAL: checked against a local stand-in, not yet against the real services (no keys on the TechieRag machine); ships in 1.1.9, not yet published.
- **Severity:** minor
- **Blocks:** no for phase 4; `Sevak#BRD-281` (hero images and thumbnails, phase 5) can start with Canva and Venice through their own APIs and gains the library route when this ships.
- **Repro:** `IImageGenerationProvider` with `GenerateImageAsync(prompt, size, options)` returning the image bytes, its media type and the usage, routed like the chat providers, no key ever persisted (TechieRag ADR-003).
- **Expected:** PNG bytes of the asked size come back with the usage recorded, for OpenAI, Gemini and Venice.
- **Actual:** BRD-161 lists image generation as deferred; no provider interface exists.
- **Encountered in:** `docs/Sevak-P5-BRD.md` BRD-281, 2026-11-07; Sevak's BRD-127 has carried it since 2026-09-03.
- **Workaround:** Canva and Venice called directly from Sevak until the library route exists.
- **Suggested fix:** TechieRag BRD-213 as written.

### TR-RAG-059 — request: token usage by day and by month, not only since the app started (minor, found 2026-11-08 building Sevak phase 4 group L) — **fixed upstream 2026-11-13, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-13 — TechieRag REQ-RAG-162 Verified; ships in 1.1.10, not yet published. (Copied into this file on 2026-11-13 from Sevak's own `docs/Sevak-TechieRag-Feedback.md`.)
- **Severity:** minor
- **Blocks:** no — `Sevak#REQ-UI-089` (the pane header shows the model and today's spend) shows the `ITokenTracker.GetSessionUsage()` cost, the figure the Token Usage screen shows; it equals today's spend only while the app has not run past midnight, and the mockup's "this month" figure is left out.
- **Repro:** ask `ITokenTracker` for the cost of today, or of the current month, after a restart.
- **Expected:** `GetUsageBetween(fromUtc, toUtc)` (or by day) from a persisted usage log, so a host can show "today" and "this month".
- **Actual:** `ITokenTracker` keeps an in-memory session only (`GetSessionUsage`, `GetUsageByModel`); a restart starts it at zero and nothing is kept per day.
- **Encountered in:** `apps/Sevak/Components/Assistant/ChatPane.razor` `RefreshSpend`, docs/mockups/assistant.html `assistant-header-spend` ("₹41 today · ₹1,240 this month").
- **Workaround:** none taken beyond showing the session figure labelled "today".
- **Suggested fix:** persist each `TokenUsage` with its timestamp (the record already carries `Timestamp`) and add a ranged query.

### TR-RAG-060 — PgVectorStore builds its IVFFlat index on an empty table, so a search returns some, one or none of the stored chunks (major, found 2026-11-09 in Sevak phase 4 fix cycle 1) — **fixed upstream 2026-11-13, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-13 — TechieRag REQ-RAG-161 Verified against PostgreSQL 18 + pgvector 0.8.6; ships in 1.1.10, not yet published. (Copied into this file on 2026-11-13 from Sevak's own `docs/Sevak-TechieRag-Feedback.md`.)
- **Severity:** major
- **Blocks:** no — Sevak passes `Options=-c ivfflat.probes=32768` on the server connection string (`TechieRagManager.WithExactVectorSearch`), which makes every search scan all lists; `Sevak#REQ-RAG-109` then passed three runs in a row.
- **Repro:** `new TechieRagBuilder().UsePgVector(cs).WithPersistence(StoreProvider.Postgres, connectionString: cs)`, `InitializeAsync`, ingest two short texts, `SearchAsync(question, 2)`. Run it a few times: the result count is 0, 1 or 2 depending on the plan PostgreSQL picks (an extra query on the connection beforehand was enough to change it). `tests/Sevak.Tests/Storage/PgVectorRagTests.cs` against `pgvector/pgvector:0.8.6-pg18-trixie` failed every run before the workaround.
- **Expected:** a search over N chunks with `topK >= N` returns all N, ordered by cosine similarity.
- **Actual:** `PgVectorStore.InitializeAsync` runs `CREATE INDEX IF NOT EXISTS IdxChunksEmbedding ON Chunks USING ivfflat (Embedding vector_cosine_ops)` at start-up, when Chunks is empty. IVFFlat trains its list centres from the rows present at build time, so the 100 lists have no meaningful centres, and with pgvector's default `ivfflat.probes = 1` an index scan reads one list and misses the rest.
- **Encountered in:** `Sevak#REQ-RAG-109` (BRD-185), `apps/Sevak.Core/Services/TechieRagManager.cs` `ServerStoreConnectionString`.
- **Workaround:** the session option above, documented at `WithExactVectorSearch`; remove it once a release fixes the index.
- **Suggested fix:** use an HNSW index (`USING hnsw (Embedding vector_cosine_ops)`), which needs no training and works from an empty table; or build the IVFFlat index only after enough rows exist and set `ivfflat.probes` (or `ivfflat.iterative_scan`) on the store's own connections.

### TR-RAG-061 — the host-data tool definitions repeat every entity's fields in each of the five tools (minor, found 2026-11-09 on TechieRag 1.1.9) — **fixed upstream 2026-11-11, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-11 — TechieRag REQ-RAG-160 Verified; ships in 1.1.10, not yet published.
- **Severity:** minor
- **Blocks:** not stated in the copy relayed to TechieRag on 2026-11-11; Sevak's own entry holds the answer.
- **Repro:** 15 entities with 2 to 9 fields each on a `HostDataSkill` over `LocalLlmProvider` with `LocalModel.Qwen25Instruct05B`.
- **Expected:** the entities are described once, and a host can pick the entities for a turn.
- **Actual:** the first call throws `LocalPromptTooLongException`: 7,805 tokens against a 4,096-token context.
- **Encountered in:** Sevak, 2026-11-09, on TechieRag 1.1.9 (entry relayed in full by the owner on 2026-11-11; this copy did not have it).
- **Workaround:** none relayed.
- **Suggested fix:** as Expected.

### TR-RAG-062 — `AgentLoopRunner` takes a tool request that a local model writes as text for the final answer (major, found 2026-11-09 on TechieRag 1.1.9) — **fixed upstream 2026-11-11, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-11 — TechieRag REQ-RAG-159 Verified; ships in 1.1.10, not yet published.
- **Severity:** major
- **Blocks:** not stated in the copy relayed to TechieRag on 2026-11-11; Sevak's own entry holds the answer.
- **Repro:** `TechieRag.Local`, Phi-3 mini, a question that needs a search.
- **Expected:** a tool request written in the text, bare, in a code fence or in `<tool_call>` tags, is read as a tool call, run under the loop's own guardrails (max tool calls, egress gate) and its result fed back, as a structured call is since 1.1.7.
- **Actual:** the loop ends after one iteration and `AgentResponse.Content` is `{"name": "rag-search", "arguments": {"query": "…"}}`.
- **Encountered in:** Sevak, 2026-11-09, on TechieRag 1.1.9 (relayed by the owner on 2026-11-11).
- **Workaround:** none relayed.
- **Suggested fix:** as Expected.

### TR-RAG-063 — `AnthropicLlmProvider` always sends temperature when `LlmCompletionOptions.Temperature` is set, and current Claude models refuse it (major, found 2026-11-09 on TechieRag 1.1.9) — **fixed upstream 2026-11-11, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-11 — TechieRag REQ-RAG-158 Verified 2026-11-12, including a live call to `claude-sonnet-5-5` with temperature set. Ships in 1.1.10, not yet published.
- **Severity:** major
- **Blocks:** not stated in the copy relayed to TechieRag on 2026-11-11; Sevak's own entry holds the answer.
- **Repro:** `AnthropicLlmProvider` with model `claude-sonnet-5-5`, `ChatAsync(messages, new LlmCompletionOptions { Temperature = 0.3f })`, alone or inside `AgentLoopRunner`.
- **Expected:** the provider knows which models take a sampling temperature and leaves it out for those that do not, or retries once without it on that 400, so a host can pass one option set to every provider; the same for any other sampling option a model rejects.
- **Actual:** the API answers 400 "temperature is deprecated for this model".
- **Encountered in:** Sevak, 2026-11-09, on TechieRag 1.1.9 (relayed by the owner on 2026-11-11).
- **Workaround:** none relayed.
- **Suggested fix:** as Expected.

### TR-RAG-064 — TokenUsageTracker prices a model it does not know at $0, and its table lacks every current Claude model, so spend reads $0.00 (major, found 2026-11-13 in Sevak triage-and-fix) — **fixed upstream 2026-11-13, ships in TechieRag 1.1.10 (not yet published), not yet re-checked here**

- **Status:** fixed upstream 2026-11-13 — TechieRag REQ-RAG-164 Verified; ships in 1.1.10, not yet published. (Copied into this file on 2026-11-13 from Sevak's own `docs/Sevak-TechieRag-Feedback.md`.)
- **Severity:** major
- **Blocks:** no — Sevak adds the current prices to every tracker it builds (`TechieRagManager.SeedCurrentModelPrices`, through `TokenUsageTracker.SetModelPricing`); the Assistant header now shows the day's spend.
- **Repro:** TechieRag 1.1.9. `var tracker = new TokenUsageTracker(); var usage = new TokenUsage { InputTokens = 1_000_000, OutputTokens = 100_000, ModelName = "claude-sonnet-5-5", ProviderName = "Anthropic" }; tracker.RecordUsage(usage);` gives `usage.EstimatedCostUsd == 0m`. `InitializeDefaultPricing` knew gpt-4o, gpt-4o-mini, gpt-4-turbo, claude-opus-4-6, claude-sonnet-4-5 and claude-haiku-4-5 only; `CalculateCost` returned 0 when `FindPricing` found nothing.
- **Expected:** the default table carries the current models (claude-fable-5-2, claude-opus-5-5, claude-sonnet-5-5, claude-haiku-5-5 at $8/$40, $4/$20, $2/$10, $0.10/$0.50 per million input/output tokens), and a model with no price is reported as "not priced" (a flag on `TokenUsage`, or a log line), never as a cost of $0, so a budget check does not pass silently.
- **Actual:** every claude-sonnet-5-5 call costs $0; Sevak's budget question (spend above the warning share) can never fire on a current Claude model.
- **Encountered in:** `Sevak#REQ-UI-089` and `Sevak#REQ-RAG-118`, phase 4 verify of 13 Nov 2026: the Assistant header said "$0.00 today" after 73 answers on claude-sonnet-5-5.
- **Workaround:** above.
- **Suggested fix:** add the current models to `InitializeDefaultPricing`, and mark an unpriced usage instead of pricing it at zero.

## Replies from TechieRag

<!-- The upstream team's answers, newest block first. -->

### 2026-11-13 (later) — TR-RAG-064 fixed for 1.1.10

TR-RAG-064 → `TechieRag#REQ-RAG-164`, Verified 2026-11-13, in the same unpublished 1.1.10 as TR-RAG-059 to TR-RAG-063; the owner tags and publishes. This copy did not have the entry; it was copied in from Sevak's file, unchanged apart from its status.

- **Prices.** The table now has every model on Anthropic's pricing page, read on 2026-11-13 at https://platform.claude.com/docs/en/about-claude/pricing, per million input / output tokens:
  - Fable 5.2 and Mythos 5.2: $8 / $40.
  - Fable 5.1, Mythos 5.1, Fable 5 and Mythos 5: $10 / $50.
  - Opus 5.5: $4 / $20. Opus 5, 4.8, 4.7, 4.6 and 4.5: $5 / $25. Opus 4.1 and 4: $15 / $75.
  - Sonnet 5.5 and Sonnet 5: $2 / $10. Sonnet 4.6, 4.5 and 4: $3 / $15.
  - Haiku 5.5: $0.10 / $0.50, or $0.50 / $2.50 when the prompt is over 100,000 tokens. Haiku 4.5: $1 / $5.
  - Your four figures match the page. Two old entries were wrong and are corrected: Opus 4.6 was $15 / $75, Haiku 4.5 was $0.80 / $4.
  - Dated, Vertex (`@date`) and Bedrock ids are priced too. Cache reads and writes are not priced.
- **"Price not known", never $0.** A model with no price keeps a cost of 0, but `usage.IsPriceKnown` is false. That is how a host tells the two apart: a priced call that came to $0 keeps `IsPriceKnown` true. The summaries count unpriced calls apart from the cost: `UnpricedOperationCount`, `UnpricedTokens`, `UnpricedModels`, and `IsCostComplete` false. `BudgetStatus.IsCostIncomplete` is true, `TokenUsageTracker.OnPriceNotKnown` fires for each such call, and `TryGetPricing(model, out _)` answers before a call. A price key also no longer prices another version of the model: `claude-opus-5` does not price `claude-opus-5-7`, which stays "price not known" until it is added.
- **Proof.** `REQ-RAG-164 SevakReproCostsThreeDollars` (your repro) cost $0 on 1.1.9 and costs $3.00 now. `REQ-RAG-164 CurrentClaudeModelsCostTheirListPrice` checks every model on the page; 25 of its 27 cases failed before. `REQ-RAG-164 UnknownModelIsPriceNotKnownNeverZero`, `NewerVersionIsNotPricedAsOlder`, `HaikuFiveFiveLongPromptCostsMore` and `UsageLogKeepsPriceNotKnown` cover the rest. 36 tests pass.

**Verify from here:** move Sevak to TechieRag 1.1.10.
1. Remove `TechieRagManager.SeedCurrentModelPrices`, or keep only prices the library does not have.
2. Ask the Assistant one question on claude-sonnet-5-5. The header should show a cost above $0.00 for today.
3. Point the Assistant at a model the table does not know. The header should say the price is not known: read `IsCostComplete` or `UnpricedTokens` from the day's summary. It should never show $0.00.
4. Set a cost budget with a small `MaxCostUsd` and run until the warning share is passed. `OnBudgetAlert` should fire.

### 2026-11-13 — TR-RAG-059 and TR-RAG-060 fixed for 1.1.10

Both are fixed in TechieRag 1.1.10, the same unpublished release as TR-RAG-061 to TR-RAG-063; the owner tags and publishes. This copy did not have either entry; both were copied in today from Sevak's file, unchanged apart from their status.

- **TR-RAG-060 → `TechieRag#REQ-RAG-161`, Verified 2026-11-13, against a real PostgreSQL 18 with pgvector 0.8.6** (TechieRag's own test container, `pgvector/pgvector:0.8.6-pg18-trixie`). `PgVectorStore` now builds an HNSW index, which needs no training, so it is correct when built on the empty table. On start-up it drops an existing IVFFlat `IdxChunksEmbedding` and builds the HNSW one, with a logged warning; that happens once, on Sevak's existing database too. Each search runs in its own transaction with `hnsw.ef_search` raised to topK (40 to 1,000). On pgvector 0.8.0 and later it also uses `hnsw.iterative_scan = strict_order`, so a search filtered to one document still finds that document's chunks. Proven by `REQ-RAG-161 SearchOnNewDatabaseReturnsEveryChunk` (50 chunks, topK 50, with the planner held to the index): on 1.1.9 it returned **2 of 50**, now 50, in cosine order. Your repro, `REQ-RAG-161 TwoChunksComeBackEveryRun` (two texts, a search for two, five times), returned **0** on 1.1.9 and now returns 2 every time. Also covered: `REQ-RAG-161 EarlierIvfFlatIndexIsReplaced` ("ivfflat" on 1.1.9, now "hnsw") and `REQ-RAG-161 FilteredSearchFindsFarDocument`. Each test builds its tables in a new schema, so the index is always built on an empty table, as on a new database.
- **TR-RAG-059 → `TechieRag#REQ-RAG-162`, Verified 2026-11-13.** `ITokenTracker` has `GetUsageBetween(fromUtc, toUtc)`, `GetUsageByDay(firstDay, lastDay, timeZone)` and `GetUsageByMonth(firstDay, lastDay, timeZone)`. Days and months are counted in the time zone you pass, so "today" is India's today. With `WithPersistence` (Sevak's server path included), every usage is also written to a `TrTokenUsage` table in the same database, so both figures include usage from before a restart. `UsageTracking.PersistUsage = false` turns this off; `WithTokenUsageLog(log)` takes another store. Proven by `REQ-RAG-162 RestartKeepsTodayAndThisMonth` (SQLite: a second tracker on the same file sees the first one's usage by day and by month, in UTC+05:30) and `REQ-RAG-162 PostgresLogKeepsUsageAcrossRestart` (the same on the real PostgreSQL). On 1.1.9 these methods did not exist, so the tests could not be built, and a restart started usage at zero. Usage from before the upgrade was never stored, so on the first day Sevak runs 1.1.10, "this month" starts from that day.

**Verify from here:** move Sevak to TechieRag 1.1.10. (1) Remove `WithExactVectorSearch`'s `ivfflat.probes` option, start Sevak once on its existing database (the log should say the IVFFlat index is replaced), then run `Sevak#REQ-RAG-109` three times; it should pass each time. (2) In `ChatPane.RefreshSpend`, read `GetUsageByDay(today, today, india)[today]` and `GetUsageByMonth(today, today, india)[firstOfMonth]`. Make a call, restart Sevak, and check that both figures still include it.

### 2026-11-11 — TR-RAG-061, TR-RAG-062 and TR-RAG-063 fixed for 1.1.10

All three are fixed in TechieRag 1.1.10. The packages now carry 1.1.10, not 1.1.9: Sevak already built against 1.1.9, and a second 1.1.9 with other contents would stay hidden behind the copy in Sevak's package cache. Nothing is published yet; the owner tags and publishes. TR-RAG-059 and TR-RAG-060 are not in this copy and are not part of this release; they are planned for Thursday on Windows.

- **TR-RAG-063 → `TechieRag#REQ-RAG-158`, Verified 2026-11-12 (live call made).** `AnthropicLlmProvider` leaves `temperature` and `top_p` out for Claude Fable and Mythos, Opus 4.7 and later, and every generation-5 model (`claude-sonnet-5-5`, `claude-opus-5-5`, `claude-haiku-5-5`). It still sends them to Opus 4.6, Sonnet 4.6, Haiku 4.5, older models and non-Claude models. A model the rule does not know that answers 400 naming a sampling option gets the call sent once more without them, and is remembered for the rest of the process. Proven by `REQ-RAG-158 SonnetFiveFiveChatWithTemperatureSucceeds`, against a stand-in that answers with your recorded 400 (`{"type":"error","error":{"type":"invalid_request_error","message":"temperature is deprecated for this model."}}`). It fails on 1.1.9 with `LlmHttpException … 400 (Bad Request). Provider message: temperature is deprecated for this model.` and passes now. The same holds inside `AgentLoopRunner` and while streaming. **Live call made 2026-11-12:** `REQ-RAG-158 LiveSonnetFiveFiveAnswersWithTemperatureSet` sent `Temperature = 0.3f` and `TopP = 0.9f` to the real `claude-sonnet-5-5` and got an answer (passed, 3 s).
- **TR-RAG-062 → `TechieRag#REQ-RAG-159`, Verified 2026-11-11.** `AgentLoopRunner.RunAsync` and `RunStreamAsync` read a tool request written as text when it names one of the loop's tools: bare JSON, a code fence, or `<tool_call>` tags (several per turn). It runs through the loop's own tool handler, so `GuardedToolHandler` and your egress gate apply, and it counts against the iteration limit. Its result is fed back, and the request is never the final answer. While streaming, such text is held back, not shown. Proven by `REQ-RAG-159 BareJsonRequestRunsAsToolCall` (your exact shape), which fails on 1.1.9 (the answer was the request) and passes now. Also covered by `WrittenRequestGoesThroughTheGuard`, `WrittenRequestsObeyTheIterationLimit`, and `LocalModelWrittenRequestIsRun` through `LocalLlmProvider` with the Phi-3 template.
- **TR-RAG-061 → `TechieRag#REQ-RAG-160`, Verified 2026-11-11.** Each entity and its fields are written once, in the first tool's description; the other four tools point to it. A host picks a turn's entities with `skill.BeginTurn(turnId, ["task", "project"])` or `skill.OfferEntities(...)`. A call naming another entity is refused with `HostDataEntityNotOffered`. Proven by `REQ-RAG-160 FifteenEntitiesFitAFourThousandTokenModel`: 15 entities with 2 to 9 fields, one agent call to a 4,096-token ChatML model through `LocalLlmProvider`. On 1.1.9 it throws `LocalPromptTooLongException` at 7,214 tokens (the test's word counter; your real tokenizer counted 7,805). Now the prompt is 2,458 tokens and the call answers. The Qwen weights are not on this machine, so the real tokenizer was not run.

**Verify from here:** move Sevak to TechieRag 1.1.10. (1) Run the agent on `claude-sonnet-5-5` with `Temperature = 0.3f`; it should answer, and no 400 should appear in the log. (2) Ask Phi-3 mini a question that needs `rag-search`; the trace should show a `rag-search` tool step and then a real answer. (3) Register the 15 entities on Qwen 2.5 0.5B; the first call should answer with no `LocalPromptTooLongException`. Then try `BeginTurn(turnId, <the turn's entities>)` to shorten the prompt further.

### 2026-11-08 — TR-RAG-055 to TR-RAG-058 built for 1.1.9

All four are in TechieRag 1.1.9. The project files and the release notes carry that version; the package is not published yet (the owner tags and publishes). TR-RAG-056 and TR-RAG-057 are built, so Sevak's phase 4 build can start once 1.1.9 is on the feed.

- **TR-RAG-055 → `TechieRag#REQ-RAG-152`, Verified 2026-11-08.** `UsePostgres(connectionString, defaultUserId)` puts vectors, conversations and workspaces in one database. `bash tests/TechieRag.Tests/livetests/start-postgres.sh` starts PostgreSQL 18 + pgvector in Docker with a new password and writes the git-ignored `tests/TechieRag.Tests/livetests.settings.json`; no environment variable is read. 13 live and builder tests passed against it, none skipped. The CI job `.github/workflows/live-postgres.yml` has not run yet because nothing is pushed.
- **TR-RAG-056 → `TechieRag#REQ-RAG-153`, Verified 2026-11-08.** `HostDataSkill` with `IHostEntity` (or `HostEntity` from delegates) in `TechieRag.Agentic.HostData`. One tool set (`host_data_list`, `_read`, `_create`, `_update`, `_delete`) covers every entity. It is an `IToolHandler`, so it works with the catalogue, guardrails and the trace. "add task call the bank at 3" runs the task's create handler with the fields the model extracted.
- **TR-RAG-057 → `TechieRag#REQ-RAG-154`, Verified 2026-11-08.** Every change returns a `HostDataChange` (entity, id, kind, before, after, turn id, tool call id). Store it from `HostDataSkillOptions.OnChangeAsync`, which is awaited before the model sees the result, in `AssistantChange`. `UndoAsync(change)` restores "before" through your update handler, and deletes or re-creates through your handlers. The record is plain data, so it can be stored as JSON and undone later.
- **TR-RAG-058 → `TechieRag#REQ-RAG-155`, PARTIAL 2026-11-08.** `IImageGenerationProvider.GenerateImageAsync(prompt, size, options)` for OpenAI, Gemini and Venice, created with `ImageGenerationProviderFactory.CreateForModel("venice/venice-sd35", key)`. It is checked against a stand-in in each service's shape; the real services were not called, because the TechieRag machine has no keys for them.

**Verify from here:** move Sevak to TechieRag 1.1.9. Register the task entity with `HostEntity` over the Dapper repository, give the skill to the agent, and ask "add task call the bank at 3". The create handler should run with title and due. Then call `UndoAsync` on the stored `HostDataChange`, and the task should be gone. For REQ-RAG-109, `UsePostgres(connectionString)` against the user's own server needs no other store call. For BRD-281, one `GenerateImageAsync` with a real Venice key should return PNG bytes of the asked size.

### 2026-10-11 — TR-RAG-053 fixed

TR-RAG-053 → `TechieRag#REQ-RAG-129`, Verified 2026-10-11, shipping in the next TechieRag release after 1.1.2.
- `LocalLlmProvider.Dispose()` may now be called while calls are loading, queued or generating. It refuses new calls, cancels the ones inside, and returns without blocking.
- Each call inside ends with `ObjectDisposedException` at its next step, before it touches the model again. A caller's own cancellation stays `OperationCanceledException`.
- The native model is freed when the last call has left, never under a running token.
- New: `await provider.DisposeAsync()` completes once the memory is freed. Await it before loading the next model; that replaces `LocalModelService`'s 10-second wait.
- `EstimateTokenCount` after a dispose now throws `ObjectDisposedException` instead of using the freed tokenizer.

**Verify from here:** once Sevak is on the new package, remove the dispose ordering from `OneAtATimeLlmProvider`, or the wrapper itself if it only existed for this. Then re-run REQ-FN-056's second setup pass while the done-elsewhere check uses the model. The process should stay alive, and the waiting call should end with `ObjectDisposedException`. TechieRag's test `REQ-RAG-129 DisposeWithTwoQueuedCallsEndsAllWithObjectDisposed` disposes with one call mid-token and two queued. A live run on 2026-10-11 did the same on the real engine with qwen2.5-0.5b, five rounds each on Linux and Windows, and had no crash. Phi-3 did not fit the Windows laptop's free memory that day, so the exact model from your logs was not re-run.

### 2026-10-08 — TR-RAG-012 and TR-RAG-036 answered

TR-RAG-012 → `TechieRag#REQ-RAG-098` and TR-RAG-036 → `TechieRag#REQ-RAG-082`, both re-Verified on 2026-10-08. No behaviour changed. TR-RAG-012 is now in `RerankConfig.Enabled`'s XML documentation, the AI reference and a new `RELEASE-NOTES.md` in the TechieRag repository. TR-RAG-036 is pinned by two tests that keep the HTTP connectors' SSRF guard off the mail transport. The release notes also carry TR-RAG-049's removed overload, as that entry asked. Details and close steps are in each entry.

### 2026-10-07 — TR-RAG-047 and TR-RAG-049 fixed

TR-RAG-049 → `TechieRag#REQ-RAG-122` (re-Verified) and TR-RAG-047 → `TechieRag#REQ-RAG-125` (new, Verified), both on 2026-10-07. They ship in the next TechieRag package after 1.1.2. TR-RAG-049 removes 1.1.2's `WithPersistence(provider, defaultUserId)`, so a two-argument call means a connection string again. It also makes `VectorStoreConfig.IsConnectionStringSet` public and adds a release test that fails on any overload pair that gives one call two meanings. TR-RAG-047 makes the fit check public and refuses a model that cannot fit before its download starts. The causes, members and close steps are in each entry. The 2026-10-06 line under TR-RAG-040 ("Sevak passes its own full path, so it is not affected") was wrong for 1.1.2, for the reason TR-RAG-049 gives.

### 2026-10-06 — phase 3 built: eleven entries fixed upstream

All eleven were built in TechieRag phase 3 and Verified on 2026-10-06 (`docs/TechieRag-P3-Checklist.md`). They ship in the next TechieRag package, the same one as the TR-RAG-048 fix, once it is published. Every change adds to the public API; the two behaviour changes are noted under TR-RAG-039 and TR-RAG-040. The AI reference and the `/techierag` agent files the package installs (`.techierag/TechieRag-AI-Reference.md`, section "Phase-3 Additions"; `.claude/commands/techierag.md`; `.opencode/command/techierag.md`) describe every new member with a call example, so Sevak's agents can use them after the package update; `dotnet build -t:TechieRagRedeployAgentFiles` refreshes them.

- TR-RAG-010 is fixed (REQ-RAG-114). Pinned documents are fetched with one filtered search (`SearchOptions.DocumentFilters`, `IVectorStore.SearchDocumentsAsync`, built into SQLite-vec, PgVector and Qdrant). A pinned document that wins no slot gets one extra search, so it stays in context. A custom `IVectorStore` keeps compiling: the new method has a default body.
- TR-RAG-011 is fixed (REQ-RAG-115). Pinned results follow the workspace's own rerank switch, and the rule is in the API docs and the AI reference.
- TR-RAG-013 is fixed (REQ-RAG-116). `IWorkspaceManager` is public and `WorkspaceManager` implements it. `AddTechieRag` registers it only if nothing is registered yet, so a stand-in registered first is kept. `GetWorkspaceManager()` still returns the class.
- TR-RAG-022 is fixed (REQ-RAG-117). A cancelled run throws `ConnectorRunCanceledException`, which is still an `OperationCanceledException`, carrying the partial result with its sync state and item reasons. A failed run's `ConnectorException` carries the same. Pass that sync state to the next run to resume. The sync rebuild in `ObservedConnector` can go.
- TR-RAG-023 is fixed (REQ-RAG-118). `ConnectorItemFailure.Outcome` says Failed or Skipped. Sizes in reasons have no thousands separator on any machine. `ConnectorIngestionExtensions.BuildMetadata` and `ConnectorDocumentKey` are public.
- TR-RAG-026 is fixed (REQ-RAG-120). The new overload `IngestTextAsync(text, name, sourceKey, metadata)` replaces the document stored under the same key; the new text is embedded before the old copy is removed. `IngestConnectorAsync` passes the key `connector:<SourceType>:<SourceName>:<ItemId>`, so two connectors with the same item id never overwrite each other. Sevak's item-to-document table can go.
- TR-RAG-035 is fixed (REQ-RAG-119). `ParsedMailMessage.Notes` lists each part skipped past `MimeParser.MaxNestingDepth`, or past the attachment cap, by code (`MailParseCodes`), name and depth. It is empty when the whole message was read. Mail synced through `EmailConnector` carries the same notes on the item's metadata, under `EmailConnector.ParseNotesMetadataKey`, as `code: part name (depth n)` entries.
- TR-RAG-039 is fixed (REQ-RAG-121). Registering a tool name again replaces its definition as well as its handler, ignoring letter case. **Behaviour change:** a second registration used to add a second definition; now the last one wins.
- TR-RAG-040 is fixed (REQ-RAG-122). With no database folder given, an existing `techierag.db` in the folder the app runs from is still used, with a logged warning naming it, and is never moved. Otherwise the database is created under `<per-user TechieRag folder>/data/<app name>/`, beside the shared `models` folder. `DataRoot.Set` moves it in code; `ModelRoot.Set` and `TECHIERAG_MODEL_ROOT` move it together with the models. `DataRoot.DefaultDatabasePath` returns the location. **Behaviour change** for an app relying on the default with no file in its running folder: it starts with an empty store in the new folder. Sevak passes its own full path (`TechieRagManager`), so it is not affected.
- TR-RAG-042 is fixed (REQ-RAG-123). Use the overload `FlowNodeCatalog.CreateNode(kind, id, stepName)` and pass Sevak's localized step name.
- TR-RAG-043 is fixed (REQ-RAG-124). `FlowDefinition.UsesLanguageModel()` and `GetLanguageModelSteps()` answer the question. They are methods, so they are never written into a saved flow. `FlowCapabilities.NeedsLlmProvider` can call them.

### 2026-10-06 — the still-open entries are ledgered as TechieRag phase 3

Each still-open defect now has a TechieRag requirement in a new phase 3 (`docs/TechieRag-P3-BRD.md`, checklist `docs/TechieRag-P3-Checklist.md`). All are Not Started; each entry stays open until its row is built and verified.

- TR-RAG-010 → BRD-174, one search over all pinned documents (REQ-RAG-114).
- TR-RAG-011 → BRD-175, pinned results follow the workspace's rerank switch (REQ-RAG-115).
- TR-RAG-013 → BRD-176, public `IWorkspaceManager` (REQ-RAG-116).
- TR-RAG-022 → BRD-177, sync state and item reasons returned on a cancel or failure (REQ-RAG-117).
- TR-RAG-023 → BRD-178, typed Failed / Skipped outcome, culture-free sizes, public metadata builder (REQ-RAG-118).
- TR-RAG-026 → BRD-179, optional document key that replaces the earlier document (REQ-RAG-120).
- TR-RAG-035 → BRD-180, optional notes list on the parsed message (REQ-RAG-119).
- TR-RAG-039 → BRD-181, a repeated tool name replaces its definition (REQ-RAG-121).
- TR-RAG-040 → BRD-182, per-app database folder beside the models when no folder is given; an existing file in the running folder is kept, with a warning (REQ-RAG-122).
- TR-RAG-042 → BRD-183, optional step name on `CreateNode` (REQ-RAG-123).
- TR-RAG-043 → BRD-184, a flow says whether it calls a model, and which steps (REQ-RAG-124).

### 2026-10-06 — re-check of the 21 entries that had no reply of their own

Each entry below was checked against TechieRag's current source and checklists on 2026-10-06. Until now they read as settled only because of a blanket sentence in the July summary, which said nothing about them one by one.

- TR-RAG-001 is fixed by `TechieRag#REQ-RAG-103` (BRD-151, Verified). Call `AskStreamWithSourcesAsync` (or `ChatWithRagStreamWithSourcesAsync` for chat). The first event carries the sources, then the tokens arrive, then a completed event with the full answer. That is one vector search per question, and the prompt goes through the configured prompt template. Test `REQ-RAG-103 StreamingYieldsSourcesThenTokensThenCompleted`.
- TR-RAG-002 is fixed by `TechieRag#REQ-RAG-104` (BRD-152, Verified). OpenAI-compatible providers ask the server for a final usage chunk, and when a server leaves it out the library estimates the tokens, so streamed usage is never 0/0. Sevak's estimate-and-record fallback can go. (The TR-RAG-002 named in REQ-RAG-112 / BRD-171 is Lekhak's and Chatur's entry, not this one.)
- TR-RAG-009 is fixed by `TechieRag#REQ-RAG-096` (BRD-143, Verified). The non-workspace prompt path now logs a warning and raises a `ContextTruncated` event on `PromptTemplateEngine` when results are dropped. To react in code, build the engine yourself, subscribe to the event, and pass it with `WithCustomPromptTemplate`.
- TR-RAG-014 is fixed for the standard document keys (same root cause as TR-RAG-038). `ListDocumentsAsync` returns `SourceUrl`, `SourceType`, `SourceName`, `ContentType`, `ItemId`, `FileSize` and the other `DocumentMetadataKeys.DocumentScoped` keys passed to `IngestTextAsync`. Custom keys stay on the chunks only. The `SourcePath` workaround can go.
- TR-RAG-015 is resolved by removal. The owner chose to remove YouTube ingestion (BRD-120 struck out; `TechieRag#REQ-RAG-107`, BRD-164, Verified), so there is nothing left to call.
- TR-RAG-016 is fixed. `TechieRag.Embedded` sets up its own ONNX Runtime library lookup when the assembly loads, so `UseEmbedded()` works in a plain net10.0 host on macOS and Linux. The model cache sits per user under LocalApplicationData (`TechieRag#REQ-RAG-053`). Sevak's own lookup can be deleted; keeping it does no harm.
- TR-RAG-020 is fixed by `TechieRag#REQ-RAG-083` (BRD-127, Verified). The `ConnectorRunner.RunAsync` overload with an `onDocument` handler passes each document on as soon as it is fetched, so a run no longer holds them all in memory. This uses a callback, not the asynchronous stream the entry suggested.
- TR-RAG-021 is fixed under the same row. Set `Method` and `Body` on `ConnectorHttpRequest` and call `IConnectorTransport.SendAsync` to send a POST. The private-network block and the rate-limit retry apply exactly as for GET.

Still open, with nothing written in TechieRag yet:

- TR-RAG-022: documents now survive a cancelled run (`IngestConnectorAsync` stores each one as it arrives), but the sync state and the per-item reasons are still lost on cancel. Keep the sync rebuild and the item-to-document table.
- TR-RAG-010 (one search per pinned document), TR-RAG-011 (no stated rule for pinned chunks and reranking), TR-RAG-013 (no interface for the workspace manager), TR-RAG-023 (failures and skips share one type), TR-RAG-026 (no upsert), TR-RAG-035 (depth cap reports nothing), TR-RAG-039 (tool definitions duplicated), TR-RAG-040 (relative SQLite paths), TR-RAG-042 (English step names) and TR-RAG-043 (no "uses a model" answer on a flow): the source is unchanged, and none has a TechieRag requirement yet.
- TR-RAG-012 and TR-RAG-036 are informational. Both still describe the code correctly. The release note TR-RAG-012 asked for has not been written. The REQ-RAG-049 / BRD-135 ids in TR-RAG-035 and 036 are TechieDesk ids; in TechieRag, BRD-135 is a different item.

### 2026-10-06 — TR-RAG-048 fixed, ships as 1.1.2

TR-RAG-048 → `TechieRag#REQ-FN-071`, Verified on 2026-10-06. The test step of the GitHub Packages workflow rebuilt the net10.0 assemblies at 1.0.0.0 after the release build, and the pack step shipped them. The test step no longer builds, and a new pack-time check fails any release whose packed assemblies, or references between TechieRag packages, do not carry the package version. TechieRag.Telemetry's net10.0 assembly was affected too. The fix is in source; 1.1.2 is available once the TechieRag owner publishes the `v1.1.2` release. The cause, the evidence and the close step are in the entry.

### 2026-10-03 — TR-RAG-046 delivered

TR-RAG-046 → `TechieRag#REQ-RAG-110` (BRD-169, move / label / Trash) and `TechieRag#REQ-RAG-111` (BRD-170, dry run), both Verified on 2026-10-03, including a live Gmail run. The usage, the result codes and the close step are in the entry itself. Ships in the next package after 1.0.8; the installed `/techierag` agent files and AI reference describe it, so Sevak's agents see it after the package update.

### 2026-09-24 — restructure and ledger mapping

The file was moved from `TechieRag-TechieRag-Feedback.md` to `Sevak-TechieRag-Feedback.md` when the application left the repository. Accepted items now carry TechieRag ids: TR-RAG-004 → BRD-142; TR-RAG-009 → BRD-143; TR-RAG-017/019/027…030 → BRD-126 (fixed); TR-RAG-018 → BRD-124 (fixed); TR-RAG-020…022 → BRD-127; TR-RAG-015 → BRD-120 (blocked, owner decision); TR-RAG-016/025 → BRD-90 (native wiring moves into the package); TR-RAG-044/045 → BRD-148; the 1024-dimension finding → BRD-158.

### Summary as it stood before 2026-09-24 (kept verbatim)

- 0 blockers · 3 major (1 OPEN: TR-RAG-001 streaming RAG sources; 2 FIXED: TR-RAG-005 cold-start deadlock, TR-RAG-006 LmStudio tool calling) · 3 minor (1 OPEN: TR-RAG-002 streamed 0-usage; TR-RAG-003/004 resolved SDK-usage notes) · 0 nice-to-have
- **Open for the TechieRag team: TR-RAG-001** (major — streaming RAG can't return sources / bypasses PromptTemplateEngine); TR-RAG-002 (minor). All others fixed app-side or are SDK-usage notes.
- 2026-07-26: TR-RAG-013 appended (minor, open) — `WorkspaceManager` has no interface / no virtual members, so consuming apps cannot unit-test services composed over it without building a full client.
- 2026-07-27: TR-RAG-014/015/016 appended (all major, all OPEN) — found by running REQ-RAG-016/017/018 against the **real** network and the **real** embedded model for the first time. Every one was invisible to the 135-test hermetic suite, because each fails only in a component those tests replace with a fake. **TR-RAG-015 blocks REQ-RAG-018 and needs a product decision, not a code fix.**
- 2026-07-27: TR-RAG-017..021 appended by REQ-RAG-032 (`IDataConnector` framework). **TR-RAG-017 and TR-RAG-018 are major security defects, both found and FIXED in-cluster** — a connector HTTP transport that re-derived the SSRF guard more weakly than the one this codebase already had and defaulted it off, and a Confluence connector that followed response-supplied URLs with the site's API token attached. Both were invisible to a 476-test suite because neither had any test at all. TR-RAG-019 (no aggregate byte budget) fixed. TR-RAG-020 (runner materialises all fetched documents; no streaming API) and TR-RAG-021 (`IConnectorTransport` is GET-only) left **OPEN** by design — both want the REQ-FN-020 consumer to define the seam.
- 2026-07-27: TR-RAG-022/023 appended by REQ-FN-020 (connector background jobs), the consumer TR-RAG-020 was waiting for. **TR-RAG-022 is major and raises TR-RAG-020's severity**: the collect-then-return shape does not merely cost memory, it *discards every fetched document, the sync state and the collected per-item reasons* when a run is cancelled — and `IngestConnectorAsync` ingests only after the walk, so a run stopped at item 480 of 500 ingests zero. The entry names the seam TR-RAG-020 asked for (decorate `IDataConnector`, not the runner) and the two requirements a streaming API must meet beyond memory. TR-RAG-023 (minor) — failures and deliberate skips share one type distinguishable only by an English string prefix; plus culture-formatted byte sizes and a `private` metadata builder.
- 2026-07-28: TR-RAG-024/025/026 appended by REQ-RAG-019/REQ-RAG-020 (saved repository + Confluence connectors). All three were found by RUNNING the thing, not by the suite. **TR-RAG-024** — the vector store round-trips only `SourcePath`, so a connector document's `SourceUrl` came back empty and every citation pointed nowhere; web ingestion had already hit this and left the workaround in a comment. **TR-RAG-025** — `TechieRag.Embedded` cannot load its ONNX native library in a plain `net10.0` console on macOS, so the default offline embedding provider blocks all ingestion in the scheduler helper host; no test covers it because every suite substitutes a stub provider. **TR-RAG-026** — `IngestTextAsync` has no upsert, so the library's own `IngestConnectorAsync` duplicates a changed item's document on every re-sync; worked around app-side with an item→document table.
- 2026-07-28: TR-RAG-027..036 appended by the security audit of `src/TechieRag/Connectors/Email/**` — the hand-rolled IMAP + MIME stack, which had never been reviewed. **TR-RAG-027 is the serious one and is FIXED: IMAP command injection.** A folder name, sender/subject filter, account name or message identifier carrying a CRLF became a *second IMAP command*, executed with the mailbox's own credentials — proved on the wire, and it defeats the connector's entire read-only promise (`BODY.PEEK` is only read-only for the commands this connector sends). TR-RAG-028/029/030 (all major, all FIXED) are the hostile-server trio: a server-declared literal length honoured without a bound (a 2 GB allocation the server chose), an `ImapMailboxOptions.Timeout` that had no effect at all on asynchronous reads (a 2s timeout still blocked at 12s), and response accumulation with no bound on either line length or untagged-line count. TR-RAG-031..034 (minor, FIXED) — a 39 MB message decoding to 400,000 retained attachments, a culture-formatted IMAP `SINCE` key that would be rejected outright on a non-English machine, mboxrd `>>From ` left unescaped, and an attachment name retaining the NTFS ADS colon. TR-RAG-035 (minor, **OPEN** — needs a product decision) — `MimeParser` silently discards content nested past its depth cap and has nowhere to report it, because `ParsedMailMessage` has no diagnostics field and adding one is a breaking change. TR-RAG-036 (informational) records why the HTTP connectors' SSRF guard must **not** be copied here. Every one of these was invisible to the suite that existed: the pre-existing email tests drove a *cooperative* scripted server, and every defect above is something the server or the configuration does adversarially. **REQ-RAG-049 / BRD-135 remains `Not Started` — this was an audit of pre-existing code, not delivery of the requirement.**
- 2026-07-30: **TR-RAG-038 appended and FIXED, and TR-RAG-024 CLOSED with it** — same root cause from two directions. No ingestion route recorded the source artefact's byte size, and the desktop's default store wrote every document row's `Metadata` column as a literal `{}`, so nothing a route recorded about a document could survive the round trip at all. REQ-UI-021's `Size` column was therefore not merely empty, it was unfillable. Fixed by a new `DocumentMetadataKeys` (the shared allowlist of document-scoped keys), `TechieRagClient` recording `FileSize` on both the file and the text ingestion routes, and all three stores round-tripping the document-scoped set. A third trap sat behind the first two: metadata deserialized into `Dictionary<string, object>` yields `JsonElement` values, which are not `IConvertible` — the size would have been stored correctly and still been unreadable. **Also resolves an ID collision:** the checklist cited "TR-RAG-004" for this defect; TR-RAG-004 is an unrelated Qdrant scroll note and now says so.
- Severity across all entries, open and closed: 1 blocker, 26 major, 2 minor-to-moderate, 21 minor, 2 nice-to-have, 2 informational. Entries keep their id order.
- Last consolidated: 2026-10-22 (phase 3 handoff)
