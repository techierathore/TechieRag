# TechieRag.Agents — Design Proposal v3 (shape for agreement, no code)

**Date:** 2026-09-03 (v3: owner direction that TechieRag.Agents is a first-class library package, TechieDesk is a live implementation of TechieRag + TechieRag.Embedded + TechieRag.Agents, and TechieDesk moves to its own repository)
**Author:** Chanakya (Business Analyst persona), with the owner
**Status:** DRAFT — awaiting agreement on the shape before any code is written
**Supersedes:** v2 (same path). v2's "opt-in head in TechieDesk" and "Phase A back-port onto the in-house loop" are withdrawn: TechieDesk's agent runtime becomes TechieRag.Agents. Section 10 adds the repository separation plan.

**Scope:** An agentic retrieval layer for TechieRag built on Microsoft Agent Framework (MAF), delivered as a new sibling package `TechieRag.Agents`, that fits the product TechieDesk already is: named agents, a six-skill catalogue with two-level permissions, egress confirmation, MCP servers, execution trace, workspaces with scoped retrieval, flows, schedules, licensing, localization, and a zero-egress default. TechieRag core stays dependency-free (ADR-003). `ITechieRag` does not change (ADR-005). The library-first boundary holds (TechieDesk ADR-001).

---

## 0. What the live Microsoft Agent Framework docs say (verified 2026-09-03)

Read from learn.microsoft.com, nuget.org, and the microsoft/agent-framework GitHub source. Several points differ from the premise in the request.

### 0.1 Versions and packages

| Package | Current stable | Released | Key dependencies | TFMs |
|---|---|---|---|---|
| `Microsoft.Agents.AI` | **1.20.0** | 2026-08-31 | `Microsoft.Extensions.AI` ≥ 10.9.0, `Microsoft.Extensions.AI.Abstractions` ≥ 10.9.0, `Microsoft.Agents.AI.Abstractions` 1.20.0 | net8.0, net9.0, net10.0, netstandard2.0, net472 |
| `Microsoft.Agents.AI.OpenAI` | 1.20.0 | 2026-08-31 | `OpenAI` ≥ 2.10.0, `Microsoft.Extensions.AI.OpenAI` ≥ 10.6.0 | same |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | 2026-08-11 | `OpenAI` ≥ 2.12.0 and < 2.13.0 | net8.0, netstandard2.0, net462 |
| `Microsoft.Extensions.AI` / `.Abstractions` | 10.9.0 | 2026-08 | — | — |

- "MAF 1.0, GA April 2026" is stale. The line went `1.0.0-preview.*` (Jan–Feb 2026) → `1.0.0-rc1` → stable, shipping roughly monthly; **1.20.0 is current**. Some Learn pages still say `--prerelease`; NuGet lists 1.20.0 as stable. Pin `1.20.0`.
- The .NET API reference pages are generated from 1.13.0; the conceptual pages are dated up to 2026-09-03. Where they disagree, the conceptual pages win.
- Not needed for v1: `Microsoft.Agents.AI.Workflows`, `.Hosting`, `.Hosting.OpenAI`, `.Foundry`, `.Anthropic`, `.DurableTask`, `.Hosting.AGUI.AspNetCore`.

### 0.2 Core types (namespace `Microsoft.Agents.AI` unless stated)

| Concept | Type | Notes |
|---|---|---|
| Agent base | `abstract class AIAgent` | Everything runs through this. |
| Agent over a chat client | `sealed class ChatClientAgent : AIAgent` | The one agent type for any `IChatClient`. SK's `ChatCompletionAgent` and friends are all replaced by it. |
| Agent options | `sealed class ChatClientAgentOptions` | `Name`, `Description`, `Id`, `ChatOptions` (instructions + tools), `ChatHistoryProvider`, `AIContextProviders`, `UseProvidedChatClientAsIs`, `RequirePerServiceCallChatHistoryPersistence`, `EnableMessageInjection`, `*OnChatHistoryProviderConflict`. |
| Construction | `IChatClient.AsAIAgent(string? instructions, string? name, string? description, IList<AITool>? tools, ILoggerFactory?, IServiceProvider?)` and `IChatClient.AsAIAgent(ChatClientAgentOptions, ...)` | Extension in namespace `Microsoft.Extensions.AI`, assembly `Microsoft.Agents.AI`. Or `new ChatClientAgent(chatClient, options)`. |
| Conversation state | `abstract class AgentSession` | **`AgentThread` is gone.** `AgentSession session = await agent.CreateSessionAsync();` carries a `StateBag`. Persist with `agent.SerializeSessionAsync(session)` / `DeserializeSessionAsync(json)`. |
| Running | `RunAsync(string \| ChatMessage \| IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken)` → `AgentResponse` (`.Text`, `.Messages`) | `RunStreamingAsync` → `IAsyncEnumerable<AgentResponseUpdate>`. `RunAsync<T>` for structured output. `ChatClientAgentRunOptions(ChatOptions)` merges per-run options including extra tools. |
| Tools | `Microsoft.Extensions.AI.AIFunctionFactory.Create(Delegate, name:, description:)` → `AIFunction : AIFunctionDeclaration : AITool` | `[Description]` on method and parameters. **`AIFunction` is an abstract class: a subclass supplies `Name`, `Description`, a raw `JsonSchema` (`JsonElement`) and `InvokeCoreAsync(AIFunctionArguments, CancellationToken)`.** No reflection needed. This is what makes the `IToolHandler` adapter in section 4 a small piece of work. |
| Auto tool loop | `Microsoft.Extensions.AI.FunctionInvokingChatClient` | `ChatClientAgent` wraps the `IChatClient` in one automatically unless the pipeline already has one or `UseProvidedChatClientAsIs = true`. Cap iterations by building it yourself: `chatClient.AsBuilder().UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = n).Build()`. |
| Function-call middleware | `agent.AsBuilder().Use(functionFunc).Build()` with `(AIAgent, FunctionInvocationContext, next, ct)` | `FunctionInvocationContext.Function`, `.Arguments`, `.Terminate`. Agent-run middleware: `.Use(runFunc:, runStreamingFunc:)`. |
| Human-in-the-loop approval | `new ApprovalRequiredAIFunction(aiFunction)` | The run ends with `ToolApprovalRequestContent` in the response instead of executing; the app inspects `.ToolCall` (a `FunctionCallContent`), then sends `new ChatMessage(ChatRole.User, [request.CreateResponse(true|false)])` on the same session. Works with `ChatClientAgent` over Chat Completions. This is the MAF-native shape of TechieDesk's `ConfirmEgress`. |
| Chat history (local) | `abstract class ChatHistoryProvider`; `InMemoryChatHistoryProvider` | Chat Completions has no service-managed history, so local history is the path for LM Studio, Ollama, OpenAI Chat Completions. |
| Context injection | `abstract class AIContextProvider` | Override `ProvideAIContextAsync(InvokingContext)` → `AIContext { Instructions, Messages, Tools }` and `StoreAIContextAsync(InvokedContext)`. Session-scoped state via `ProviderSessionState<T>`; no per-session state in provider fields. |
| Built-in RAG | `sealed class TextSearchProvider : MessageAIContextProvider` | Ctor `(Func<string, CancellationToken, Task<IEnumerable<TextSearchResult>>>, TextSearchProviderOptions?, ILoggerFactory?)`. `TextSearchResult { SourceName, SourceLink, Text, RawRepresentation }`. `SearchTime` = `BeforeAIInvoke` (default) or `OnDemandFunctionCalling`; default tool name `"Search"`, default description `"Allows searching for additional information to help answer the user question."` |
| Agent as tool | `agent.AsAIFunction()` | Any `AIAgent` becomes an `AIFunction`. |
| Harness agent | `chatClient.AsHarnessAgent(...)` | **Do not use.** It adds `HostedWebSearchTool` by default and a file-memory provider; both contradict TechieDesk's zero-egress default (REQ-NFR-008) and BRD-99 data locality. |

### 0.3 Model providers relevant to us

- Any `IChatClient` backs a `ChatClientAgent`. That is the contract.
- **OpenAI-compatible (LM Studio, Ollama `/v1`, vLLM):** `new OpenAIClient(new ApiKeyCredential("lm-studio"), new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") }).GetChatClient(model).AsIChatClient()` then `.AsAIAgent(...)`. `AsIChatClient()` is from `Microsoft.Extensions.AI.OpenAI`. Chat Completions is the right API for local servers ("broad model compatibility"); LM Studio also serves `/v1/responses` with function tools since v0.3.29.
- **Ollama native:** Learn uses `OllamaSharp`: `new OllamaApiClient(uri, model).AsAIAgent(...)`. No first-party `Microsoft.Agents.AI.Ollama` .NET package exists.
- **LM Studio tool calling:** supported on `/v1/chat/completions` in the OpenAI `tools` format. Known failure (LM Studio bug tracker #2115): a model without native tool support returns tool-call **text**, not a `tool_calls` array. The live smoke test must assert a real `FunctionCallContent`.

### 0.4 Pipeline order on `RunAsync`

Agent middleware → `ChatHistoryProvider` loads → `AIContextProviders` add messages/tools/instructions → `IChatClient` middleware → model, with `FunctionInvokingChatClient` looping on tool calls → response back → history and context providers notified.

---

## 1. What exists today (verified in-repo, 2026-09-03)

### 1.1 The library

- `TechieRag.slnx`; folders `src/`, `apps/`, `tests/`, `docs/`. No `Directory.Build.props` / `Directory.Packages.props`; every project pins its own versions.
- Core `src/TechieRag`, `net10.0;net8.0`, PackageId `TechieRag` 1.0.0, no project references, references `Azure.AI.OpenAI 2.1.0` (declares `OpenAI >= 2.1.0`, open upper bound). Ships MSBuild targets that write AI-reference files into the consumer's repo on every build (ADR-006).
- Siblings: `TechieRag.Embedded` (net10.0 only), `TechieRag.Telemetry` (net10.0;net8.0, opt-in OpenTelemetry exporters, BRD-117). `TechieRag.Embedded/TechieRagBuilderExtensions.cs` is the out-of-assembly extension precedent.
- Builder `TechieRagBuilder`: instance methods `UseOllama(endpoint, model = "bge-m3")`, `UseLmStudio(endpoint)`, `UseOpenAI(apiKey, model, endpoint)`, `UseCustomEmbeddingProvider(...)`; **and an LLM side:** `UseLlm(...)`, `UseOllamaLlm`, `UseLmStudioLlm`, `UseOpenAICompatibleLlm`, `UseAzureAIFoundryLlm`, `UseGeminiLlm`, `UseAnthropicLlm`, `UseConnectorLlm`, `UseLlmForModel`, `UseCustomLlmProvider`, `WithFallbackLlm`, `WithToolHandler`, `WithTools`, `WithConversationMemory`, `WithPersistence`. `Build()` → `ITechieRag`.
- **Agent stack in core:** `ILlmProvider` (`ChatAsync`/`ChatStreamAsync`, `SupportsToolCalling`; `LlmCompletionOptions.Tools : IReadOnlyList<ToolDefinition>`, `ToolChoice`; `LlmResponse.ToolCalls`), six providers, `RetryHandler`, `FallbackLlmHandler`, `ModelRouter`; `IToolHandler { IReadOnlyList<ToolDefinition> ToolDefinitions; Task<ToolResult> ExecuteToolAsync(ToolCall, ct) }`; `ToolDefinition { Name, Description, ParametersSchema (JSON string), RequiresConfirmation }`; `ToolCall { Id, Name, ArgumentsJson }`; `ToolResult { ToolCallId, Content, IsSuccess, ErrorMessage, FlowMessage? Message }`; `ToolRegistry`, `CompositeToolHandler`, `GuardedToolHandler`; `AgentLoopRunner(ILlmProvider, IToolHandler, logger, maxIterations = 10).RunAsync(List<ChatMessage>, options, IProgress<AgentStep>, ct)`; `AgentStep { Iteration, Kind, ToolName, ToolArgumentsJson, Content, IsSuccess, ErrorMessage, FailureMessage }` with `AgentStepKind` (four loop kinds + seven flow kinds appended), deliberately unsealed so `FlowStep : AgentStep`; `Orchestration/` (`FlowRunner`, `FlowRuntime`, `FlowDefinition`, `IFlowGuardrail`, `FlowRuntime.HostGuardrails`, `AgentToolHandler` agent-as-tool); `Mcp/` (`McpClient`, `McpToolHandler`, `McpAgentExtensions.BuildWorkspaceToolsAsync`); `IConversationMemory`, `IConversationStore`, `WorkspaceManager.SearchScopedAsync` (pinned/selected scope, per-workspace `SimilarityThreshold`, `TopK`, `RerankEnabled`).
- Retrieval surface consumed here: `ITechieRag.SearchAsync(query, topK = 5, documentFilter, ct)`, `SearchAsync(query, SearchOptions?, ct)` (a default interface method that **drops `Rerank`**; `TechieRagClient` overrides it), `ListDocumentsAsync`. `SearchResult { Chunk: TextChunk, Score }`, `TextChunk { Id, DocumentId, Text, PageNumber?, ChunkIndex?, Metadata }`, `Document { Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata }`. Scores are cosine, higher is better, on all three stores; **no score threshold on the core path**; reranking replaces the score scale; only `documentFilter` (exact id), no metadata filter. Default `SqliteVecStore.SearchAsync` is an O(N) managed scan.
- **No `Microsoft.Extensions.AI`, `Microsoft.Agents.AI`, or Semantic Kernel reference anywhere in `src/`, `apps/`, or `tests/`.** Checklist row **REQ-RAG-045 / BRD-126 "Microsoft.Extensions.AI interop package" (GAP-LIB-20, P3, deferred)** is `Not Started 0%`.

### 1.2 TechieDesk (the product this must fit)

From `docs/TechieDesk-BRD.md` (highest ID BRD-145), `docs/TechieDesk-Checklist.md`, `docs/TechieDesk-Architecture.md`:

| Feature | Status | What it means for an agent layer |
|---|---|---|
| **F-AGENT** (BRD-83…86, BRD-138), 88% | `@handle` invocation (REQ-RAG-021); named agents with instructions, model, skill subset, knowledge scope, guardrails: `MaxToolCalls` 8, `TimeLimitSeconds` 90, `RestrictToPinned`, `AllowGeneralKnowledge`, `ShowTrace`, `ConfirmEgress`, `AllowFollowUp` (REQ-UI-045); six skills as library tools (`rag-search`, `web-search`, `web-scrape`, `sql-query`, `chart-generate`, `file-operations`), two-level permission model: workspace catalogue is the outer boundary, agents select within it, **permission enforced by absence** (REQ-RAG-022, `AgentToolPlanner`); execution trace (REQ-UI-034, `AgentTracePanel`); MCP servers per workspace, started per turn, HTTP servers gated by egress, stdio not (REQ-RAG-023). | The agent turn today: `WorkspaceChat.razor` ≈ line 1795 builds `AgentLoopRunner(provider, mcpTools.ToolHandler, maxIterations: agent.MaxToolCalls)`. **A MAF head must reproduce every one of these behaviours, not just the chat.** |
| Egress confirmation (REQ-NFR-013, BRD-99) | `EgressGate` wraps skill invokers; a gated call suspends until the user answers; decline reports `SkillUnavailable`; stock catalogue composes to exactly `[rag-search]`, zero egress by default (REQ-NFR-008). `FlowRuntime.HostGuardrails` is the flow-side seam. | Any MAF agent must go through the same gate. MAF's `ApprovalRequiredAIFunction` is the native equivalent. |
| Retrieval tool today | `WorkspaceSkillTools.RagSearch`: one `query` parameter, description "Searches this workspace's documents and returns the matching passages, honoring the retrieval scope chosen for this turn.", returns chunk texts joined by blank lines, "No matching passages" only on zero hits. Bound in `WorkspaceChat.razor` line 1733 and `WorkspaceFlowService.cs` line 296. | No score, no document name, no page, no citation ref, no status, no hint, no `top_k`, no `document_id`. This is the gap the agentic contract (section 3) fills, for both loops. |
| Agent system prompt | `AgentSystemPrompt`: agent instructions or workspace prompt, plus one honesty sentence when `AllowGeneralKnowledge` is off. | No retrieve-first rule, no query guidance, no re-retrieve protocol. |
| **F-WS / F-RETRIEVE / F-DOCLIB** (BRD-27…48), 75–90% | Workspace-scoped retrieval (BRD-32), per-workspace threshold/topK/rerank (BRD-47), chat vs query mode (BRD-48), pinning (BRD-44), per-turn scope: whole / pinned / chosen documents (BRD-137). | The retrieval tool must run through `WorkspaceManager.SearchScopedAsync` in the app, not raw `ITechieRag.SearchAsync`. The contract must take a search delegate, not only an `ITechieRag`. |
| **F-HIST / F-CITE** (BRD-33…39) | Persisted threads via library memory; streamed citations with name, snippet, score (BRD-39). | Sources must remain typed `SearchResult` so `ToCitations` keeps working; the app owns persistence and passes prior turns as messages. |
| **F-FLOWS / F-SCHED** (BRD-92/93, BRD-123, BRD-139/140), 70% | Library orchestration `FlowRunner` (graphs, handoffs, guardrails, agent-as-tool; "trace is not forked"; zero new packages), builder UI, scheduler helper process, natural-language authoring. | Flows call agents through `ILlmProvider` + `IToolHandler`. A MAF agent joins a flow only as a tool (adapter, section 4) in v1; MAF Workflows are not adopted. |
| **F-LIC** (BRD-49…51) | `AGENTS` is a binary licence feature. | App-level gate, unchanged. |
| **F-I18N** (BRD-91), 95% | All user-facing strings are resource keys; **model-facing text (tool descriptions, instructions) is deliberately invariant English** (REQ-UI-056 policy); trace failures carry `FlowMessage` codes for localization (REQ-RAG-050/051). | The tool description and instructions in section 3 are invariant by policy. Trace adapter must carry `FailureMessage` codes, not English. |
| **BRD-99 / REQ-NFR-008** | Documents, chats, vectors never leave the machine except to the configured provider; no product telemetry; zero egress by default. | No `HarnessAgent`, no MAF hosted tools, no MAF telemetry exporters; sessions stored locally. |
| **F-DESKTOP** (BRD-128…133) | net10.0 MAUI Blazor Hybrid, Mac Catalyst + Windows, single-user, per-user data directory, OS credential store for provider keys. | Provider keys reach the chat client from the credential store via the existing `LlmConfig`, never from the sibling's own settings. |
| **F-LIB governance** (BRD-105…127, §12 single-checklist governance) | The TechieRag BRD/checklist are frozen; **all library work is ledgered in the TechieDesk BRD (append-only IDs) and driven by the TechieDesk checklist.** | This proposal lands as TechieDesk BRD amendments (section 8), not in the TechieRag BRD. |
| ADRs | TechieRag ADR-002 everything behind a provider interface; ADR-003 raw HttpClient, no vendor SDKs in core; ADR-005 additive-only. TechieDesk ADR-001 library-first; ADR-011 named agents with two-level permissions. | MAF goes in a sibling package; the sibling exposes reusable pieces; TechieDesk only surfaces them. |

---

## 2. Positioning (owner direction, 2026-09-03)

1. **Three library packages, one product that demonstrates all three.** `TechieRag` (retrieval, ingestion, providers, the classic loop), `TechieRag.Embedded` (offline embeddings), and **`TechieRag.Agents` (agents on Microsoft Agent Framework)** are the deliverables. TechieDesk is a *live implementation* of their capabilities: it consumes them as NuGet packages, the way any customer does, and it is the place where every package capability is proven on a real screen.
2. **`TechieRag.Agents` is a first-class package, not an optional head.** It is the MAF layer over TechieRag and the Microsoft.Extensions.AI interop package (this re-scopes and pulls forward BRD-126 / REQ-RAG-045 / GAP-LIB-20). TechieDesk's agent turns, named agents, flows-with-agents, and scheduled agent runs move onto it. Core's `AgentLoopRunner` stays shipped and supported for consumers who want zero dependencies (ADR-005 additive-only), but TechieDesk stops being its consumer.
3. **The package adapts at the seams TechieRag already exposes.** `ILlmProvider` → `IChatClient`, `IToolHandler` ↔ `AITool`, MAF middleware → `IProgress<AgentStep>`, `IConversationMemory` → `ChatHistoryProvider`. This is what lets TechieDesk keep one provider configuration, one skill catalogue with permission-by-absence, one egress gate, one MCP registry, and one trace renderer while running on MAF. Because TechieDesk consumes packages, **every one of these adapters must be public library API**; nothing app-shaped is allowed to leak into them.
4. **The agentic retrieval contract lives in core, dependency-free.** Tool description, JSON schema, structured result with refs and status, hints, and the default retrieve-first instructions are plain types in `TechieRag` (namespace `TechieRag.Agentic`, zero packages). `TechieRag.Agents` binds it to MAF; `ToolRegistry` users bind it to the classic loop; both loops answer the same way.
5. **TechieDesk moves to its own repository.** The library repo ships packages; the app repo pins package versions and proves them. Section 10 has the plan.

---

## 3. The agentic retrieval contract (core, zero new packages)

Namespace `TechieRag.Agentic` in `src/TechieRag`. Additive; no `ITechieRag` change.

### 3.1 Types

```csharp
public interface IRetrievalSource                       // what the tool searches
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK, string? documentId, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken ct);
}
public sealed class TechieRagRetrievalSource : IRetrievalSource      // over ITechieRag (calls the SearchOptions overload, forces Rerank per options)
public sealed class DelegateRetrievalSource : IRetrievalSource       // over Func<...>; TechieDesk wraps WorkspaceManager.SearchScopedAsync

public sealed class RetrievalToolOptions { TopK = 5, MaxTopK = 20, MaxSearchesPerTurn = 4, WeakScoreThreshold = 0.55f, NoneScoreThreshold = 0.35f, Rerank = false, RerankWeakThreshold = null, MaxChunkChars = 1500, DocumentFilter = null, IncludeScores = true, ToolName = "search_knowledge_base", ListToolName = "list_documents" }

public sealed class RetrievalTurnState                 // per turn: searches used, ref counter, ref-by-chunk-id map, collected SearchResults
public sealed record RetrievalTrace(string Query, int TopK, string? DocumentId, string Status, float? BestScore, IReadOnlyList<string> Refs, TimeSpan Duration);

public static class KnowledgeBaseTools                 // the contract itself
{
    public const string SearchDescription = "...";      // section 3.2
    public const string SearchParametersSchema = "...";  // JSON schema with the parameter descriptions in section 3.2
    public const string ListDescription = "...";
    public static ToolDefinition SearchDefinition(RetrievalToolOptions o);   // for IToolHandler / ToolRegistry users
    public static ToolDefinition ListDefinition(RetrievalToolOptions o);
    public static Task<string> ExecuteSearchAsync(IRetrievalSource src, RetrievalToolOptions o, RetrievalTurnState state, string argumentsJson, CancellationToken ct); // returns the JSON in 3.3
    public static Task<string> ExecuteListAsync(IRetrievalSource src, CancellationToken ct);
}
public static class AgenticInstructions { public const string Default = "..."; public static string WithDomainGuidance(string extra); }  // section 3.4

// convenience for the existing loop:
public static class ToolRegistryExtensions { public static ToolRegistry RegisterKnowledgeBase(this ToolRegistry r, IRetrievalSource src, RetrievalToolOptions? o, RetrievalTurnState state); }
```

### 3.2 Tool descriptions (the part that matters)

**`search_knowledge_base`**

> Search the user's ingested documents for passages relevant to a query. This is the only source of facts about those documents; never answer questions about their contents from memory. Returns the best-matching passages, each with a citation ref (S1, S2, ...), source document, page, relevance score from 0 to 1, and text. The result also reports match quality as "strong", "weak", "none", or "limit_reached". If quality is "weak" or "none", do not answer yet: call this tool again with a different query. Rephrase using other words the document would use, split a compound question into one concept per call, or restrict to one document using an id from list_documents. Write the query as the words likely to appear in the relevant passage (3 to 12 words), not as a sentence or a question, and never include instructions to yourself in it.

| Parameter | Type | Description |
|---|---|---|
| `query` | string, required | "Words likely to appear in the passage you need. One concept per call. Not the user's whole message." |
| `top_k` | integer, optional | "How many passages to return, 1 to 20. Default 5. Use more for broad questions, fewer for a precise fact." |
| `document_id` | string, optional | "Restrict the search to one document. Use an id from list_documents. Omit to search everything." |

**`list_documents`** (no parameters)

> List the documents currently in the knowledge base, with id, name, and how many passages each contains. Call this when the user names or asks about a specific document, asks what is available, or when search_knowledge_base returned weak results and restricting to one document might help. Do not call it before every search.

Names are snake_case: local models call them more reliably, and MAF passes them through unchanged. `WorkspaceSkillTools.RagSearch` keeps its catalogue name `rag-search` for the permission model and maps to this contract internally.

### 3.3 Tool result (JSON string; identical from `IToolHandler` and from MAF)

```json
{
  "status": "weak",
  "best_score": 0.48,
  "searches_used": 1,
  "searches_remaining": 3,
  "results": [
    { "ref": "S1", "document": "Returns Policy 2026.pdf", "document_id": "d7c1…", "page": 3, "chunk_index": 14, "score": 0.48, "text": "…" }
  ],
  "hint": "No passage scored above 0.55. Search again with different terminology, or narrow to a single document from list_documents. Do not answer from memory."
}
```

| status | hint |
|---|---|
| `strong` | "Answer from these passages and cite them by ref. Search again only if the question has a part these passages do not cover." |
| `weak` | "No passage scored above {threshold}. Search again with different terminology, or narrow to a single document from list_documents. Do not answer from memory." |
| `none` | "Nothing relevant was found. Try one more query using different words. If that also finds nothing, tell the user the documents do not appear to cover this and say what you searched for." |
| `limit_reached` | "You have used all {n} searches for this turn. Answer now from the passages already retrieved, and state clearly which parts of the question you could not find support for." |

Rules: refs continue across searches in a turn and across turns in a session, deduplicated by `Chunk.Id`; `text` truncated to `MaxChunkChars`; scores rounded to two decimals; status classified on `best_score` only when `Rerank == false` (cosine scale), otherwise `strong` if any result else `none` unless `RerankWeakThreshold` is set; full `SearchResult` objects are kept in `RetrievalTurnState.Collected` for typed citations; cancellation flows through.

In TechieDesk the app's `WorkspaceManager.SearchScopedAsync` already applies the workspace threshold before the tool sees results, so with a threshold configured, `weak` mostly collapses into `none`. That is correct behaviour, not a conflict.

### 3.4 Default instructions

Exposed as `AgenticInstructions.Default`; roughly 260 words; written for small local models; refers to the `status` values literally so the prompt and the tool result reinforce each other.

```
You answer questions using a private document knowledge base. You reach it only through the search_knowledge_base and list_documents tools.

RETRIEVE FIRST
1. Before making any factual statement about the documents, call search_knowledge_base. Do this even if you think you know the answer.
2. The only exceptions: greetings, questions about what you can do, and requests that only reformat or summarise passages already retrieved in this conversation.
3. Write queries as the words a matching passage would contain, 3 to 12 words. Not a question, not the user's whole message.
4. Ask for one concept per search. Split compound questions into separate searches.

JUDGE THE RESULTS
5. Every result has a status. "strong": answer from it. "weak" or "none": do not answer yet; search again.
6. When searching again, change something: use synonyms or the document's own vocabulary, narrow with document_id from list_documents, or raise top_k for broad questions.
7. You have a limited number of searches per turn; the result tells you how many remain. When it says limit_reached, stop searching and answer with what you have.

ANSWER
8. Use only retrieved passages. Never add facts from memory, even plausible ones.
9. Cite each claim with the passage ref in square brackets, for example [S2]. Only cite refs that were returned to you.
10. If, after searching, the passages do not answer the question, say so plainly, name what you searched for, and suggest what the user could add or clarify. Do not guess.
11. Do not mention scores, tool names, or statuses to the user. Do not show your search process unless asked.

CONVERSATION
12. For a follow-up on the same topic, you may cite passages already retrieved. For a new topic, search again.
13. Keep answers concise and in the user's language.
```

`AgenticInstructions.WithDomainGuidance(extra)` appends a `DOMAIN GUIDANCE` block and never edits the numbered lines. In TechieDesk, `AgentSystemPrompt` becomes: default protocol, then the agent's instructions or workspace prompt as domain guidance, then (when `AllowGeneralKnowledge` is on) a one-line relaxation of rule 8 ("You may add general knowledge, but label it as such and keep document facts cited."). When it is off, the existing honesty clause is already covered by rules 8 and 10.

### 3.5 Back-port into TechieDesk (Phase A, section 7)

- `WorkspaceSkillTools.RagSearch` takes a `DelegateRetrievalSource` over `SearchScopedAsync` and returns `KnowledgeBaseTools.ExecuteSearchAsync(...)`; schema becomes `SearchParametersSchema`; description becomes `SearchDescription`. Add a `list_documents` skill implementation under the same `rag-search` catalogue permission (one permission, two tools; the catalogue is about egress and capability, and listing documents is the same capability as searching them).
- `AgentSystemPrompt` composes from `AgenticInstructions`.
- `RetrievalTurnState.Collected` replaces the `retrieved` list the razor page keeps by hand, so `ToCitations` and `streamingSources` keep working.
- `WorkspaceFlowService.BuildToolsAsync` gets the same binding, so flows benefit too.

---

## 4. `TechieRag.Agents`: the MAF package

### 4.1 Layout and references

```
src/TechieRag.Agents/
  TechieRag.Agents.csproj                 net10.0;net8.0 · ProjectReference TechieRag · PackageId TechieRag.Agents 1.0.0 · no build targets
  TechieRagAgentBuilder.cs                fluent builder, instance methods
  ITechieRagAgent.cs / TechieRagAgent.cs
  Retrieval/RetrievalContextProvider.cs   AIContextProvider: supplies the two tools as AIFunctions over KnowledgeBaseTools, owns RetrievalTurnState in session state
  Interop/LlmProviderChatClient.cs        ILlmProvider  → IChatClient           (adapter 1)
  Interop/ToolHandlerFunctions.cs         IToolHandler  → IList<AITool>         (adapter 2a)  ToolDefinition.RequiresConfirmation → ApprovalRequiredAIFunction
  Interop/AIToolHandler.cs                AITool/AIAgent → IToolHandler         (adapter 2b)  MAF agent or MEAI tool usable by AgentLoopRunner and FlowRunner
  Interop/AgentStepReporter.cs            MAF middleware → IProgress<AgentStep> (adapter 3)   emits the four loop kinds only
  Interop/ConversationMemoryChatHistoryProvider.cs   IConversationMemory → ChatHistoryProvider (adapter 4, phase D)
  ChatClients/OpenAICompatibleChatClientFactory.cs   LM Studio / Ollama v1 / OpenAI / compatible → IChatClient
  DependencyInjection/ServiceCollectionExtensions.cs AddTechieRagAgent(...)
tests/TechieRag.Agents.Tests/               scripted IChatClient fake, fake ITechieRag, fake IToolHandler; Live/ gated by [LiveNetworkFact]
```

| PackageReference | Version | Why |
|---|---|---|
| `Microsoft.Agents.AI` | 1.20.0 | Agent, session, context providers, middleware, `ApprovalRequiredAIFunction`. |
| `Microsoft.Extensions.AI` | 10.9.0 | `AIFunction`, `FunctionInvokingChatClient`, `ChatClientBuilder`. |
| `Microsoft.Extensions.AI.OpenAI` | 10.9.0 | Only for the `UseLmStudio` / `UseOllama` / `UseOpenAI` / `UseOpenAICompatible` conveniences. Brings `OpenAI` 2.12.x. |
| `Microsoft.Extensions.Logging.Abstractions`, `.DependencyInjection.Abstractions` | 10.0.3 | Match core. |

Not referenced: `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Workflows`, `OllamaSharp`, `Azure.AI.OpenAI`. TechieDesk does not need the OpenAI SDK route at all; it uses adapter 1 and keeps one provider configuration.

Dependency ripple: `OpenAI` unifies to 2.12.x in any app that references both packages; restore is clean because `Azure.AI.OpenAI 2.1.0` has an open upper bound; add a test that constructs core's `AzureOpenAIEmbeddingProvider` from the sibling's test project.

### 4.2 The four adapters (how "all that" is handled)

| Seam | Adapter | Direction | What it preserves | Notes |
|---|---|---|---|---|
| Model | `LlmProviderChatClient : IChatClient` | core → MAF | All six providers, `ModelRouter`, `RetryHandler`, `FallbackLlmHandler`, `TokenUsageTracker` events, prompt caching, vision parts, credential-store keys. | Maps MEAI `ChatMessage`/`ChatOptions.Tools` to core `ChatMessage`/`LlmCompletionOptions.Tools` (`ToolDefinition` from `AIFunctionDeclaration.JsonSchema`), `LlmResponse.ToolCalls` to `FunctionCallContent`. `ChatStreamAsync` yields text only, so streaming with tools runs non-streaming per model call and streams the final text; documented. Requires `SupportsToolCalling`; otherwise throws at build time with the provider name. |
| Tools | `ToolHandlerFunctions.From(IToolHandler)` → `IList<AITool>` | core → MAF | The six skills, `AgentToolPlanner` permission-by-absence, `CompositeToolHandler`, `GuardedToolHandler`, `McpToolHandler` with server provenance, `EgressGate` wrapping (it wraps invokers below this seam, so it keeps working unchanged). | One `AIFunction` subclass per `ToolDefinition`: raw `JsonSchema`, `InvokeCoreAsync` builds a `ToolCall` and calls `ExecuteToolAsync`; `ToolResult.IsSuccess == false` returns the error text to the model as today. `RequiresConfirmation == true` wraps in `ApprovalRequiredAIFunction` so the MAF-native approval flow is available where a host wants it. |
| Tools (reverse) | `AIToolHandler : IToolHandler` over `AITool[]` or `AIAgent.AsAIFunction()` | MAF → core | `AgentLoopRunner`, `FlowRunner` Agent/Tool nodes, `HostGuardrails`. | Lets a MAF agent be a flow node or a tool of the classic loop. Phase D. |
| Trace | `AgentStepReporter` (agent-run + function middleware) | MAF → app | `AgentTracePanel`, `AgentTrace`, REQ-UI-034, `FailureMessage` codes. | Emits `ToolCallRequested`, `ToolExecuted`, `FinalAnswer`, `MaxIterationsReached` only, on `IProgress<AgentStep>`. No new `AgentStepKind`. `ToolResult.Message` codes flow into `AgentStep.FailureMessage` through adapter 2. |
| Memory | `ConversationMemoryChatHistoryProvider` | core → MAF | BRD-33 persistence through the library store. | Phase D. In v1 the app passes prior turns as messages, as it does today, with `InMemoryChatHistoryProvider` per turn. |
| Egress | (a) unchanged: `EgressGate` below adapter 2; (b) `ApprovalRequiredAIFunction` + `ToolApprovalRequestContent` handled by the page | app | REQ-NFR-013 | v1 ships (a) so behaviour is identical. (b) is offered as a later option; it has one real advantage: an approval request survives session serialization, so a scheduled or background run (BRD-139) can park on approval. |

### 4.3 Public API (existing fluent style)

```csharp
// library consumer with no core LLM configured: OpenAI SDK route
var agent = new TechieRagAgentBuilder(rag)
    .UseLmStudio("http://localhost:1234", model: "qwen3-8b")
    .WithRetrieval(r => { r.TopK = 5; r.MaxSearchesPerTurn = 4; })
    .Build();

// TechieDesk, or any consumer that already configured UseLmStudioLlm()/UseConnectorLlm(): adapter 1
var agent = new TechieRagAgentBuilder(rag)
    .UseConfiguredLlm()                                  // rag.GetLlmProvider() via LlmProviderChatClient
    .UseRetrievalSource(new DelegateRetrievalSource(...)) // WorkspaceManager.SearchScopedAsync with the turn's scope
    .WithToolHandler(mcpTools.ToolHandler)               // adapter 2: the permitted skills + MCP tools, egress-gated
    .WithMaxToolIterations(agentDefinition.MaxToolCalls)
    .WithAdditionalInstructions(agentDefinition.Instructions)
    .WithTrace(progress)                                 // adapter 3: IProgress<AgentStep>
    .Build();
```

| Method | Signature | Notes |
|---|---|---|
| `UseLmStudio` | `(string endpoint = "http://localhost:1234", string model)` | `model` **required** (decision 2 of v1, resolved: a wrong model string is the first thing a new user hits). Chat Completions at `{endpoint}/v1`, placeholder key. |
| `UseOllama` | `(string endpoint = "http://localhost:11434", string model = "llama3.2")` | Ollama `/v1`. Defaults mirror `UseOllamaLlm`. |
| `UseOpenAI` | `(string apiKey, string model = "gpt-4o-mini", string endpoint = "https://api.openai.com")` | Same parameter order as core; `/v1` appended internally. |
| `UseOpenAICompatible` | `(string endpoint, string? apiKey = null, string model)` | Endpoint given with `/v1`. |
| `UseConfiguredLlm` | `()` | Adapter 1 over `rag.GetLlmProvider()`; throws with a clear message if core has `LlmSource.None`. |
| `UseCustomChatClient` | `(Func<IChatClient>)` | Escape hatch, mirrors `UseCustomLlmProvider`. OllamaSharp, Azure OpenAI, Anthropic, Foundry come in here without new references. |
| `UseRetrievalSource` | `(IRetrievalSource)` | Default `TechieRagRetrievalSource(rag)`. |
| `WithRetrieval` | `(Action<RetrievalToolOptions>)` | Section 3.1. |
| `WithToolHandler` | `(IToolHandler)` | Adapter 2. Same name as core's `WithToolHandler`. |
| `WithTools` | `(params AITool[])` | Native MAF tools. |
| `WithInstructions` / `WithAdditionalInstructions` | `(string)` | Replace / append to `AgenticInstructions.Default`. |
| `WithMaxToolIterations` | `(int max = 8)` | Builds `FunctionInvokingChatClient` ourselves with `MaximumIterationsPerRequest`; matches `AgentDefinition.DefaultMaxToolCalls`. |
| `WithTrace` | `(IProgress<AgentStep>)` | Adapter 3. |
| `WithChatOptions` | `(Action<ChatOptions>)` | Temperature, max output tokens; instructions and tools are applied after, so they cannot be clobbered. |
| `WithChatHistoryProvider` | `(ChatHistoryProvider)` | Default in-memory. |
| `WithPrefetch` | `(bool = true)` | Adds MAF `TextSearchProvider` in `BeforeAIInvoke` mode in addition to the on-demand tools, for models reluctant to call tools on turn one. Off by default. |
| `WithLogging`, `WithName` | | As core. |
| `Build` | `() → ITechieRagAgent` | |

```csharp
public interface ITechieRagAgent
{
    AIAgent Agent { get; }                       // middleware, workflows, AsAIFunction(), hosting: all reachable
    ITechieRag Rag { get; }
    Task<AgentSession> CreateSessionAsync(CancellationToken ct = default);
    Task<AgentRagResponse> AskAsync(string question, AgentSession? session = null, CancellationToken ct = default);
    Task<AgentRagResponse> AskAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, CancellationToken ct = default); // app passes prior turns
    IAsyncEnumerable<RagStreamEvent> AskStreamAsync(string question, AgentSession? session = null, CancellationToken ct = default);   // Sources after each search, Token, Completed
}
public sealed class AgentRagResponse { string Answer; IReadOnlyList<SearchResult> Sources; IReadOnlyList<RetrievalTrace> Searches; IReadOnlyList<ToolApprovalRequestContent> PendingApprovals; AgentResponse Raw; }
```

MAF types are exposed on purpose, not wrapped: hiding them would recreate the parallel-abstraction problem and lock users out of the reason to adopt MAF.

DI: `services.AddTechieRagAgent(Action<TechieRagAgentBuilder>)` resolves `ITechieRag` and `ILoggerFactory` from the container; registers `ITechieRagAgent` and a keyed `AIAgent` (`"techierag"`).

### 4.4 Constraints carried from the BRD

- No `HarnessAgent`, no MAF hosted tools, no MAF exporters: zero egress by default holds (REQ-NFR-008, BRD-99). MAF's own OpenTelemetry instrumentation is inert without an exporter, the same posture as core's.
- Model-facing strings are invariant English (REQ-UI-056 policy). User-facing failures go through `FlowMessage` codes.
- Sessions are serialized to the per-user data directory if persisted at all (BRD-130); they must never carry provider keys (BRD-144 archive rule).
- The `AGENTS` licence feature gates the UI, not the library.
- `net10.0;net8.0`, no MSBuild targets, `InternalsVisibleTo("TechieRag.Agents.Tests")`.

---

## 5. TechieDesk as the live implementation of TechieRag.Agents

TechieDesk consumes `TechieRag.Agents` from NuGet and switches its agent runtime to it. Nothing about the product's behaviour changes; what changes is which package runs the loop.

- **Agent turn.** `WorkspaceChat.razor` and `WorkspaceFlowService` compose exactly as today up to `mcpTools.ToolHandler`, then: `new TechieRagAgentBuilder(rag).UseConfiguredLlm().UseRetrievalSource(new DelegateRetrievalSource(SearchScopedAsync with the turn's scope)).WithToolHandler(mcpTools.ToolHandler).WithMaxToolIterations(agent.MaxToolCalls).WithAdditionalInstructions(agent.Instructions ?? workspace.SystemPrompt).WithTrace(progress).Build().AskAsync(conversation, ct)`. Timeout token, egress gate, permission intersection, MCP lifetime, trace rendering, citations: unchanged, because every one of them sits at a seam the package adapts.
- **`rag-search` skill.** Bound to the core contract (`KnowledgeBaseTools` over `DelegateRetrievalSource`) and joined by `list_documents` under the same catalogue permission. `RetrievalTurnState.Collected` replaces the hand-kept `retrieved` list so `ToCitations` keeps working.
- **System prompt.** `AgentSystemPrompt` = `AgenticInstructions.Default` + the agent's instructions or the workspace prompt as domain guidance + a one-line relaxation of rule 8 when `AllowGeneralKnowledge` is on.
- **Named agents (BRD-138).** `AgentDefinition` needs no new field. `Model` → `ChatOptions.ModelId` through adapter 1's router; `MaxToolCalls` → `WithMaxToolIterations`; `TimeLimitSeconds` → the cancellation token; `RestrictToPinned` and per-turn scope → the retrieval source; `ConfirmEgress` → `EgressGate` below the tool adapter (v1) or `ApprovalRequiredAIFunction` (later); `ShowTrace` → adapter 3; `AllowFollowUp` → a serialized `AgentSession` with a pending approval is a resumable run (later).
- **Flows (BRD-92/123).** `FlowRunner` stays the engine. Adapter 2b (`AIToolHandler`) makes a MAF agent an Agent node or a tool, so flows run MAF agents from day one of the switch; `HostGuardrails` still see every tool call because the adapter sits below them.
- **Scheduled agent runs (BRD-93/139).** The scheduler helper hosts the same code; no change beyond the package reference.
- **Licence gate `AGENTS`, i18n keys, trace `FailureMessage` codes, zero-egress default:** unchanged and re-verified.
- **What TechieDesk stops using:** `AgentLoopRunner` directly. It remains in core for library consumers.

---

## 6. What in the existing design fights this (updated)

1. **Two loops.** Now a feature, not a conflict: same seams, same contract, opt-in per agent. The cost is one adapter per seam, all small, all tested against fakes.
2. **Builder names.** Core has `UseLmStudioLlm`; the sibling has `UseLmStudio` on a different builder class. The README shows both builders together; `UseConfiguredLlm()` is the recommended path when core already has an LLM.
3. **No score threshold on the core path; rerank replaces the scale.** Handled by `Rerank` forced per call and thresholds applied on the cosine path only. In TechieDesk the workspace threshold applies first.
4. **`SearchAsync(string, SearchOptions?)` default interface method drops `Rerank`.** `TechieRagRetrievalSource` calls it deliberately; documented for decorators.
5. **No metadata filter.** `list_documents` + `document_id` in v1; `SearchOptions.MetadataFilter` is a candidate core addition, out of scope.
6. **Default SQLite store is a full scan per search.** Multiplied by up to four searches per turn. Not a blocker; steer large corpora to Qdrant.
7. **`ILlmProvider.ChatStreamAsync` streams text only.** Adapter 1 cannot stream tool-call deltas; it runs each model call non-streaming inside the loop and streams the final answer. Same user-visible behaviour as `AgentLoopRunner` today, which is non-streaming.
8. **`ToolDefinition.ParametersSchema` is a string; `AIFunctionDeclaration.JsonSchema` is a `JsonElement`.** Parse once at adapter construction; malformed schema throws at build time, not at call time.
9. **`OpenAI` SDK unification** to 2.12.x in apps that reference both packages. Restore is clean; add the compatibility test.
10. **Trace kinds.** The MAF loop can produce content the four loop kinds cannot express (approval requests, reasoning text). v1 drops them from the trace; if the product wants them, that is an appended `AgentStepKind`, never a fork, per the REQ-RAG-042 rule.
11. **`AgentStep.Iteration` semantics.** `FunctionInvokingChatClient` does not expose an iteration counter to middleware; adapter 3 counts model round-trips itself.
12. **Local model tool-call reliability.** `WithPrefetch()` for reluctant models; the live smoke test asserts a real `FunctionCallContent` on a tool-capable model.
13. **MSBuild targets** stay in core only; the AI-reference content file in core gains an "agent layer" section rather than a second package writing to the same paths.

---

## 7. Phasing

| Phase | Repo | Delivers | Proof |
|---|---|---|---|
| **0. Repository separation** | both | Section 10. TechieDesk in its own repo consuming `TechieRag` and `TechieRag.Embedded` from packages at a pinned version; library repo without `apps/`. | Both repos build and test green on their own; the desktop head launches from packages. |
| **A. Agentic retrieval contract** | TechieRag (core) | Section 3: `TechieRag.Agentic` types, tool description and schema, result and hints, instructions, `ToolRegistry.RegisterKnowledgeBase`. Zero new packages. Published as a prerelease. | Hermetic tests on classification, refs, truncation, budget. |
| **B. `TechieRag.Agents` 1.0** | TechieRag (new package) | Builder, `ITechieRagAgent`, `RetrievalContextProvider`, adapters 1, 2a, 2b, 3; OpenAI-SDK `Use*` methods; DI; tests; `[LiveNetworkFact]` LM Studio smoke. Closes BRD-126 as re-scoped. | Scripted-`IChatClient` loop tests (weak → re-search → answer; cap → answer with gaps); real `FunctionCallContent` on LM Studio. |
| **C. TechieDesk on TechieRag.Agents** | TechieDesk | Package reference to `TechieRag.Agents`; agent turn, `rag-search` + `list_documents`, system prompt, flows via adapter 2b, scheduler; `AgentLoopRunner` usage removed from the app. | Existing TechieDesk.Tests agent suites pass against the new runtime; live smoke of an `@agent` turn with a tool call, egress prompt, and trace. |
| **D. On demand** | TechieRag.Agents | Adapter 4 (history over `IConversationStore`), MAF-native approval with resumable sessions for `AllowFollowUp` and background runs, `UseAzureOpenAI` / OllamaSharp conveniences, MAF Workflows if a case appears. | As each lands. |

Order: 0 first, because A and B then ship as packages the app pins, which is the whole point of the split. A and B are one library release train; C follows the first prerelease that contains both.

---

## 8. Governance: two ledgers again

The 2026-07-17 "single-checklist governance" put all library work into the TechieDesk BRD (F-LIB, BRD-105…127) because both codebases lived in one repo. With the split, that rule inverts back:

- **The TechieRag BRD becomes the live library ledger again.** It is already in use in practice: DECISIONS.md (2026-09-03) records `REQ-FN-003`/`REQ-FN-004` publishing defects in `docs/TechieRag-Checklist.md`. Its highest ID is BRD-82; new library requirements continue from BRD-83. Open F-LIB rows in the TechieDesk checklist that are library work (`REQ-RAG-036`, `042`, `044`, `045`, `046`, `050`, `051`, `052`) migrate to the TechieRag checklist with their status and remarks preserved; the TechieDesk rows are closed as "moved to TechieRag REQ-…", never deleted.
- **The TechieDesk BRD keeps app requirements** and states its library dependencies as package versions ("needs TechieRag.Agents ≥ 1.0.0-preview.N"), the way BRD-105…127 already name dependencies inline.

Proposed amendments, applied with `*amend-docs` on each side after agreement:

| Ledger | ID | Feature | Requirement (draft wording) |
|---|---|---|---|
| TechieRag | **BRD-83** | F-AGENT (library) | Agentic retrieval contract in core (`TechieRag.Agentic`): search and list tools with a stable description, JSON schema, a structured result carrying citation refs, relevance score, and a strong/weak/none/limit_reached status with a next-step hint; a per-turn search budget; default retrieve-first instructions. Zero new packages; usable from `ToolRegistry`, flows, and `TechieRag.Agents`. |
| TechieRag | **BRD-84** | F-AGENTS-PKG (new feature) | `TechieRag.Agents` package on Microsoft Agent Framework 1.20: fluent builder in the existing style with LM Studio as the primary local target; agentic retrieval context provider over BRD-83; public adapters at the `ILlmProvider`, `IToolHandler` (both directions), `IProgress<AgentStep>` and `IConversationMemory` seams; DI registration; `net10.0;net8.0`. Supersedes the deferred "Microsoft.Extensions.AI interop" item (TechieDesk BRD-126 / GAP-LIB-20), which closes as "delivered by TechieRag BRD-84". |
| TechieRag | **BRD-85** | F-PKG | Repository separation: `apps/`, app tests, app docs, app workflows, and app verification harness leave this repository; the README's sample-application section links to the TechieDesk repository; the solution contains library and library-test projects only. |
| TechieDesk | **BRD-146** | F-DESKTOP / F-DATA | TechieDesk lives in its own repository and consumes `TechieRag`, `TechieRag.Embedded`, and `TechieRag.Agents` as NuGet packages at pinned versions (central package management); prerelease builds come from GitHub Packages, releases from nuget.org; no `ProjectReference` to any library project. |
| TechieDesk | **BRD-147** | F-AGENT | The agent runtime is `TechieRag.Agents`: `@handle` turns, named agents, flows with agent nodes, and scheduled agent runs execute on it, honouring the skill catalogue intersection, egress confirmation, MCP servers, max tool calls, time limit, trace, and citations exactly as before; the `rag-search` skill and the agent system prompt use the library's agentic retrieval contract and `list_documents` is offered under the same catalogue permission. |
| TechieDesk | **BRD-126** (amend) | F-LIB | Closed as delivered by TechieRag BRD-84; the F-LIB feature itself is retired for new work with the note that library requirements are ledgered in the TechieRag BRD from 2026-09-03. |

---

## 9. Decisions for you

1. **Repository separation first** (Phase 0), then the library release train (A + B), then TechieDesk on the new packages (C). Agree with the order?
2. **Library governance returns to the TechieRag BRD/checklist**, open F-LIB rows migrate, TechieDesk BRD keeps app-only. Agree?
3. **Contract in core** (`TechieRag.Agentic`, zero packages) with `TechieRag.Agents` binding it. Agree?
4. **All four adapters are public library API** in `TechieRag.Agents` (they are what lets a package-consuming app keep one catalogue, one gate, one trace). Agree?
5. **`list_documents` under the existing `rag-search` catalogue permission.** Agree?
6. **Egress in v1 stays `EgressGate` below the tool adapter**; MAF-native approval with resumable sessions comes in D. Agree?
7. **Streaming on the MAF loop**: model calls inside the tool loop run non-streaming; only the final answer streams. Acceptable for 1.0?
8. **Thresholds** 0.55 / 0.35 as starting points for bge-m3 cosine, configurable.
9. **Dev loop across two repos** (section 10.4): a local folder feed for same-day iteration, GitHub Packages prerelease for anything shared. Agree?

---

## 10. Repository separation plan

### 10.1 What is coupled today (verified on disk)

- `apps/TechieDesk.Core` has `ProjectReference`s to `src/TechieRag` and `src/TechieRag.Embedded`; `apps/TechieDesk`, `apps/TechieDeskScheduler`, `apps/TechieDeskDb.Cli` reference app projects only; `tests/TechieDesk.Tests` references app projects only. **No app project uses library internals** (`InternalsVisibleTo` names only `TechieRag.Tests`), so package references are a drop-in replacement.
- Library tests reference only `src/*`. Clean.
- One solution file `TechieRag.slnx` with `/src/`, `/apps/`, `/tests/`, `/docs/` folders.
- Workflows: `publish-github-packages.yml` and `publish-nuget.yml` are library-only (they pack `TechieRag` and `TechieRag.Embedded`); `publish-desktop.yml` is app-only. `.github/workflows/scripts/determine-version.sh` is library.
- Publishing model (DECISIONS.md 2026-08-09 and 2026-09-03): GitHub Packages is the dev feed, fed automatically on push to `main` and on release tags; nuget.org is public, `workflow_dispatch` only, version derived from the release tag. **This is exactly the mechanism a separate app repo needs**; nothing new has to be invented.
- Docs: `docs/TechieDesk-*.md/.html`, `docs/devguides/TechieDesk-DevGuide-*`, `docs/mockups/*`, `docs/screenshots/TechieDesk/`, `docs/TechieDesk-Checklist.md`, `uiIssues/`, `tests/verify/*.spec.ts` + `playwright.config.ts`, `tests/appium/` are app-owned. `docs/TechieRag-*`, `docs/TechieRag.Embedded-UserGuide.md`, `docs/NUGET-PUBLISHING-GUIDE.md`, `NUGET-PUBLISHING.md`, `docs/screenshots/TechieRag/`, `docs/TechieRag-CompetitorAnalysis.md` are library-owned. Shared: `PROJECT-STATUS.md`, `DECISIONS.md`, `.tfcore/` (framework, copied per repo), `.editorconfig`, `WORKFLOW.html`, `docs/metrics`.
- Core's packaged MSBuild targets (ADR-006) write `.techierag/` and agent command files into the **consumer's** repo. After the split, TechieDesk becomes a real consumer and receives them on build, which is the intended dogfooding; the `.techierag/` folder in the library repo itself is then only the packing source.

### 10.2 What moves to the new repository (`TechieDesk`)

`apps/*` (five projects) · `tests/TechieDesk.Tests` · `tests/appium` · `tests/verify` + `playwright.config.ts` · `.github/workflows/publish-desktop.yml` · `docs/TechieDesk-*` · `docs/devguides` · `docs/mockups` · `docs/screenshots/TechieDesk` · `uiIssues/` · app entries of `DECISIONS.md` and `PROJECT-STATUS.md` (copied, then trimmed on each side) · a copy of `.tfcore/` with `core-config.yaml` `metrics.project_type: app` and `runtimeVerification` (Appium/FlaUI) · `.editorconfig` · `.trblazeui/` if it holds app-side TrBlazeUI material.

Stays: `src/*`, `tests/TechieRag.Tests`, library workflows and scripts, library docs, `README.md` (sample-application section becomes a link), `.tfcore/` with `project_type: library` (the metrics note in `core-config.yaml` says gate metrics are only comparable within a type, so this split also makes the telemetry honest).

### 10.3 Mechanics, in order

1. **Cut a library baseline release** from the current tree (release → tag → GitHub Packages automatically; nuget.org by dispatch). This is the version the app pins on day one, so the split changes no behaviour.
2. **Create the `TechieDesk` repo** with the moved paths. History: the owner does git; `git subtree split` or `git filter-repo --path apps/ --path tests/TechieDesk.Tests ...` preserves history for the moved paths if wanted; a plain copy is acceptable if the library repo keeps the history.
3. **Replace `ProjectReference` with `PackageReference`** in `apps/TechieDesk.Core` (`TechieRag`, `TechieRag.Embedded` at the baseline version; later `TechieRag.Agents`). Add `Directory.Packages.props` with central package management from the start: the version-drift the earlier code map found (`Microsoft.Data.Sqlite` 10.0.3 vs 10.0.10, `Qdrant.Client` 1.16.1 vs 1.18.1) is precisely what a single pin file prevents. Add a `NuGet.config` with the GitHub Packages source for prereleases and a `local` folder source for the dev loop.
4. **New `TechieDesk.slnx`**; remove app projects and folders from `TechieRag.slnx`.
5. **Docs and ledgers**: `*amend-docs` on both sides per section 8; `PROJECT-STATUS.md` and `DECISIONS.md` split with a cross-link; TechieRag README "Sample Application" → link to the new repo.
6. **Verify both repos independently**: library `dotnet build` + tests green with `apps/` gone; app `dotnet build` + TechieDesk.Tests green from packages; the Catalyst head launches and the 23-screen sweep runs from the app repo.
7. **Delete `apps/` and app docs from the library repo** only after step 6 passes.

### 10.4 The dev loop across two repos

- **Shared change (default):** push to library `main` → GitHub Packages gets a prerelease automatically → bump the pin in the app's `Directory.Packages.props`. Latency is one CI run.
- **Same-day iteration (library and app in one sitting):** `dotnet pack src/... -o <local-feed> -p:Version=1.x.y-local.N` and the app's `NuGet.config` lists that folder first. The version must be *higher* than the published one so restore picks it; a `-local.N` suffix on a bumped patch number does that. Never commit a `-local` pin.
- **Branch work on the library** (`dev`, as today): the GitHub Packages workflow publishes on `main` only, so branch builds are consumed through the local feed. If that becomes friction, add `dev` to the workflow's branch list with a `-dev.` suffix; it is a one-line change and DECISIONS.md rules about the public feed are untouched.
- **Releases:** unchanged process, three packages instead of two once `TechieRag.Agents` exists; `publish-github-packages.yml` and `publish-nuget.yml` add the third `dotnet pack` line.

### 10.5 What the split costs

- A library fix is no longer visible in the app until a package exists (minutes with the local feed, one CI run otherwise). This is the honest price of "TechieDesk consumes what customers consume".
- Two `.tfcore/` copies and two ledgers to keep in step; the cross-links in section 8 are the mitigation.
- The library repo loses its only end-to-end UI proof. `TechieRag.Tests` already covers the library hermetically and with gated live tests; the screen-level proof moves to the app repo by design.
- The `AGENT-ONLY AUTHORING NOTES` at the top of the TechieDesk BRD state that GAP-LIB items are tracked in the library BRD; the 2026-07-17 decision overrode that. Section 8 restores it, so the note becomes true again.
