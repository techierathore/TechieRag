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
