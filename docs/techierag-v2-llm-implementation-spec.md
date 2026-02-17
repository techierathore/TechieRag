# TechieRag v2: Full RAG + LLM Management Platform

## Implementation Specification

**Version:** 2.0
**Date:** 2026-02-17
**Status:** SPECIFICATION - Ready for Implementation
**Prerequisite:** TechieRag v1.1 (100% complete - embedding, vector stores, document processing)

---

## Implementation Status Tracker

**Last Updated:** 2026-02-17

| # | Component | Status | Progress | Phase |
|---|-----------|--------|----------|-------|
| 1 | LLM Core Abstractions (ILlmProvider, IToolHandler) | :x: NOT STARTED | 0% | Phase 1 |
| 2 | LLM Models (ChatMessage, LlmOptions, ToolDefinition, etc.) | :x: NOT STARTED | 0% | Phase 1 |
| 3 | LLM Configuration (LlmConfig, LlmSource enum) | :x: NOT STARTED | 0% | Phase 1 |
| 4 | Token Management (TokenUsageTracker, UsageBudget) | :x: NOT STARTED | 0% | Phase 2 |
| 5 | Resilience (RetryPolicy, CircuitBreaker, FallbackHandler) | :x: NOT STARTED | 0% | Phase 2 |
| 6 | Conversation Memory (IConversationMemory) | :x: NOT STARTED | 0% | Phase 2 |
| 7 | Prompt Template System | :x: NOT STARTED | 0% | Phase 2 |
| 8 | LLM Provider: Ollama | :x: NOT STARTED | 0% | Phase 3 |
| 9 | LLM Provider: LM Studio | :x: NOT STARTED | 0% | Phase 3 |
| 10 | LLM Provider: OpenAI-Compatible HTTP | :x: NOT STARTED | 0% | Phase 3 |
| 11 | LLM Provider: Azure AI Foundry | :x: NOT STARTED | 0% | Phase 3 |
| 12 | LLM Provider: Google Gemini | :x: NOT STARTED | 0% | Phase 3 |
| 13 | LLM Provider: Anthropic (Claude) | :x: NOT STARTED | 0% | Phase 3 |
| 14 | Extended ITechieRag (Auto-RAG methods) | :x: NOT STARTED | 0% | Phase 4 |
| 15 | TechieRagClient v2 (LLM integration) | :x: NOT STARTED | 0% | Phase 4 |
| 16 | TechieRagBuilder v2 (LLM builder methods) | :x: NOT STARTED | 0% | Phase 4 |
| 17 | ServiceCollectionExtensions v2 (DI updates) | :x: NOT STARTED | 0% | Phase 4 |
| 18 | Agent Loop (Tool execution engine) | :x: NOT STARTED | 0% | Phase 5 |
| 19 | TechieRagWeb: Migrate ALL pages to TrBlazeUI | :x: NOT STARTED | 0% | Phase 6 |
| 20 | TechieRagWeb: LLM Settings Page (provider config UI) | :x: NOT STARTED | 0% | Phase 6 |
| 21 | TechieRagWeb: Chat v2 Page (LLM-powered RAG chat) | :x: NOT STARTED | 0% | Phase 6 |
| 22 | TechieRagWeb: Token Usage Dashboard Page | :x: NOT STARTED | 0% | Phase 6 |
| 23 | TechieRagWeb: LLM Playground Page (direct LLM testing) | :x: NOT STARTED | 0% | Phase 6 |
| 24 | TechieRagWeb: Tool Calling Demo Page | :x: NOT STARTED | 0% | Phase 6 |
| 25 | TechieRagWeb: DI/Program.cs updates for LLM services | :x: NOT STARTED | 0% | Phase 6 |
| 26 | Integration Testing | :x: NOT STARTED | 0% | Phase 7 |

### Legend
- :white_check_mark: COMPLETE - Fully implemented and functional
- :arrows_counterclockwise: IN PROGRESS - Currently being worked on
- :x: NOT STARTED - Not yet implemented
- :pause_button: DEFERRED - Optional feature, deferred for later

**Overall Progress: 0% (Specification complete, implementation pending)**

---

## 1. Vision & Scope

### 1.1 Problem Statement

TechieRag v1 handles the **retrieval** half of RAG excellently (embeddings, vector stores, document processing). However, the **generation** half (LLM interaction) is absent. Every application that uses TechieRag must independently implement:

- LLM API communication (different for each provider)
- Token counting and cost tracking
- Prompt construction with RAG context
- Streaming response handling
- Tool/function calling
- Retry logic and error handling
- Conversation history management

This leads to duplicated code across applications (story writing app, astrology app, etc.).

### 1.2 Solution

Expand TechieRag into a **complete RAG + LLM management platform**. Any .NET application needing AI capabilities references TechieRag for ALL interactions:

- **Embeddings** (existing v1 capability)
- **Vector storage** (existing v1 capability)
- **Document processing** (existing v1 capability)
- **LLM completions** (NEW - chat, streaming, structured output)
- **Tool/function calling** (NEW - full agent loop)
- **Token management** (NEW - counting, cost tracking, budgets)
- **Conversation memory** (NEW - optional history management)
- **Resilience** (NEW - retry, fallback, circuit breaker)

### 1.3 Design Principles

1. **Same patterns as v1** - Provider abstraction, fluent builder, DI integration
2. **Composable architecture** - Auto-RAG convenience methods AND individual components
3. **Don't touch TechieRag.Embedded** - It stays as an embedding-only extension
4. **All changes in main TechieRag DLL** - Single package for all capabilities
5. **Follow existing coding standards** - PascalCase, no underscores, XML docs, camelCase private fields

### 1.4 Non-Goals (Out of Scope for v2)

- Fine-tuning or model training
- Image/audio/video generation (text-only)
- Multi-agent orchestration (single agent loop only)
- RAG evaluation/benchmarking framework
- Model hosting or serving

---

## 2. Architecture Overview

### 2.1 Current Architecture (v1)

```
ITechieRag (Public API)
    |
TechieRagClient (Orchestrator)
    +-- IVectorStore (3 implementations)
    +-- IEmbeddingProvider (6 implementations)
    +-- IDocumentProcessor[] (9 processors)
    +-- TechieRagConfig
```

### 2.2 New Architecture (v2)

```
+--------------------------------------------------------------------+
|                        YOUR APPLICATION                             |
+-------------------+-------------------+----------------------------+
                    |                   |
         references |                   | references (optional)
                    v                   v
+-------------------+---+   +----------+------------------+
| ITechieRag             |   | ILlmProvider                  |
| (Auto-RAG Methods)     |   | (Direct LLM Access)           |
+------------------------+   +-------------------------------+
| AskAsync()             |   | CompleteAsync()                |
| AskStreamAsync()       |   | CompleteStreamAsync()          |
| ChatWithRagAsync()     |   | ChatAsync()                   |
| ChatWithRagStreamAsync()|   | ChatStreamAsync()             |
| (+ all v1 methods)     |   | CompleteAsync<T>()            |
+----------+-------------+   | RunAgentLoopAsync()           |
           |                  +------+------------------------+
           |                         |
           +--------+-------+-------+
                    |       |       |
                    v       v       v
+------------------+ +-----+-----+ +------------------+
| IVectorStore     | |IEmbedding | | ILlmProvider     |
| (3 impls)        | |Provider   | | (6 impls)        |
|                  | |(6 impls)  | |                  |
| SqliteVecStore   | |Ollama     | | OllamaLlm       |
| PgVectorStore    | |LmStudio   | | LmStudioLlm     |
| QdrantStore      | |Onnx       | | OpenAICompatLlm  |
+------------------+ |AzureOpenAI| | AzureAIFoundryLlm|
                     |Http       | | GeminiLlm        |
                     |Embedded   | | AnthropicLlm     |
                     +-----------+ +------------------+

Supporting Services:
+------------------+ +------------------+ +------------------+
| ITokenTracker    | |IConversationMemory| | IToolHandler     |
| TokenUsageTracker| | InMemoryMemory   | | AgentLoopRunner  |
| UsageBudget      | | (future: DB)     | | ToolRegistry     |
+------------------+ +------------------+ +------------------+
```

### 2.3 Data Flow: Auto-RAG (AskAsync)

```
User Query
    |
    v
[1] EmbeddingProvider.EmbedAsync(query)
    |
    v
[2] VectorStore.SearchAsync(queryVector, topK)
    |
    v
[3] PromptBuilder.BuildContextPrompt(query, searchResults, systemPrompt)
    |
    v
[4] LlmProvider.CompleteAsync(contextualPrompt) or .CompleteStreamAsync()
    |
    v
[5] TokenTracker.RecordUsage(inputTokens, outputTokens)
    |
    v
RagResponse { Answer, Sources[], TokenUsage }
```

### 2.4 Data Flow: Agent Tool Loop

```
User Query + Tool Definitions
    |
    v
[1] LlmProvider.ChatAsync(messages, tools)
    |
    +---> LLM returns text? --> Done, return response
    |
    +---> LLM returns tool_calls?
              |
              v
         [2] ToolHandler.ExecuteToolAsync(toolCall)
              |
              v
         [3] Add tool result to messages
              |
              v
         [4] Loop back to [1] (max iterations configurable)
```

---

## 3. New File Structure

All new files go into `src/TechieRag/`. No changes to `src/TechieRag.Embedded/`.

```
src/TechieRag/
+-- Abstractions/
|   +-- IVectorStore.cs                    (existing - no changes)
|   +-- IEmbeddingProvider.cs              (existing - no changes)
|   +-- IDocumentProcessor.cs              (existing - no changes)
|   +-- ILlmProvider.cs                    (NEW)
|   +-- IToolHandler.cs                    (NEW)
|   +-- IConversationMemory.cs             (NEW)
|   +-- ITokenTracker.cs                   (NEW)
|   +-- IPromptTemplate.cs                 (NEW)
|
+-- Models/
|   +-- TextChunk.cs                       (existing - no changes)
|   +-- Document.cs                        (existing - no changes)
|   +-- SearchResult.cs                    (existing - no changes)
|   +-- IngestionStats.cs                  (existing - no changes)
|   +-- ChatMessage.cs                     (NEW)
|   +-- LlmCompletionOptions.cs            (NEW)
|   +-- ToolDefinition.cs                  (NEW)
|   +-- ToolCall.cs                        (NEW)
|   +-- ToolResult.cs                      (NEW)
|   +-- TokenUsage.cs                      (NEW)
|   +-- RagResponse.cs                     (NEW)
|   +-- LlmResponse.cs                     (NEW)
|
+-- Llm/                                   (NEW - entire folder)
|   +-- OllamaLlmProvider.cs
|   +-- LmStudioLlmProvider.cs
|   +-- OpenAICompatibleLlmProvider.cs
|   +-- AzureAIFoundryLlmProvider.cs
|   +-- GoogleGeminiLlmProvider.cs
|   +-- AnthropicLlmProvider.cs
|
+-- Services/                              (NEW - entire folder)
|   +-- TokenUsageTracker.cs
|   +-- InMemoryConversationMemory.cs
|   +-- AgentLoopRunner.cs
|   +-- PromptTemplateEngine.cs
|   +-- FallbackLlmHandler.cs
|   +-- RetryHandler.cs
|
+-- VectorStores/                          (existing - no changes)
+-- Embedding/                             (existing - no changes)
+-- Processors/                            (existing - no changes)
+-- DependencyInjection/
|   +-- ServiceCollectionExtensions.cs     (existing - MODIFIED to register LLM services)
|
+-- ITechieRag.cs                          (existing - EXTENDED with new methods)
+-- TechieRagClient.cs                     (existing - EXTENDED with LLM orchestration)
+-- TechieRagConfig.cs                     (existing - EXTENDED with LlmConfig)
+-- TechieRagBuilder.cs                    (existing - EXTENDED with LLM builder methods)
```

---

## 4. Interface Specifications

### 4.1 ILlmProvider (NEW)

**File:** `src/TechieRag/Abstractions/ILlmProvider.cs`

```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for Large Language Model (LLM) interaction services.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for text generation, chat completions,
/// streaming responses, and tool calling across different LLM providers
/// (Ollama, OpenAI, Azure AI Foundry, Gemini, Anthropic).</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder based on LLM configuration.
/// Called by TechieRagClient for auto-RAG methods, or used directly by applications
/// for standalone LLM operations.</para>
/// <para><b>Implementations:</b> OllamaLlmProvider, LmStudioLlmProvider,
/// OpenAICompatibleLlmProvider, AzureAIFoundryLlmProvider, GoogleGeminiLlmProvider,
/// AnthropicLlmProvider</para>
/// </remarks>
public interface ILlmProvider
{
    /// <summary>
    /// Gets the display name of this LLM provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the name of the LLM model being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets whether this provider supports tool/function calling.
    /// </summary>
    bool SupportsToolCalling { get; }

    /// <summary>
    /// Gets whether this provider supports streaming responses.
    /// </summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Generates a text completion for a single prompt.
    /// </summary>
    /// <param name="prompt">The text prompt to complete.</param>
    /// <param name="options">Optional completion parameters (temperature, maxTokens, etc.).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The LLM response including generated text and token usage.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Sends prompt to LLM API, receives full response,
    /// records token usage, and returns structured result.</para>
    /// </remarks>
    Task<LlmResponse> CompleteAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a streaming text completion for a single prompt.
    /// </summary>
    /// <param name="prompt">The text prompt to complete.</param>
    /// <param name="options">Optional completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of response tokens/chunks.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Opens SSE/streaming connection to LLM API,
    /// yields tokens as they arrive for real-time UI updates.</para>
    /// </remarks>
    IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a multi-turn chat conversation and returns the assistant response.
    /// </summary>
    /// <param name="messages">The conversation history (system, user, assistant messages).</param>
    /// <param name="options">Optional completion parameters including tool definitions.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The assistant's response message with token usage.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Sends full message history to LLM chat API.
    /// If tools are defined in options and LLM returns tool calls,
    /// the response will contain ToolCalls instead of or alongside content.</para>
    /// </remarks>
    Task<LlmResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a multi-turn chat conversation and streams the response.
    /// </summary>
    /// <param name="messages">The conversation history.</param>
    /// <param name="options">Optional completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of response tokens/chunks.</returns>
    IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a typed/structured response by requesting JSON output from the LLM
    /// and deserializing it to the specified type.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize the response into.</typeparam>
    /// <param name="prompt">The prompt describing the desired output.</param>
    /// <param name="options">Optional completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized response object.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> 1) Generates JSON schema from T, 2) Adds schema instruction to prompt,
    /// 3) Requests JSON mode from LLM, 4) Deserializes response to T.</para>
    /// </remarks>
    Task<T> CompleteAsync<T>(
        string prompt,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Estimates the token count for a given text using provider-specific tokenization.
    /// </summary>
    /// <param name="text">The text to count tokens for.</param>
    /// <returns>Estimated token count.</returns>
    /// <remarks>
    /// <para><b>Note:</b> Token counting may be approximate for providers that don't
    /// expose their tokenizer. Uses cl100k_base estimation as fallback.</para>
    /// </remarks>
    int EstimateTokenCount(string text);

    /// <summary>
    /// Event raised after each LLM completion, providing token usage metrics.
    /// </summary>
    event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
}

/// <summary>
/// Event arguments for LLM completion telemetry.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides metrics about LLM operations for token tracking,
/// cost calculation, and usage monitoring.</para>
/// </remarks>
public class LlmCompletionEventArgs : EventArgs
{
    /// <summary>Gets the number of input/prompt tokens.</summary>
    public required int InputTokens { get; init; }

    /// <summary>Gets the number of output/completion tokens.</summary>
    public required int OutputTokens { get; init; }

    /// <summary>Gets the total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Gets the duration of the LLM operation.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the model name used.</summary>
    public required string ModelName { get; init; }

    /// <summary>Gets the provider name used.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets whether this was a streaming request.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Gets whether tool calls were involved.</summary>
    public bool InvolvedToolCalls { get; init; }
}
```

### 4.2 IToolHandler (NEW)

**File:** `src/TechieRag/Abstractions/IToolHandler.cs`

```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for handling tool/function calls from LLMs.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines tools that the LLM can call and handles their execution.
/// Used by the agent loop to process tool calls and return results to the LLM.</para>
/// <para><b>Code Flow:</b> Registered with TechieRagBuilder. When the LLM returns tool calls,
/// AgentLoopRunner uses this handler to execute them and feed results back.</para>
/// </remarks>
public interface IToolHandler
{
    /// <summary>
    /// Gets the list of tool definitions available for the LLM to call.
    /// </summary>
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }

    /// <summary>
    /// Executes a tool call and returns the result.
    /// </summary>
    /// <param name="toolCall">The tool call from the LLM containing name and arguments.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the tool execution.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Matches tool call name to registered handler,
    /// deserializes arguments, executes the function, and wraps result.</para>
    /// </remarks>
    Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall,
        CancellationToken cancellationToken = default);
}
```

### 4.3 IConversationMemory (NEW)

**File:** `src/TechieRag/Abstractions/IConversationMemory.cs`

```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Optional abstraction for managing conversation history.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides conversation state management for multi-turn interactions.
/// Applications can opt into this component for automatic history tracking.</para>
/// <para><b>Code Flow:</b> Optionally injected into TechieRagClient. When present,
/// auto-RAG chat methods automatically append messages and manage context window.</para>
/// <para><b>Implementations:</b> InMemoryConversationMemory (default),
/// future: database-backed memory</para>
/// </remarks>
public interface IConversationMemory
{
    /// <summary>
    /// Gets the current conversation ID.
    /// </summary>
    string ConversationId { get; }

    /// <summary>
    /// Adds a message to the conversation history.
    /// </summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all messages in the current conversation.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ordered list of conversation messages.</returns>
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Trims conversation history to fit within a token limit, keeping the most recent messages.
    /// </summary>
    /// <param name="maxTokens">Maximum token budget for conversation history.</param>
    /// <param name="tokenCounter">Function to estimate tokens for a message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Trimmed list of messages within the token budget.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b> Always keeps the system message (if present).
    /// Removes oldest user/assistant pairs until total tokens fit within budget.</para>
    /// </remarks>
    Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(
        int maxTokens,
        Func<string, int> tokenCounter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all messages from the current conversation.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a new conversation, optionally preserving the system message.
    /// </summary>
    /// <param name="conversationId">Optional ID for the new conversation. Auto-generated if null.</param>
    /// <param name="systemMessage">Optional system message to start with.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task StartNewConversationAsync(
        string? conversationId = null,
        string? systemMessage = null,
        CancellationToken cancellationToken = default);
}
```

### 4.4 ITokenTracker (NEW)

**File:** `src/TechieRag/Abstractions/ITokenTracker.cs`

```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for tracking token usage and costs across LLM operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides centralized token counting, cost calculation,
/// and usage budget management. Supports per-request tracking and cumulative totals.</para>
/// <para><b>Code Flow:</b> Automatically wired to ILlmProvider.OnCompletionCompleted events.
/// Applications can query usage stats and configure budget alerts.</para>
/// </remarks>
public interface ITokenTracker
{
    /// <summary>
    /// Records token usage from an LLM operation.
    /// </summary>
    /// <param name="usage">The token usage to record.</param>
    void RecordUsage(TokenUsage usage);

    /// <summary>
    /// Gets the cumulative token usage for the current session.
    /// </summary>
    /// <returns>Aggregated token usage statistics.</returns>
    TokenUsageSummary GetSessionUsage();

    /// <summary>
    /// Gets token usage breakdown by model.
    /// </summary>
    /// <returns>Dictionary of model name to usage summary.</returns>
    IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel();

    /// <summary>
    /// Gets the estimated cost for the current session based on provider pricing.
    /// </summary>
    /// <returns>Estimated cost in USD.</returns>
    decimal GetEstimatedCost();

    /// <summary>
    /// Sets a usage budget with alert threshold.
    /// </summary>
    /// <param name="budget">The budget configuration.</param>
    void SetBudget(UsageBudget budget);

    /// <summary>
    /// Gets the current budget status.
    /// </summary>
    /// <returns>Budget status or null if no budget is set.</returns>
    BudgetStatus? GetBudgetStatus();

    /// <summary>
    /// Resets all tracked usage (e.g., for a new session).
    /// </summary>
    void Reset();

    /// <summary>
    /// Event raised when usage exceeds the configured budget alert threshold.
    /// </summary>
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <summary>
    /// Event raised after each token usage is recorded.
    /// </summary>
    event EventHandler<TokenUsage>? OnUsageRecorded;
}
```

### 4.5 IPromptTemplate (NEW)

**File:** `src/TechieRag/Abstractions/IPromptTemplate.cs`

```csharp
namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for building prompts with RAG context injection.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Separates prompt construction from LLM interaction,
/// allowing applications to customize how RAG context is formatted and injected.</para>
/// <para><b>Implementations:</b> DefaultPromptTemplate (ships with sensible defaults),
/// applications can provide custom implementations.</para>
/// </remarks>
public interface IPromptTemplate
{
    /// <summary>
    /// Builds a system prompt with RAG context from search results.
    /// </summary>
    /// <param name="userQuery">The user's original question.</param>
    /// <param name="searchResults">Search results from the vector store.</param>
    /// <param name="systemPrompt">Optional base system prompt to prepend.</param>
    /// <returns>List of ChatMessages ready to send to the LLM.</returns>
    /// <remarks>
    /// <para><b>Default behavior:</b> Creates a system message with context chunks
    /// and a user message with the query. The system message instructs the LLM
    /// to answer based on provided context.</para>
    /// </remarks>
    IReadOnlyList<ChatMessage> BuildRagPrompt(
        string userQuery,
        IReadOnlyList<SearchResult> searchResults,
        string? systemPrompt = null);

    /// <summary>
    /// Builds a chat prompt with RAG context and conversation history.
    /// </summary>
    /// <param name="userMessage">The latest user message.</param>
    /// <param name="searchResults">Search results from the vector store.</param>
    /// <param name="conversationHistory">Previous messages in the conversation.</param>
    /// <param name="systemPrompt">Optional base system prompt.</param>
    /// <returns>List of ChatMessages ready to send to the LLM.</returns>
    IReadOnlyList<ChatMessage> BuildRagChatPrompt(
        string userMessage,
        IReadOnlyList<SearchResult> searchResults,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        string? systemPrompt = null);
}
```

---

## 5. Model Specifications

### 5.1 ChatMessage (NEW)

**File:** `src/TechieRag/Models/ChatMessage.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents a single message in a chat conversation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Core data structure for multi-turn conversations with LLMs.
/// Supports system, user, assistant, and tool roles.</para>
/// </remarks>
public class ChatMessage
{
    /// <summary>
    /// Gets or sets the role of the message sender.
    /// </summary>
    /// <remarks>Valid values: "system", "user", "assistant", "tool"</remarks>
    public required string Role { get; set; }

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Gets or sets tool calls requested by the assistant (when Role is "assistant").
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Gets or sets the tool call ID this message responds to (when Role is "tool").
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the sender (optional).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets the timestamp when this message was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Creates a system message.</summary>
    public static ChatMessage System(string content) => new() { Role = "system", Content = content };

    /// <summary>Creates a user message.</summary>
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };

    /// <summary>Creates an assistant message.</summary>
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };

    /// <summary>Creates a tool result message.</summary>
    public static ChatMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}
```

### 5.2 LlmCompletionOptions (NEW)

**File:** `src/TechieRag/Models/LlmCompletionOptions.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Configuration options for LLM completion requests.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Allows per-request customization of LLM behavior.
/// Properties left as null will use the provider's defaults.</para>
/// </remarks>
public class LlmCompletionOptions
{
    /// <summary>Gets or sets the sampling temperature (0.0 - 2.0). Lower = more deterministic.</summary>
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the maximum number of output tokens.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Gets or sets the top-p (nucleus) sampling parameter.</summary>
    public float? TopP { get; set; }

    /// <summary>Gets or sets the frequency penalty (-2.0 to 2.0).</summary>
    public float? FrequencyPenalty { get; set; }

    /// <summary>Gets or sets the presence penalty (-2.0 to 2.0).</summary>
    public float? PresencePenalty { get; set; }

    /// <summary>Gets or sets stop sequences that halt generation.</summary>
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>Gets or sets the system prompt (used when calling CompleteAsync, not ChatAsync).</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Gets or sets whether to force JSON output mode.</summary>
    public bool JsonMode { get; set; }

    /// <summary>Gets or sets the JSON schema for structured output (used with CompleteAsync{T}).</summary>
    public string? JsonSchema { get; set; }

    /// <summary>Gets or sets tool definitions for function calling.</summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; set; }

    /// <summary>Gets or sets how the LLM should handle tools ("auto", "none", "required", or specific tool name).</summary>
    public string? ToolChoice { get; set; }

    /// <summary>Gets or sets a seed for reproducible generation (provider support varies).</summary>
    public int? Seed { get; set; }
}
```

### 5.3 ToolDefinition, ToolCall, ToolResult (NEW)

**File:** `src/TechieRag/Models/ToolDefinition.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Defines a tool/function that the LLM can call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Describes a callable function including its name, description,
/// and parameter schema (JSON Schema format) for the LLM to understand when and how to use it.</para>
/// </remarks>
public class ToolDefinition
{
    /// <summary>Gets or sets the tool name (must be unique, lowercase with underscores).</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets a description of what the tool does (helps LLM decide when to use it).</summary>
    public required string Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON Schema describing the tool's parameters.
    /// </summary>
    /// <remarks>
    /// <para><b>Format:</b> Standard JSON Schema object. Example:</para>
    /// <code>
    /// { "type": "object", "properties": { "query": { "type": "string" } }, "required": ["query"] }
    /// </code>
    /// </remarks>
    public required string ParametersSchema { get; set; }

    /// <summary>Gets or sets whether this tool requires user confirmation before execution.</summary>
    public bool RequiresConfirmation { get; set; }
}
```

**File:** `src/TechieRag/Models/ToolCall.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents a tool/function call requested by the LLM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Contains the LLM's request to execute a specific tool with arguments.
/// Returned as part of LlmResponse when the LLM decides to use a tool.</para>
/// </remarks>
public class ToolCall
{
    /// <summary>Gets or sets the unique ID for this tool call (used to match results).</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the name of the tool to call.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the JSON-serialized arguments for the tool.</summary>
    public required string ArgumentsJson { get; set; }

    /// <summary>
    /// Deserializes the arguments to the specified type.
    /// </summary>
    /// <typeparam name="T">The target argument type.</typeparam>
    /// <returns>Deserialized arguments.</returns>
    public T GetArguments<T>() where T : class =>
        System.Text.Json.JsonSerializer.Deserialize<T>(ArgumentsJson)
        ?? throw new InvalidOperationException($"Failed to deserialize tool arguments to {typeof(T).Name}");
}
```

**File:** `src/TechieRag/Models/ToolResult.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Represents the result of executing a tool call.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Contains the output from a tool execution that gets sent back
/// to the LLM as context for generating the next response.</para>
/// </remarks>
public class ToolResult
{
    /// <summary>Gets or sets the ID of the tool call this result responds to.</summary>
    public required string ToolCallId { get; set; }

    /// <summary>Gets or sets the result content (typically text or serialized JSON).</summary>
    public required string Content { get; set; }

    /// <summary>Gets or sets whether the tool execution was successful.</summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>Gets or sets an error message if the tool execution failed.</summary>
    public string? ErrorMessage { get; set; }
}
```

### 5.4 TokenUsage & Budget Models (NEW)

**File:** `src/TechieRag/Models/TokenUsage.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Token usage metrics from a single LLM operation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Tracks input/output tokens and estimated cost for each LLM call.
/// Used by ITokenTracker for cumulative tracking and budget management.</para>
/// </remarks>
public class TokenUsage
{
    /// <summary>Gets or sets the number of input/prompt tokens.</summary>
    public int InputTokens { get; set; }

    /// <summary>Gets or sets the number of output/completion tokens.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Gets the total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Gets or sets the estimated cost in USD for this operation.</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Gets or sets the model name that generated this usage.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider name.</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the timestamp of this usage record.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Aggregated token usage summary across multiple operations.
/// </summary>
public class TokenUsageSummary
{
    /// <summary>Gets or sets the total input tokens across all operations.</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>Gets or sets the total output tokens across all operations.</summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>Gets the total tokens.</summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>Gets or sets the total estimated cost in USD.</summary>
    public decimal TotalEstimatedCostUsd { get; set; }

    /// <summary>Gets or sets the number of operations.</summary>
    public int OperationCount { get; set; }

    /// <summary>Gets or sets the timestamp of the first operation.</summary>
    public DateTime? FirstOperationAt { get; set; }

    /// <summary>Gets or sets the timestamp of the last operation.</summary>
    public DateTime? LastOperationAt { get; set; }
}

/// <summary>
/// Configuration for a usage budget with alert thresholds.
/// </summary>
public class UsageBudget
{
    /// <summary>Gets or sets the maximum total tokens allowed (0 = unlimited).</summary>
    public long MaxTotalTokens { get; set; }

    /// <summary>Gets or sets the maximum cost in USD allowed (0 = unlimited).</summary>
    public decimal MaxCostUsd { get; set; }

    /// <summary>
    /// Gets or sets the alert threshold as a percentage (0.0-1.0).
    /// Alert fires when usage reaches this percentage of the budget.
    /// </summary>
    /// <remarks>Default 0.8 means alert at 80% of budget.</remarks>
    public float AlertThreshold { get; set; } = 0.8f;

    /// <summary>Gets or sets whether to block requests when budget is exceeded.</summary>
    public bool BlockOnExceeded { get; set; }
}

/// <summary>
/// Current status of a usage budget.
/// </summary>
public class BudgetStatus
{
    /// <summary>Gets or sets the configured budget.</summary>
    public required UsageBudget Budget { get; init; }

    /// <summary>Gets or sets the current usage summary.</summary>
    public required TokenUsageSummary CurrentUsage { get; init; }

    /// <summary>Gets the percentage of token budget consumed (0.0-1.0).</summary>
    public float TokenUtilization => Budget.MaxTotalTokens > 0
        ? (float)CurrentUsage.TotalTokens / Budget.MaxTotalTokens
        : 0;

    /// <summary>Gets the percentage of cost budget consumed (0.0-1.0).</summary>
    public float CostUtilization => Budget.MaxCostUsd > 0
        ? (float)(CurrentUsage.TotalEstimatedCostUsd / Budget.MaxCostUsd)
        : 0;

    /// <summary>Gets whether the budget has been exceeded.</summary>
    public bool IsExceeded =>
        (Budget.MaxTotalTokens > 0 && CurrentUsage.TotalTokens >= Budget.MaxTotalTokens) ||
        (Budget.MaxCostUsd > 0 && CurrentUsage.TotalEstimatedCostUsd >= Budget.MaxCostUsd);

    /// <summary>Gets whether the alert threshold has been reached.</summary>
    public bool IsAlertTriggered => TokenUtilization >= Budget.AlertThreshold || CostUtilization >= Budget.AlertThreshold;
}

/// <summary>
/// Event arguments when budget alert is triggered.
/// </summary>
public class BudgetAlertEventArgs : EventArgs
{
    /// <summary>Gets the current budget status.</summary>
    public required BudgetStatus Status { get; init; }

    /// <summary>Gets whether this is an exceeded alert (vs warning threshold).</summary>
    public bool IsExceeded { get; init; }
}
```

### 5.5 LlmResponse & RagResponse (NEW)

**File:** `src/TechieRag/Models/LlmResponse.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Response from an LLM completion or chat operation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Wraps the LLM's response with metadata including token usage,
/// tool calls, and finish reason.</para>
/// </remarks>
public class LlmResponse
{
    /// <summary>Gets or sets the generated text content.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets the tool calls requested by the LLM (if any).</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>Gets whether the response contains tool calls.</summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    /// <summary>Gets or sets the token usage for this operation.</summary>
    public required TokenUsage Usage { get; set; }

    /// <summary>Gets or sets the finish reason ("stop", "tool_calls", "length", "content_filter").</summary>
    public string FinishReason { get; set; } = "stop";

    /// <summary>Gets or sets the model that generated the response.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Converts to a ChatMessage for conversation history.</summary>
    public ChatMessage ToChatMessage() => new()
    {
        Role = "assistant",
        Content = Content,
        ToolCalls = ToolCalls
    };
}
```

**File:** `src/TechieRag/Models/RagResponse.cs`

```csharp
namespace TechieRag.Models;

/// <summary>
/// Response from an auto-RAG operation (search + generate).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Combines the LLM's generated answer with the source documents
/// and search results used to produce it, enabling citation and transparency.</para>
/// </remarks>
public class RagResponse
{
    /// <summary>Gets or sets the generated answer text.</summary>
    public required string Answer { get; set; }

    /// <summary>Gets or sets the search results (sources) used to generate the answer.</summary>
    public required IReadOnlyList<SearchResult> Sources { get; set; }

    /// <summary>Gets or sets the token usage for the LLM operation.</summary>
    public required TokenUsage Usage { get; set; }

    /// <summary>Gets or sets the original query.</summary>
    public required string Query { get; set; }

    /// <summary>Gets or sets the model name that generated the answer.</summary>
    public string ModelName { get; set; } = string.Empty;
}
```

---

## 6. Configuration Extensions

### 6.1 Extended TechieRagConfig

**File:** `src/TechieRag/TechieRagConfig.cs` (MODIFY existing file)

Add the following new configuration classes and enum:

```csharp
// ADD to existing TechieRagConfig class:
/// <summary>
/// Gets or sets the LLM provider configuration.
/// </summary>
public LlmConfig Llm { get; set; } = new();

/// <summary>
/// Gets or sets the LLM fallback provider configuration (optional).
/// </summary>
public LlmConfig? LlmFallback { get; set; }

/// <summary>
/// Gets or sets the usage tracking configuration.
/// </summary>
public UsageTrackingConfig UsageTracking { get; set; } = new();

/// <summary>
/// Gets or sets the prompt template configuration.
/// </summary>
public PromptConfig Prompt { get; set; } = new();

/// <summary>
/// Gets or sets the resilience/retry configuration.
/// </summary>
public ResilienceConfig Resilience { get; set; } = new();

// NEW CLASS:
/// <summary>
/// Configuration for LLM provider selection and settings.
/// </summary>
public class LlmConfig
{
    /// <summary>Gets or sets the LLM source type.</summary>
    public LlmSource Source { get; set; } = LlmSource.None;

    /// <summary>Gets or sets the API endpoint URL.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the API key for authentication.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the model name/deployment.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the default temperature.</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Gets or sets the default max output tokens.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Gets or sets the API version (for Azure AI Foundry).</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Gets or sets the project ID (for Google Gemini).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Gets or sets the maximum context window size in tokens.</summary>
    public int MaxContextTokens { get; set; } = 128000;
}

// NEW ENUM:
/// <summary>
/// Supported LLM provider sources.
/// </summary>
public enum LlmSource
{
    /// <summary>No LLM configured (embedding/retrieval only mode).</summary>
    None,
    /// <summary>Ollama local model server.</summary>
    Ollama,
    /// <summary>LM Studio local model server.</summary>
    LmStudio,
    /// <summary>OpenAI-compatible REST API (works with OpenAI, vLLM, LocalAI, etc.).</summary>
    OpenAICompatible,
    /// <summary>Azure AI Foundry (formerly Azure OpenAI).</summary>
    AzureAIFoundry,
    /// <summary>Google Gemini API.</summary>
    GoogleGemini,
    /// <summary>Anthropic Claude API.</summary>
    Anthropic
}

// NEW CLASS:
/// <summary>
/// Configuration for token usage tracking and budgets.
/// </summary>
public class UsageTrackingConfig
{
    /// <summary>Gets or sets whether token tracking is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum total tokens budget (0 = unlimited).</summary>
    public long MaxTotalTokens { get; set; }

    /// <summary>Gets or sets the maximum cost budget in USD (0 = unlimited).</summary>
    public decimal MaxCostUsd { get; set; }

    /// <summary>Gets or sets the budget alert threshold percentage (0.0-1.0).</summary>
    public float AlertThreshold { get; set; } = 0.8f;

    /// <summary>Gets or sets whether to block requests when budget is exceeded.</summary>
    public bool BlockOnExceeded { get; set; }
}

// NEW CLASS:
/// <summary>
/// Configuration for prompt templates used in RAG operations.
/// </summary>
public class PromptConfig
{
    /// <summary>
    /// Gets or sets the default system prompt for RAG operations.
    /// </summary>
    /// <remarks>Default instructs the LLM to answer based on provided context.</remarks>
    public string SystemPrompt { get; set; } =
        "You are a helpful assistant. Answer the user's question based on the provided context. " +
        "If the context doesn't contain relevant information, say so. " +
        "Cite the source documents when possible.";

    /// <summary>
    /// Gets or sets the template for formatting context chunks.
    /// Placeholders: {index}, {text}, {source}, {score}, {page}
    /// </summary>
    public string ContextChunkTemplate { get; set; } =
        "[Source {index}: {source} (relevance: {score:P0})]\n{text}";

    /// <summary>
    /// Gets or sets the maximum number of context chunks to include in the prompt.
    /// </summary>
    public int MaxContextChunks { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum tokens to allocate for context in the prompt.
    /// </summary>
    public int MaxContextTokens { get; set; } = 4000;
}

// NEW CLASS:
/// <summary>
/// Configuration for retry and resilience behavior.
/// </summary>
public class ResilienceConfig
{
    /// <summary>Gets or sets the maximum number of retry attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Gets or sets the initial delay between retries in milliseconds.</summary>
    public int InitialRetryDelayMs { get; set; } = 1000;

    /// <summary>Gets or sets the maximum delay between retries in milliseconds.</summary>
    public int MaxRetryDelayMs { get; set; } = 30000;

    /// <summary>Gets or sets the backoff multiplier for exponential backoff.</summary>
    public float BackoffMultiplier { get; set; } = 2.0f;

    /// <summary>Gets or sets whether to automatically handle rate limiting (429 status).</summary>
    public bool HandleRateLimiting { get; set; } = true;

    /// <summary>Gets or sets the circuit breaker failure threshold (consecutive failures before opening).</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Gets or sets the circuit breaker recovery time in seconds.</summary>
    public int CircuitBreakerRecoverySeconds { get; set; } = 30;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
```

---

## 7. Extended ITechieRag Interface

**File:** `src/TechieRag/ITechieRag.cs` (MODIFY existing file)

Add the following methods to the existing ITechieRag interface:

```csharp
// === NEW: LLM-Powered RAG Methods ===

/// <summary>
/// Performs a complete RAG operation: searches for relevant context and generates an answer.
/// </summary>
/// <param name="question">The user's question.</param>
/// <param name="topK">Maximum number of context chunks to retrieve.</param>
/// <param name="systemPrompt">Optional system prompt override.</param>
/// <param name="documentFilter">Optional document ID to restrict search scope.</param>
/// <param name="options">Optional LLM completion parameters.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>A RagResponse containing the answer, sources, and token usage.</returns>
/// <remarks>
/// <para><b>Flow:</b> 1) Embeds the question, 2) Searches vector store for relevant chunks,
/// 3) Builds prompt with context, 4) Calls LLM to generate answer, 5) Returns answer with sources.</para>
/// <para><b>Requires:</b> Both IEmbeddingProvider and ILlmProvider must be configured.</para>
/// </remarks>
Task<RagResponse> AskAsync(
    string question,
    int topK = 5,
    string? systemPrompt = null,
    string? documentFilter = null,
    LlmCompletionOptions? options = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Performs a complete RAG operation with streaming response.
/// </summary>
/// <param name="question">The user's question.</param>
/// <param name="topK">Maximum number of context chunks to retrieve.</param>
/// <param name="systemPrompt">Optional system prompt override.</param>
/// <param name="documentFilter">Optional document ID to restrict search scope.</param>
/// <param name="options">Optional LLM completion parameters.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>An async enumerable of response tokens for real-time streaming.</returns>
IAsyncEnumerable<string> AskStreamAsync(
    string question,
    int topK = 5,
    string? systemPrompt = null,
    string? documentFilter = null,
    LlmCompletionOptions? options = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Performs a RAG-powered chat operation with conversation history.
/// </summary>
/// <param name="userMessage">The latest user message.</param>
/// <param name="conversationHistory">Previous messages in the conversation (optional if using ConversationMemory).</param>
/// <param name="topK">Maximum number of context chunks to retrieve.</param>
/// <param name="systemPrompt">Optional system prompt override.</param>
/// <param name="options">Optional LLM completion parameters.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>A RagResponse containing the answer, sources, and token usage.</returns>
/// <remarks>
/// <para><b>Flow:</b> Similar to AskAsync but includes conversation history for multi-turn context.
/// If IConversationMemory is configured, history is automatically managed.</para>
/// </remarks>
Task<RagResponse> ChatWithRagAsync(
    string userMessage,
    IReadOnlyList<ChatMessage>? conversationHistory = null,
    int topK = 5,
    string? systemPrompt = null,
    LlmCompletionOptions? options = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Performs a RAG-powered chat operation with streaming response.
/// </summary>
/// <param name="userMessage">The latest user message.</param>
/// <param name="conversationHistory">Previous messages in the conversation.</param>
/// <param name="topK">Maximum number of context chunks to retrieve.</param>
/// <param name="systemPrompt">Optional system prompt override.</param>
/// <param name="options">Optional LLM completion parameters.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>An async enumerable of response tokens.</returns>
IAsyncEnumerable<string> ChatWithRagStreamAsync(
    string userMessage,
    IReadOnlyList<ChatMessage>? conversationHistory = null,
    int topK = 5,
    string? systemPrompt = null,
    LlmCompletionOptions? options = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Gets the configured LLM provider for direct access.
/// </summary>
/// <returns>The ILlmProvider instance, or null if no LLM is configured.</returns>
/// <remarks>
/// <para><b>Usage:</b> Use this when you need direct LLM access without RAG context,
/// such as for summarization, translation, or standalone generation.</para>
/// </remarks>
ILlmProvider? GetLlmProvider();

/// <summary>
/// Gets the token usage tracker for monitoring consumption.
/// </summary>
/// <returns>The ITokenTracker instance.</returns>
ITokenTracker GetTokenTracker();

/// <summary>
/// Gets the conversation memory component (if configured).
/// </summary>
/// <returns>The IConversationMemory instance, or null if not configured.</returns>
IConversationMemory? GetConversationMemory();
```

---

## 8. Extended TechieRagBuilder

**File:** `src/TechieRag/TechieRagBuilder.cs` (MODIFY existing file)

Add the following builder methods:

```csharp
// === NEW: LLM Provider Configuration ===

/// <summary>
/// Configures the LLM provider with full control over all settings.
/// </summary>
public TechieRagBuilder UseLlm(LlmSource source, string? endpoint = null, string? apiKey = null,
    string? model = null, float temperature = 0.7f, int maxTokens = 2048)
{
    config.Llm = new LlmConfig
    {
        Source = source,
        Endpoint = endpoint,
        ApiKey = apiKey,
        Model = model ?? string.Empty,
        Temperature = temperature,
        MaxTokens = maxTokens
    };
    return this;
}

/// <summary>
/// Configures Ollama as the LLM provider.
/// </summary>
/// <param name="endpoint">Ollama API endpoint (default: http://localhost:11434).</param>
/// <param name="model">Model name (e.g., "llama3.2", "mistral", "gemma2").</param>
public TechieRagBuilder UseOllamaLlm(string endpoint = "http://localhost:11434", string model = "llama3.2")
    => UseLlm(LlmSource.Ollama, endpoint, model: model);

/// <summary>
/// Configures LM Studio as the LLM provider.
/// </summary>
/// <param name="endpoint">LM Studio API endpoint (default: http://localhost:1234).</param>
/// <param name="model">Model identifier (optional, LM Studio auto-selects loaded model).</param>
public TechieRagBuilder UseLmStudioLlm(string endpoint = "http://localhost:1234", string? model = null)
    => UseLlm(LlmSource.LmStudio, endpoint, model: model ?? "default");

/// <summary>
/// Configures an OpenAI-compatible REST API as the LLM provider.
/// Works with OpenAI, vLLM, LocalAI, Together.ai, Groq, and other OpenAI-compatible APIs.
/// </summary>
/// <param name="endpoint">API endpoint (e.g., "https://api.openai.com/v1" for OpenAI).</param>
/// <param name="apiKey">API key for authentication.</param>
/// <param name="model">Model name (e.g., "gpt-4o", "gpt-4o-mini").</param>
public TechieRagBuilder UseOpenAICompatibleLlm(string endpoint, string apiKey, string model = "gpt-4o")
    => UseLlm(LlmSource.OpenAICompatible, endpoint, apiKey, model);

/// <summary>
/// Configures Azure AI Foundry as the LLM provider.
/// </summary>
/// <param name="endpoint">Azure AI Foundry endpoint URL.</param>
/// <param name="apiKey">API key for authentication.</param>
/// <param name="model">Deployment/model name.</param>
/// <param name="apiVersion">API version (default: latest stable).</param>
public TechieRagBuilder UseAzureAIFoundryLlm(string endpoint, string apiKey, string model,
    string apiVersion = "2024-12-01-preview")
{
    config.Llm = new LlmConfig
    {
        Source = LlmSource.AzureAIFoundry,
        Endpoint = endpoint,
        ApiKey = apiKey,
        Model = model,
        ApiVersion = apiVersion
    };
    return this;
}

/// <summary>
/// Configures Google Gemini as the LLM provider.
/// </summary>
/// <param name="apiKey">Google AI API key.</param>
/// <param name="model">Model name (default: "gemini-2.0-flash").</param>
public TechieRagBuilder UseGeminiLlm(string apiKey, string model = "gemini-2.0-flash")
    => UseLlm(LlmSource.GoogleGemini, "https://generativelanguage.googleapis.com", apiKey, model);

/// <summary>
/// Configures Anthropic Claude as the LLM provider.
/// </summary>
/// <param name="apiKey">Anthropic API key.</param>
/// <param name="model">Model name (default: "claude-sonnet-4-5-20250929").</param>
public TechieRagBuilder UseAnthropicLlm(string apiKey, string model = "claude-sonnet-4-5-20250929")
    => UseLlm(LlmSource.Anthropic, "https://api.anthropic.com", apiKey, model);

/// <summary>
/// Configures a custom LLM provider implementation.
/// </summary>
/// <param name="factory">Factory function that creates the ILlmProvider instance.</param>
public TechieRagBuilder UseCustomLlmProvider(Func<ILlmProvider> factory)
{
    customLlmProviderFactory = factory;
    return this;
}

// === NEW: Fallback LLM ===

/// <summary>
/// Configures a fallback LLM provider that activates when the primary provider fails.
/// </summary>
/// <param name="configure">Action to configure the fallback LLM.</param>
public TechieRagBuilder WithFallbackLlm(Action<LlmConfig> configure)
{
    config.LlmFallback = new LlmConfig();
    configure(config.LlmFallback);
    return this;
}

// === NEW: Token Tracking ===

/// <summary>
/// Configures token usage tracking and budgets.
/// </summary>
/// <param name="configure">Action to configure usage tracking.</param>
public TechieRagBuilder WithUsageTracking(Action<UsageTrackingConfig>? configure = null)
{
    config.UsageTracking.Enabled = true;
    configure?.Invoke(config.UsageTracking);
    return this;
}

// === NEW: Conversation Memory ===

/// <summary>
/// Enables optional conversation memory for multi-turn chat.
/// </summary>
public TechieRagBuilder WithConversationMemory()
{
    useConversationMemory = true;
    return this;
}

// === NEW: Prompt Templates ===

/// <summary>
/// Configures the RAG prompt template.
/// </summary>
/// <param name="systemPrompt">Custom system prompt for RAG operations.</param>
/// <param name="contextTemplate">Custom template for formatting context chunks.</param>
public TechieRagBuilder WithPromptTemplate(string? systemPrompt = null, string? contextTemplate = null)
{
    if (systemPrompt != null) config.Prompt.SystemPrompt = systemPrompt;
    if (contextTemplate != null) config.Prompt.ContextChunkTemplate = contextTemplate;
    return this;
}

/// <summary>
/// Provides a custom IPromptTemplate implementation.
/// </summary>
/// <param name="factory">Factory function that creates the IPromptTemplate instance.</param>
public TechieRagBuilder WithCustomPromptTemplate(Func<IPromptTemplate> factory)
{
    customPromptTemplateFactory = factory;
    return this;
}

// === NEW: Resilience ===

/// <summary>
/// Configures retry and resilience behavior for LLM calls.
/// </summary>
/// <param name="configure">Action to configure resilience settings.</param>
public TechieRagBuilder WithResilience(Action<ResilienceConfig>? configure = null)
{
    configure?.Invoke(config.Resilience);
    return this;
}

// === NEW: Tool Handling ===

/// <summary>
/// Registers a tool handler for function calling with the agent loop.
/// </summary>
/// <param name="handler">The tool handler implementation.</param>
public TechieRagBuilder WithToolHandler(IToolHandler handler)
{
    toolHandler = handler;
    return this;
}

/// <summary>
/// Registers individual tool functions for the agent loop.
/// </summary>
/// <param name="configure">Action to register tools with the ToolRegistry.</param>
public TechieRagBuilder WithTools(Action<ToolRegistry> configure)
{
    var registry = new ToolRegistry();
    configure(registry);
    toolHandler = registry;
    return this;
}
```

---

## 9. LLM Provider Implementations

### 9.1 Provider Implementation Pattern

All LLM providers follow the same implementation pattern:

```csharp
// Common structure for all providers:
public class XxxLlmProvider : ILlmProvider
{
    // Fields: httpClient, endpoint, modelName, logger
    // Constructor: validates config, creates HttpClient
    // CompleteAsync: POST to chat/completion endpoint, parse response
    // CompleteStreamAsync: POST with stream=true, parse SSE events
    // ChatAsync: POST with messages array, handle tool calls
    // ChatStreamAsync: POST with messages + stream, parse SSE events
    // CompleteAsync<T>: Add JSON schema instruction, parse JSON response
    // EstimateTokenCount: Use cl100k_base approximation (chars/4)
    // OnCompletionCompleted: Fire event with token usage
}
```

### 9.2 Provider Details

| Provider | Class | Endpoint Format | Auth | Tool Support | Streaming |
|----------|-------|----------------|------|-------------|-----------|
| Ollama | `OllamaLlmProvider` | `{endpoint}/api/chat` | None | Yes (v0.4+) | Yes (SSE) |
| LM Studio | `LmStudioLlmProvider` | `{endpoint}/v1/chat/completions` | None | Limited | Yes (SSE) |
| OpenAI-Compatible | `OpenAICompatibleLlmProvider` | `{endpoint}/chat/completions` | Bearer token | Yes | Yes (SSE) |
| Azure AI Foundry | `AzureAIFoundryLlmProvider` | `{endpoint}/openai/deployments/{model}/chat/completions` | api-key header | Yes | Yes (SSE) |
| Google Gemini | `GoogleGeminiLlmProvider` | `{endpoint}/v1beta/models/{model}:generateContent` | API key in URL | Yes | Yes |
| Anthropic | `AnthropicLlmProvider` | `https://api.anthropic.com/v1/messages` | x-api-key header | Yes | Yes (SSE) |

### 9.3 OllamaLlmProvider (Implementation Reference)

**File:** `src/TechieRag/Llm/OllamaLlmProvider.cs`

```csharp
namespace TechieRag.Llm;

/// <summary>
/// LLM provider implementation for Ollama local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using locally-hosted models via Ollama,
/// supporting chat completions, streaming, and tool calling (Ollama v0.4+).</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.Ollama is configured.
/// Communicates with Ollama's HTTP API at /api/chat endpoint.</para>
/// <para><b>Dependencies:</b> Requires Ollama to be running locally with a model pulled.</para>
/// </remarks>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string endpoint;
    private readonly ILogger<OllamaLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "Ollama";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new Ollama LLM provider instance.
    /// </summary>
    /// <param name="endpoint">Ollama API endpoint (e.g., http://localhost:11434).</param>
    /// <param name="model">Model name to use (e.g., "llama3.2", "mistral").</param>
    /// <param name="logger">Logger instance.</param>
    public OllamaLlmProvider(string endpoint, string model, ILogger<OllamaLlmProvider>? logger = null)
    {
        this.endpoint = endpoint.TrimEnd('/');
        ModelName = model;
        this.logger = logger ?? NullLogger<OllamaLlmProvider>.Instance;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(this.endpoint),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    // Implementation follows Ollama API: POST /api/chat
    // Request: { "model": "...", "messages": [...], "stream": true/false, "tools": [...] }
    // Response: { "message": { "role": "assistant", "content": "..." }, "eval_count": N, "prompt_eval_count": N }
    // Streaming: newline-delimited JSON objects
}
```

### 9.4 AnthropicLlmProvider (Implementation Reference)

**File:** `src/TechieRag/Llm/AnthropicLlmProvider.cs`

```csharp
namespace TechieRag.Llm;

/// <summary>
/// LLM provider implementation for Anthropic Claude API.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using Anthropic's Claude models
/// via the Messages API, supporting chat, streaming, and tool use.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.Anthropic is configured.
/// Uses Anthropic Messages API v1 with x-api-key authentication.</para>
/// <para><b>API Differences:</b> Anthropic uses a different API format than OpenAI:
/// system message is a top-level field, not in the messages array.
/// Tool use returns content blocks with type "tool_use" instead of "tool_calls".</para>
/// </remarks>
public class AnthropicLlmProvider : ILlmProvider
{
    // Anthropic Messages API: POST /v1/messages
    // Headers: x-api-key, anthropic-version: 2023-06-01
    // Request: { "model": "...", "max_tokens": N, "system": "...", "messages": [...], "tools": [...] }
    // Response: { "content": [{ "type": "text", "text": "..." }], "usage": { "input_tokens": N, "output_tokens": N } }
    // Streaming: SSE with event types: message_start, content_block_delta, message_delta, message_stop
}
```

### 9.5 GoogleGeminiLlmProvider (Implementation Reference)

**File:** `src/TechieRag/Llm/GoogleGeminiLlmProvider.cs`

```csharp
namespace TechieRag.Llm;

/// <summary>
/// LLM provider implementation for Google Gemini API.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using Google's Gemini models
/// via the Generative Language API.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.GoogleGemini is configured.
/// Uses the generateContent endpoint with API key authentication.</para>
/// <para><b>API Differences:</b> Gemini uses "parts" instead of "content" in messages,
/// role names differ ("model" instead of "assistant"), and tool definitions use
/// a different schema format ("functionDeclarations").</para>
/// </remarks>
public class GoogleGeminiLlmProvider : ILlmProvider
{
    // Gemini API: POST /v1beta/models/{model}:generateContent?key={apiKey}
    // Streaming: POST /v1beta/models/{model}:streamGenerateContent?key={apiKey}
    // Request: { "contents": [{ "role": "user", "parts": [{ "text": "..." }] }], "tools": [...] }
    // Response: { "candidates": [{ "content": { "parts": [{ "text": "..." }] } }], "usageMetadata": { ... } }
}
```

---

## 10. Service Implementations

### 10.1 AgentLoopRunner (NEW)

**File:** `src/TechieRag/Services/AgentLoopRunner.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// Runs the complete agent tool-calling loop until the LLM produces a final answer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages the multi-turn tool execution cycle where the LLM
/// can call tools, receive results, and continue generating until it produces
/// a text response (not a tool call).</para>
/// <para><b>Code Flow:</b></para>
/// <list type="number">
/// <item>Send messages + tool definitions to LLM</item>
/// <item>If LLM returns tool_calls: execute each tool via IToolHandler</item>
/// <item>Add tool results to messages</item>
/// <item>Send updated messages back to LLM</item>
/// <item>Repeat until LLM returns text (no tool calls) or max iterations reached</item>
/// </list>
/// <para><b>Safety:</b> Configurable max iterations to prevent infinite loops.</para>
/// </remarks>
public class AgentLoopRunner
{
    private readonly ILlmProvider llmProvider;
    private readonly IToolHandler toolHandler;
    private readonly ILogger<AgentLoopRunner> logger;
    private readonly int maxIterations;

    /// <summary>
    /// Creates a new agent loop runner.
    /// </summary>
    /// <param name="llmProvider">The LLM provider to use for generation.</param>
    /// <param name="toolHandler">The tool handler for executing tool calls.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="maxIterations">Maximum tool-call iterations before stopping (default: 10).</param>
    public AgentLoopRunner(
        ILlmProvider llmProvider,
        IToolHandler toolHandler,
        ILogger<AgentLoopRunner>? logger = null,
        int maxIterations = 10)
    {
        this.llmProvider = llmProvider;
        this.toolHandler = toolHandler;
        this.logger = logger ?? NullLogger<AgentLoopRunner>.Instance;
        this.maxIterations = maxIterations;
    }

    /// <summary>
    /// Runs the agent loop with the given messages and returns the final response.
    /// </summary>
    /// <param name="messages">Initial conversation messages.</param>
    /// <param name="options">LLM completion options (tools are added automatically).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The final LLM response after all tool calls are resolved.</returns>
    public async Task<LlmResponse> RunAsync(
        List<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Ensure tool definitions are included
        options ??= new LlmCompletionOptions();
        options = options with { Tools = toolHandler.ToolDefinitions };

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var response = await llmProvider.ChatAsync(messages, options, cancellationToken);

            if (!response.HasToolCalls)
            {
                return response; // Final answer - no more tool calls
            }

            // Add assistant message with tool calls to history
            messages.Add(response.ToChatMessage());

            // Execute each tool call
            foreach (var toolCall in response.ToolCalls!)
            {
                logger.LogInformation("Executing tool: {ToolName} (iteration {Iteration})",
                    toolCall.Name, iteration + 1);

                var result = await toolHandler.ExecuteToolAsync(toolCall, cancellationToken);
                messages.Add(ChatMessage.Tool(result.ToolCallId, result.Content));
            }
        }

        // Max iterations reached - return last response
        logger.LogWarning("Agent loop reached max iterations ({MaxIterations})", maxIterations);
        return await llmProvider.ChatAsync(messages,
            options with { Tools = null, ToolChoice = "none" }, cancellationToken);
    }
}
```

### 10.2 ToolRegistry (NEW)

**File:** `src/TechieRag/Services/ToolRegistry.cs` (part of AgentLoopRunner.cs or separate)

```csharp
namespace TechieRag.Services;

/// <summary>
/// Registry for dynamically registering tools with delegate-based handlers.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a fluent API for registering tool functions without
/// implementing IToolHandler. Each tool is a name + schema + async delegate.</para>
/// <para><b>Usage:</b></para>
/// <code>
/// builder.WithTools(tools =>
/// {
///     tools.Register("get_weather", "Gets current weather for a city",
///         "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}",
///         async (args, ct) => { /* handler code */ return "72F, sunny"; });
/// });
/// </code>
/// </remarks>
public class ToolRegistry : IToolHandler
{
    private readonly List<ToolDefinition> definitions = new();
    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> handlers = new();

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <summary>
    /// Registers a tool with a delegate handler.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description for the LLM.</param>
    /// <param name="parametersSchema">JSON Schema for tool parameters.</param>
    /// <param name="handler">Async function: (argumentsJson, cancellationToken) => resultString.</param>
    public void Register(string name, string description, string parametersSchema,
        Func<string, CancellationToken, Task<string>> handler)
    {
        definitions.Add(new ToolDefinition
        {
            Name = name,
            Description = description,
            ParametersSchema = parametersSchema
        });
        handlers[name] = handler;
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        if (!handlers.TryGetValue(toolCall.Name, out var handler))
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolCall.Name}' is not registered"
            };
        }

        try
        {
            var result = await handler(toolCall.ArgumentsJson, cancellationToken);
            return new ToolResult { ToolCallId = toolCall.Id, Content = result };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error executing tool: {ex.Message}",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
```

### 10.3 TokenUsageTracker (NEW)

**File:** `src/TechieRag/Services/TokenUsageTracker.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// Tracks token usage and manages usage budgets.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Central service for recording, aggregating, and monitoring
/// token consumption across all LLM operations. Supports cost estimation
/// and budget alerting.</para>
/// <para><b>Code Flow:</b> Automatically subscribed to ILlmProvider.OnCompletionCompleted events.
/// Applications can query GetSessionUsage() and GetEstimatedCost() for monitoring.</para>
/// <para><b>Cost Model:</b> Uses configurable per-model pricing. Default pricing
/// covers major providers (OpenAI, Anthropic, Google, Azure).</para>
/// </remarks>
public class TokenUsageTracker : ITokenTracker
{
    // Thread-safe tracking with ConcurrentBag or lock
    // Maintains per-model usage breakdown
    // Fires OnBudgetAlert when threshold reached
    // Fires OnUsageRecorded for each operation
    // GetEstimatedCost() uses pricing table keyed by model name
}
```

### 10.4 Model Pricing Table

The `TokenUsageTracker` includes a default pricing table (USD per 1M tokens):

| Model | Input Price | Output Price |
|-------|------------|-------------|
| gpt-4o | $2.50 | $10.00 |
| gpt-4o-mini | $0.15 | $0.60 |
| gpt-4-turbo | $10.00 | $30.00 |
| claude-opus-4-6 | $15.00 | $75.00 |
| claude-sonnet-4-5 | $3.00 | $15.00 |
| claude-haiku-4-5 | $0.80 | $4.00 |
| gemini-2.0-flash | $0.075 | $0.30 |
| gemini-1.5-pro | $1.25 | $5.00 |
| Local models (Ollama/LM Studio) | $0.00 | $0.00 |

Pricing is configurable via `TokenUsageTracker.SetModelPricing(modelName, inputPrice, outputPrice)`.

### 10.5 InMemoryConversationMemory (NEW)

**File:** `src/TechieRag/Services/InMemoryConversationMemory.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// In-memory implementation of conversation history management.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides simple, thread-safe conversation memory for multi-turn
/// chat. Supports automatic context window management by trimming oldest messages.</para>
/// <para><b>Limitations:</b> History is lost when the application restarts.
/// For persistent memory, implement a database-backed IConversationMemory.</para>
/// </remarks>
public class InMemoryConversationMemory : IConversationMemory
{
    // ConcurrentDictionary<string, List<ChatMessage>> for multiple conversations
    // GetTrimmedHistoryAsync: Always keeps system message, trims oldest pairs
    // StartNewConversationAsync: Creates new conversation ID, optionally with system prompt
}
```

### 10.6 DefaultPromptTemplate (NEW)

**File:** `src/TechieRag/Services/PromptTemplateEngine.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// Default implementation of RAG prompt construction.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Builds well-structured prompts that combine the user's query
/// with relevant context from the vector store, formatted for optimal LLM performance.</para>
/// <para><b>Default Prompt Structure:</b></para>
/// <code>
/// System: "You are a helpful assistant. Answer based on the provided context..."
///
/// Context:
/// [Source 1: document.pdf (relevance: 95%)]
/// This is the relevant text from the document...
///
/// [Source 2: notes.md (relevance: 87%)]
/// Another relevant passage...
///
/// User: "What is the meaning of life?"
/// </code>
/// </remarks>
public class PromptTemplateEngine : IPromptTemplate
{
    private readonly PromptConfig config;

    public PromptTemplateEngine(PromptConfig config)
    {
        this.config = config;
    }

    // BuildRagPrompt: Creates [system + context, user query] message list
    // BuildRagChatPrompt: Creates [system + context, ...history, user query] message list
    // FormatContext: Applies ContextChunkTemplate to each search result
}
```

### 10.7 RetryHandler & FallbackLlmHandler (NEW)

**File:** `src/TechieRag/Services/RetryHandler.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// Wraps an ILlmProvider with retry, rate-limit handling, and circuit breaker logic.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides resilience for LLM API calls without requiring
/// external libraries like Polly. Implements exponential backoff, rate-limit
/// detection (HTTP 429), and circuit breaker pattern.</para>
/// <para><b>Code Flow:</b> Wraps the actual ILlmProvider as a decorator.
/// All calls are delegated to the inner provider with retry logic around them.</para>
/// </remarks>
public class RetryHandler : ILlmProvider
{
    // Decorator pattern: wraps inner ILlmProvider
    // Exponential backoff: delay * backoffMultiplier^attempt
    // Rate limit handling: reads Retry-After header on 429 responses
    // Circuit breaker: opens after N consecutive failures, waits recovery period
    // All ILlmProvider methods delegate to inner provider with retry wrapper
}
```

**File:** `src/TechieRag/Services/FallbackLlmHandler.cs`

```csharp
namespace TechieRag.Services;

/// <summary>
/// Wraps primary and fallback ILlmProviders with automatic failover.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Routes LLM requests to the primary provider. If the primary
/// fails (after retries), automatically switches to the fallback provider.</para>
/// <para><b>Code Flow:</b> Try primary -> if exception -> log warning -> try fallback -> return result.</para>
/// </remarks>
public class FallbackLlmHandler : ILlmProvider
{
    // Decorator pattern: wraps primary + fallback ILlmProviders
    // All ILlmProvider methods: try primary, catch, try fallback
    // Logs which provider is being used
    // Name/ModelName reflect which provider is currently active
}
```

---

## 11. Dependency Injection Updates

**File:** `src/TechieRag/DependencyInjection/ServiceCollectionExtensions.cs` (MODIFY)

Add LLM service registration to existing extension methods:

```csharp
// Inside the AddTechieRag method, after existing registrations:

// Register LLM provider (if configured)
if (config.Llm.Source != LlmSource.None)
{
    services.AddSingleton<ILlmProvider>(sp =>
    {
        var provider = builder.CreateLlmProvider(sp);

        // Wrap with retry handler
        var retryProvider = new RetryHandler(provider, config.Resilience,
            sp.GetService<ILogger<RetryHandler>>());

        // Wrap with fallback (if configured)
        if (config.LlmFallback is not null)
        {
            var fallbackProvider = builder.CreateFallbackLlmProvider(sp);
            return new FallbackLlmHandler(retryProvider, fallbackProvider,
                sp.GetService<ILogger<FallbackLlmHandler>>());
        }

        return retryProvider;
    });
}

// Register token tracker
if (config.UsageTracking.Enabled)
{
    services.AddSingleton<ITokenTracker>(sp =>
    {
        var tracker = new TokenUsageTracker(config.UsageTracking);
        // Auto-subscribe to LLM events
        var llm = sp.GetService<ILlmProvider>();
        if (llm != null)
        {
            llm.OnCompletionCompleted += (_, args) => tracker.RecordUsage(new TokenUsage
            {
                InputTokens = args.InputTokens,
                OutputTokens = args.OutputTokens,
                ModelName = args.ModelName,
                ProviderName = args.ProviderName
            });
        }
        return tracker;
    });
}

// Register conversation memory (if enabled)
if (useConversationMemory)
{
    services.AddSingleton<IConversationMemory, InMemoryConversationMemory>();
}

// Register prompt template
services.AddSingleton<IPromptTemplate>(sp =>
    customPromptTemplateFactory?.Invoke() ?? new PromptTemplateEngine(config.Prompt));

// Register tool handler (if configured)
if (toolHandler != null)
{
    services.AddSingleton<IToolHandler>(toolHandler);
    services.AddSingleton<AgentLoopRunner>();
}
```

---

## 12. Configuration via appsettings.json

### 12.1 Full Configuration Example

```json
{
  "TechieRag": {
    "Embedding": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "bge-m3"
    },
    "VectorStore": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=techierag.db"
    },
    "Processing": {
      "DefaultChunkSize": 500,
      "DefaultChunkOverlap": 50
    },
    "Llm": {
      "Source": "OpenAICompatible",
      "Endpoint": "https://api.openai.com/v1",
      "ApiKey": "sk-...",
      "Model": "gpt-4o",
      "Temperature": 0.7,
      "MaxTokens": 2048,
      "MaxContextTokens": 128000
    },
    "LlmFallback": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.2",
      "Temperature": 0.7,
      "MaxTokens": 2048
    },
    "UsageTracking": {
      "Enabled": true,
      "MaxTotalTokens": 1000000,
      "MaxCostUsd": 50.00,
      "AlertThreshold": 0.8,
      "BlockOnExceeded": false
    },
    "Prompt": {
      "SystemPrompt": "You are a helpful assistant. Answer based on the provided context.",
      "MaxContextChunks": 5,
      "MaxContextTokens": 4000
    },
    "Resilience": {
      "MaxRetries": 3,
      "InitialRetryDelayMs": 1000,
      "MaxRetryDelayMs": 30000,
      "BackoffMultiplier": 2.0,
      "HandleRateLimiting": true,
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerRecoverySeconds": 30,
      "TimeoutSeconds": 120
    },
    "EnableTelemetry": true
  }
}
```

### 12.2 Minimal Configuration (Embedding Only - v1 Compatible)

```json
{
  "TechieRag": {
    "Embedding": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "bge-m3"
    },
    "VectorStore": {
      "Type": "SqliteVec",
      "ConnectionString": "Data Source=techierag.db"
    }
  }
}
```

Note: When `Llm.Source` is `None` (default), all LLM methods throw `InvalidOperationException("No LLM provider configured. Configure an LLM provider using TechieRagBuilder to use this feature.")`. This maintains backward compatibility with v1.

---

## 13. Usage Examples

### 13.1 Simple RAG (Ask a Question)

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()                           // Embedding
    .UseSqliteVec()                        // Vector Store
    .UseOpenAICompatibleLlm(              // LLM
        "https://api.openai.com/v1",
        "sk-...",
        "gpt-4o")
    .Build();

await rag.InitializeAsync();
await rag.IngestDirectoryAsync("./documents");

var response = await rag.AskAsync("What is the TechieRag library?");
Console.WriteLine(response.Answer);
Console.WriteLine($"Sources: {string.Join(", ", response.Sources.Select(s => s.Chunk.Metadata["SourceFile"]))}");
Console.WriteLine($"Tokens: {response.Usage.TotalTokens} (${response.Usage.EstimatedCostUsd:F4})");
```

### 13.2 Streaming RAG Chat

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseAnthropicLlm("sk-ant-...", "claude-sonnet-4-5-20250929")
    .WithConversationMemory()
    .Build();

await rag.InitializeAsync();

// Stream the response token by token
await foreach (var token in rag.AskStreamAsync("Explain vector databases"))
{
    Console.Write(token);
}
```

### 13.3 Direct LLM Access (No RAG)

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseGeminiLlm("AIza...", "gemini-2.0-flash")
    .Build();

// Get the LLM provider directly
var llm = rag.GetLlmProvider()!;

// Simple completion
var response = await llm.CompleteAsync("Write a haiku about coding");
Console.WriteLine(response.Content);

// Typed/structured output
var analysis = await llm.CompleteAsync<SentimentAnalysis>(
    "Analyze the sentiment of: 'I love this library!'");
Console.WriteLine($"Sentiment: {analysis.Sentiment}, Score: {analysis.Score}");

public class SentimentAnalysis
{
    public string Sentiment { get; set; } = "";
    public float Score { get; set; }
    public string Explanation { get; set; } = "";
}
```

### 13.4 Tool Calling with Agent Loop

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseOpenAICompatibleLlm("https://api.openai.com/v1", "sk-...", "gpt-4o")
    .WithTools(tools =>
    {
        tools.Register(
            "calculate_birth_chart",
            "Calculates an astrological birth chart for given date, time, and location",
            """{"type":"object","properties":{"birthDate":{"type":"string"},"birthTime":{"type":"string"},"location":{"type":"string"}},"required":["birthDate","birthTime","location"]}""",
            async (argsJson, ct) =>
            {
                var args = JsonSerializer.Deserialize<BirthChartArgs>(argsJson)!;
                // Your calculation logic here
                return JsonSerializer.Serialize(new { sun = "Aries", moon = "Cancer", ascending = "Leo" });
            });
    })
    .Build();

// The agent loop runs automatically
var response = await rag.AskAsync(
    "Calculate the birth chart for someone born on March 21, 1990 at 14:30 in New Delhi, India");
Console.WriteLine(response.Answer); // LLM interprets the tool result and explains it
```

### 13.5 Token Budget Management

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseOpenAICompatibleLlm("https://api.openai.com/v1", "sk-...", "gpt-4o")
    .WithUsageTracking(tracking =>
    {
        tracking.MaxCostUsd = 10.00m;
        tracking.AlertThreshold = 0.8f;
        tracking.BlockOnExceeded = true;
    })
    .Build();

// Subscribe to budget alerts
var tracker = rag.GetTokenTracker();
tracker.OnBudgetAlert += (_, alert) =>
{
    if (alert.IsExceeded)
        Console.WriteLine("BUDGET EXCEEDED! Requests will be blocked.");
    else
        Console.WriteLine($"Warning: {alert.Status.CostUtilization:P0} of budget used.");
};

// Check usage anytime
var usage = tracker.GetSessionUsage();
Console.WriteLine($"Total tokens: {usage.TotalTokens:N0}, Cost: ${usage.TotalEstimatedCostUsd:F2}");
```

### 13.6 Primary + Fallback LLM

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseOpenAICompatibleLlm("https://api.openai.com/v1", "sk-...", "gpt-4o")
    .WithFallbackLlm(fallback =>
    {
        fallback.Source = LlmSource.Ollama;
        fallback.Endpoint = "http://localhost:11434";
        fallback.Model = "llama3.2";
    })
    .WithResilience(r =>
    {
        r.MaxRetries = 3;
        r.CircuitBreakerThreshold = 5;
    })
    .Build();

// If OpenAI fails (after retries), automatically falls back to local Ollama
var response = await rag.AskAsync("What is quantum computing?");
```

---

## 14. NuGet Package Updates

**File:** `src/TechieRag/TechieRag.csproj` (MODIFY)

Add new dependencies:

```xml
<ItemGroup>
  <!-- Existing dependencies remain unchanged -->

  <!-- NEW: For JSON Schema generation (typed output) -->
  <PackageReference Include="System.Text.Json" Version="10.0.0" />

  <!-- No additional NuGet packages needed - all LLM providers use HttpClient -->
  <!-- Anthropic, OpenAI, Gemini all use REST APIs via System.Net.Http -->
</ItemGroup>
```

Note: All LLM providers are implemented using raw `HttpClient` and `System.Text.Json` to avoid heavy SDK dependencies. This keeps the package lightweight.

---

## 15. TechieRagWeb Sample Application Updates (Phase 6)

### 15.0 CRITICAL: TrBlazeUI Integration

> **ALL UI work in TechieRagWeb MUST use the TrBlazeUI component library.**
>
> The current TechieRagWeb uses raw HTML with custom inline CSS. As part of the v2 update,
> ALL existing pages and ALL new pages must be migrated to use TrBlazeUI components.
>
> **HOW TO USE TrBlazeUI:**
> Use the `/trblazeui` skill/command (available at `.claude/commands/trblazeui.md`).
> This skill activates a specialized Blazor UI developer agent that knows all TrBlazeUI
> components, patterns, and best practices. The orchestrator MUST invoke this skill
> when building or modifying any `.razor` page in TechieRagWeb.
>
> **SETUP STEPS (run `/trblazeui` then `*integrate`):**
> 1. Add NuGet source for TrBlazeUI (GitHub Packages)
> 2. Install packages: `TrBlazeUI.Components`, `TrBlazeUI.Icons.Lucide`
> 3. Add CSS references to `App.razor`: `theme.css` + `trblazeui.css`
> 4. Register services in `Program.cs`: `AddTrBlazeUIPrimitives()`, `AddScoped<ToastService>()`
> 5. Add `@using` statements to `_Imports.razor`
> 6. Add `<PortalHost />` to `MainLayout.razor` for overlay components
>
> **KEY RULES:**
> - NEVER use raw `<input>`, `<button>`, `<select>`, `<label>`, `<textarea>` -- use TrBlazeUI components
> - NEVER use inline styles -- use Tailwind CSS utility classes via the `Class` parameter
> - ALWAYS wrap form inputs with `<Field>` + `<FieldLabel>` + `<FieldContent>`
> - ALWAYS use `@bind-Value` or `@bind-Checked` for two-way binding
> - Use `ToastService` for success/error/warning notifications
> - Use `Dialog`/`Sheet`/`AlertDialog` for modals
> - Use `Typography` components (`H1`-`H4`, `P`, `Lead`, `Muted`) for text hierarchy
> - Use `DataTable` component for tabular data (documents, usage stats, etc.)
> - Use `Card`/`CardHeader`/`CardContent` for content sections
> - Use `Tabs`/`TabsList`/`TabsTrigger`/`TabsContent` for multi-section pages
>
> **REFERENCE FILE:** If `docs/TrBlazeUI-AI-Reference.md` exists, the trblazeui agent
> will load it automatically as its component knowledge base.

### 15.1 Existing Pages to Migrate (Component #19)

All 6 existing pages must be rewritten using TrBlazeUI components. The functionality
stays the same, but the UI layer changes completely:

| Page | Route | Migration Notes |
|------|-------|----------------|
| Home.razor | `/` | Replace raw HTML with Card, Typography, Button components |
| Settings.razor | `/settings` | Replace `<input>`/`<select>` with TrBlazeUI Input, Select, Switch; use Card sections |
| Ingestion.razor | `/ingestion` | Replace file input area with TrBlazeUI Input, Button, Progress; use DataTable for documents list |
| TextIngestion.razor | `/text-ingestion` | Replace `<textarea>` with Textarea component; use Card layout |
| Chat.razor | `/chat` | Major rework - see Section 15.3 below |
| QdrantAdmin.razor | `/qdrant-admin` | Replace custom modals with Dialog/Sheet; use DataTable for collections/vectors |
| NavMenu.razor | Layout | Replace custom nav with TrBlazeUI navigation components |
| MainLayout.razor | Layout | Add `<PortalHost />`, use TrBlazeUI layout patterns |

### 15.2 New Page: LLM Settings (Component #20)

**File:** `samples/TechieRagWeb/Components/Pages/LlmSettings.razor`
**Route:** `/llm-settings`

**Purpose:** Configure the LLM provider, fallback, usage tracking, resilience, and prompt templates.

**UI Mockup / Sections:**

```
+------------------------------------------------------------------+
|  LLM Configuration                                    [Save] [Reset]|
+------------------------------------------------------------------+
|                                                                    |
|  [Tab: Provider] [Tab: Fallback] [Tab: Usage] [Tab: Prompts]     |
|                                                                    |
|  === PROVIDER TAB ===                                             |
|  +--------------------------------------------------------------+ |
|  | Card: Primary LLM Provider                                   | |
|  |                                                               | |
|  | Field: Source     [Select: Ollama | LM Studio | OpenAI-Compat| |
|  |                            | Azure AI Foundry | Gemini        | |
|  |                            | Anthropic | None]                | |
|  |                                                               | |
|  | (Dynamic fields based on source selection:)                   | |
|  |                                                               | |
|  | Field: Endpoint   [Input: http://localhost:11434         ]    | |
|  | Field: API Key    [Input: ********************************]   | |
|  | Field: Model      [Input: llama3.2                       ]    | |
|  | Field: Temperature [Slider: 0.0 ----[0.7]---- 2.0      ]    | |
|  | Field: Max Tokens  [Input: 2048                         ]    | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  === FALLBACK TAB ===                                             |
|  +--------------------------------------------------------------+ |
|  | Card: Fallback LLM Provider                                   | |
|  | Switch: [Enable Fallback Provider]                            | |
|  | (Same fields as Primary when enabled)                         | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  === USAGE TAB ===                                                |
|  +--------------------------------------------------------------+ |
|  | Card: Usage Tracking & Budgets                                | |
|  | Switch: [Enable Token Tracking]                               | |
|  | Field: Max Total Tokens  [Input: 1000000            ]        | |
|  | Field: Max Cost USD      [Input: 50.00              ]        | |
|  | Field: Alert Threshold   [Slider: 0% ---[80%]--- 100%]      | |
|  | Switch: [Block Requests When Exceeded]                        | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  === PROMPTS TAB ===                                              |
|  +--------------------------------------------------------------+ |
|  | Card: RAG Prompt Template                                     | |
|  | Field: System Prompt [Textarea: You are a helpful...]        | |
|  | Field: Context Template [Textarea: [Source {index}...]]      | |
|  | Field: Max Context Chunks [Input: 5]                         | |
|  | Field: Max Context Tokens [Input: 4000]                      | |
|  |                                                               | |
|  | Card: Resilience Settings                                     | |
|  | Field: Max Retries  [Input: 3]                               | |
|  | Field: Timeout (sec) [Input: 120]                            | |
|  | Switch: [Handle Rate Limiting]                                | |
|  | Field: Circuit Breaker Threshold [Input: 5]                  | |
|  +--------------------------------------------------------------+ |
|                                                                    |
|  +--------------------------------------------------------------+ |
|  | Card: Connection Test                                         | |
|  | [Button: Test LLM Connection]                                 | |
|  | Status: ✅ Connected - llama3.2 via Ollama (response: 245ms) | |
|  +--------------------------------------------------------------+ |
+------------------------------------------------------------------+
```

**Key Interactions:**
- Dynamic form fields show/hide based on selected LLM source (same pattern as existing Embedding settings)
- "Test LLM Connection" sends a simple prompt and shows response time
- Save persists to `techierag-config.json` via `TechieRagConfigService`
- Toast notifications for save success/failure
- Tabs component for organizing the 4 config sections

**@code block must include:**
- `LlmConfig` bound to form fields
- `LlmConfig? fallbackConfig` for fallback tab
- `UsageTrackingConfig` bound to usage fields
- `PromptConfig` bound to prompt fields
- `ResilienceConfig` bound to resilience fields
- `TestLlmConnectionAsync()` method
- `SaveConfigAsync()` and `ResetToDefaultsAsync()` methods

### 15.3 Updated Page: Chat v2 (Component #21)

**File:** `samples/TechieRagWeb/Components/Pages/Chat.razor` (REWRITE)
**Route:** `/chat`

**Purpose:** Full LLM-powered RAG chat replacing the current search-only interface.

**UI Mockup:**

```
+------------------------------------------------------------------+
|  RAG Chat                         [Clear Chat] [New Conversation] |
+------------------------------------------------------------------+
|                                                                    |
| +--------------------------------------------------------------+ |
| | Chat Configuration Bar (collapsible)                          | |
| | Mode: [Auto-RAG | Direct LLM | Search Only]                  | |
| | Top K: [5]  Doc Filter: [All Documents v]                     | |
| | Streaming: [Switch: On]                                       | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Chat Messages Area (scrollable)                                | |
| |                                                                | |
| | [System] Using Ollama/llama3.2 with 5 context chunks          | |
| |                                                                | |
| |                          [User bubble]                         | |
| |                 What is the TechieRag library?                 | |
| |                                                                | |
| | [Assistant bubble]                                             | |
| | TechieRag is a configurable RAG library for .NET...            | |
| |                                                                | |
| | [Expandable: Sources Used (3)]                                 | |
| |   📄 readme.md (95% relevance) - "TechieRag is a..."         | |
| |   📄 architecture.md (87%) - "The library provides..."        | |
| |   📄 api-docs.md (72%) - "ITechieRag interface..."            | |
| |                                                                | |
| | [Expandable: Token Usage]                                      | |
| |   Input: 1,234 | Output: 567 | Cost: $0.0023                 | |
| |                                                                | |
| |                          [User bubble]                         | |
| |               Can you explain the vector stores?               | |
| |                                                                | |
| | [Assistant bubble - streaming...]                              | |
| | TechieRag supports three vector store backends█                | |
| |                                                                | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | [Textarea: Ask a question...              ] [Send Button 🔵] | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Footer Stats: Session: 3,456 tokens | $0.0089 | 4 messages   | |
| +--------------------------------------------------------------+ |
+------------------------------------------------------------------+
```

**Key Features vs Current Chat:**
- **Current:** Search-only, shows raw search results, no LLM generation
- **New:** Full LLM-powered responses with streaming
- **Modes:** Auto-RAG (search + generate), Direct LLM (no context), Search Only (v1 behavior)
- **Streaming:** Real-time token-by-token response rendering
- **Sources:** Expandable section showing which documents were used
- **Token tracking:** Per-message and session-level usage display
- **Conversation memory:** Multi-turn conversations with context
- **Configuration bar:** Change mode, Top K, document filter without leaving the page

**@code block must include:**
- Chat message list with `ChatMessage` model
- Streaming support using `IAsyncEnumerable<string>` via `AskStreamAsync` / `ChatWithRagStreamAsync`
- Mode switching (Auto-RAG / Direct LLM / Search Only)
- Token usage display per message and cumulative
- Source/citation expandable sections per assistant message
- Auto-scroll to bottom on new messages
- Connection to `IConversationMemory` when available

### 15.4 New Page: Token Usage Dashboard (Component #22)

**File:** `samples/TechieRagWeb/Components/Pages/TokenUsage.razor`
**Route:** `/token-usage`

**Purpose:** Monitor token consumption, costs, and budget status.

**UI Mockup:**

```
+------------------------------------------------------------------+
|  Token Usage Dashboard                            [Reset Session] |
+------------------------------------------------------------------+
|                                                                    |
| +------------+ +------------+ +------------+ +----------------+  |
| | Card       | | Card       | | Card       | | Card           |  |
| | Total      | | Total      | | Estimated  | | Operations     |  |
| | Tokens     | | Input/Out  | | Cost       | | Count          |  |
| | 45,678     | | 32K / 13K  | | $0.1234    | | 23             |  |
| +------------+ +------------+ +------------+ +----------------+  |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Budget Status                                            | |
| | Progress Bar: [========>........] 67% of token budget          | |
| | Progress Bar: [=====>..........] 45% of cost budget            | |
| | Alert: Warning at 80% | Block at 100%                         | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Usage by Model                                           | |
| | DataTable:                                                     | |
| | | Model          | Requests | Input Tok | Output Tok | Cost  | |
| | | gpt-4o         | 15       | 22,345    | 8,901      | $0.09 | |
| | | llama3.2       | 8        | 10,234    | 4,567      | $0.00 | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Recent Operations (last 20)                              | |
| | DataTable:                                                     | |
| | | Time     | Model    | In Tok | Out Tok | Cost   | Type    | |
| | | 14:32:01 | gpt-4o   | 1,234  | 567     | $0.003 | RAG     | |
| | | 14:31:45 | llama3.2 | 890    | 234     | $0.000 | Direct  | |
| +--------------------------------------------------------------+ |
+------------------------------------------------------------------+
```

**@code block must include:**
- Inject `ITokenTracker` via `ITechieRag.GetTokenTracker()`
- Auto-refresh every 5 seconds (or on `OnUsageRecorded` event)
- Budget progress bars with color coding (green < 60%, yellow < 80%, red >= 80%)
- DataTable for usage by model breakdown
- DataTable for recent operations log
- Reset session button with confirmation dialog

### 15.5 New Page: LLM Playground (Component #23)

**File:** `samples/TechieRagWeb/Components/Pages/LlmPlayground.razor`
**Route:** `/llm-playground`

**Purpose:** Direct LLM testing without RAG -- test completions, structured output, and streaming.

**UI Mockup:**

```
+------------------------------------------------------------------+
|  LLM Playground                                                    |
+------------------------------------------------------------------+
|                                                                    |
| [Tab: Completion] [Tab: Structured Output] [Tab: Chat]            |
|                                                                    |
| === COMPLETION TAB ===                                            |
| +--------------------------------------------------------------+ |
| | Card: Prompt                                                   | |
| | Field: System Prompt [Textarea: You are a helpful assistant]  | |
| | Field: User Prompt   [Textarea: Write a haiku about coding]  | |
| |                                                                | |
| | Row: Temperature [0.7] Max Tokens [2048] Streaming [Switch]   | |
| |                                                                | |
| | [Button: Generate]                                             | |
| +--------------------------------------------------------------+ |
| | Card: Response                                                 | |
| | "Lines of code appear,                                        | |
| |  Logic flows like morning streams,                            | |
| |  Bugs await downstream."                                      | |
| |                                                                | |
| | Muted: 245ms | Input: 34 tokens | Output: 28 tokens | $0.00 | |
| +--------------------------------------------------------------+ |
|                                                                    |
| === STRUCTURED OUTPUT TAB ===                                     |
| +--------------------------------------------------------------+ |
| | Card: Typed Response Test                                      | |
| | Field: Prompt [Textarea: Analyze the sentiment of: ...]       | |
| | Field: Response Type [Select: SentimentAnalysis |              | |
| |                        WeatherForecast | BookSummary | Custom] | |
| | (If Custom: JSON Schema editor textarea)                       | |
| |                                                                | |
| | [Button: Generate Typed Response]                              | |
| +--------------------------------------------------------------+ |
| | Card: Parsed Result                                            | |
| | JSON tree view of deserialized result                          | |
| | Raw JSON toggle                                                | |
| +--------------------------------------------------------------+ |
|                                                                    |
| === CHAT TAB ===                                                  |
| (Simple multi-turn chat without RAG context - direct ILlmProvider)|
| Similar to Chat v2 but without the search/context/sources display |
+------------------------------------------------------------------+
```

**@code block must include:**
- Get `ILlmProvider` via `ITechieRag.GetLlmProvider()`
- `CompleteAsync()` for single completions
- `CompleteStreamAsync()` for streaming completions
- `CompleteAsync<T>()` for structured output
- `ChatAsync()` for multi-turn direct chat
- Pre-defined response types (SentimentAnalysis, WeatherForecast, BookSummary) for demo
- Custom JSON schema input for ad-hoc typed responses
- Response time display, token counts, and cost per request

### 15.6 New Page: Tool Calling Demo (Component #24)

**File:** `samples/TechieRagWeb/Components/Pages/ToolDemo.razor`
**Route:** `/tool-demo`

**Purpose:** Demonstrate and test the agent tool-calling loop with sample tools.

**UI Mockup:**

```
+------------------------------------------------------------------+
|  Tool Calling Demo                                                 |
+------------------------------------------------------------------+
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Available Tools                                          | |
| | DataTable:                                                     | |
| | | Tool Name        | Description                    | Status | |
| | | get_weather      | Gets current weather for city  | Active | |
| | | calculate_math   | Evaluates math expressions     | Active | |
| | | search_documents | Searches ingested documents    | Active | |
| | | get_current_time | Returns current date and time  | Active | |
| |                                                                | |
| | [Button: + Add Custom Tool]                                    | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Agent Interaction                                        | |
| |                                                                | |
| | [Textarea: What's the weather in New Delhi and what is 42*17?]| |
| | [Button: Run Agent Loop]                                       | |
| |                                                                | |
| | --- Execution Trace ---                                        | |
| | Step 1: LLM called get_weather({"city": "New Delhi"})         | |
| |   Result: "32°C, Partly Cloudy, Humidity: 65%"                | |
| | Step 2: LLM called calculate_math({"expression": "42*17"})   | |
| |   Result: "714"                                                | |
| | Step 3: LLM generated final answer                            | |
| |                                                                | |
| | --- Final Answer ---                                           | |
| | "The weather in New Delhi is 32°C and partly cloudy with 65%  | |
| |  humidity. And 42 × 17 = 714."                                | |
| |                                                                | |
| | Muted: 3 tool calls | 2 iterations | 4,567 tokens | $0.012  | |
| +--------------------------------------------------------------+ |
|                                                                    |
| +--------------------------------------------------------------+ |
| | Card: Add Custom Tool (Dialog/Sheet when button clicked)       | |
| | Field: Tool Name    [Input: my_tool               ]           | |
| | Field: Description  [Input: Does something useful ]           | |
| | Field: Parameters   [Textarea: JSON Schema        ]           | |
| | Field: Mock Response[Textarea: Response to return  ]           | |
| | [Button: Add Tool]                                             | |
| +--------------------------------------------------------------+ |
+------------------------------------------------------------------+
```

**Built-in Demo Tools:**
1. **get_weather** - Returns mock weather data for any city
2. **calculate_math** - Evaluates simple math expressions (using `System.Data.DataTable.Compute`)
3. **search_documents** - Wraps `ITechieRag.SearchAsync()` as a tool the LLM can call
4. **get_current_time** - Returns current UTC and local time

**@code block must include:**
- `ToolRegistry` with pre-registered demo tools
- `AgentLoopRunner` integration
- Execution trace log (step-by-step display of tool calls and results)
- Custom tool registration via Sheet/Dialog component
- Token usage and iteration count display

### 15.7 Program.cs Updates (Component #25)

**File:** `samples/TechieRagWeb/Program.cs` (MODIFY)

Add the following service registrations:

```csharp
// === EXISTING (unchanged) ===
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 1024 * 1024);
builder.Services.AddSingleton<TechieRagManager>();
builder.Services.AddSingleton<ITechieRag>(sp => sp.GetRequiredService<TechieRagManager>());
builder.Services.AddScoped<TechieRagConfigService>();
builder.Services.AddSingleton<IDockerContainerService, DockerContainerService>();
builder.Services.AddSingleton<IQdrantAdminService, QdrantAdminService>();

// === NEW: TrBlazeUI Services ===
builder.Services.AddTrBlazeUIPrimitives();
builder.Services.AddScoped<ToastService>();

// === NEW: LLM services are registered automatically via TechieRagManager ===
// TechieRagManager already creates ITechieRag via TechieRagBuilder.
// The builder now also creates ILlmProvider, ITokenTracker, IConversationMemory
// when LLM is configured. These are accessible via:
//   ITechieRag.GetLlmProvider()
//   ITechieRag.GetTokenTracker()
//   ITechieRag.GetConversationMemory()
// No additional DI registrations needed for LLM services.
```

### 15.8 Navigation Updates

**File:** `samples/TechieRagWeb/Components/Layout/NavMenu.razor` (REWRITE with TrBlazeUI)

Add new navigation items for the LLM pages:

| Icon | Label | Route | Section |
|------|-------|-------|---------|
| Home icon | Home | `/` | General |
| Settings icon | Settings | `/settings` | Configuration |
| Cpu icon | LLM Settings | `/llm-settings` | Configuration |
| FileText icon | File Ingestion | `/ingestion` | Data |
| Type icon | Text Ingestion | `/text-ingestion` | Data |
| MessageSquare icon | RAG Chat | `/chat` | AI Features |
| Terminal icon | LLM Playground | `/llm-playground` | AI Features |
| Wrench icon | Tool Demo | `/tool-demo` | AI Features |
| BarChart3 icon | Token Usage | `/token-usage` | Monitoring |
| Database icon | Qdrant Admin | `/qdrant-admin` | Admin |

Use Lucide icons from `TrBlazeUI.Icons.Lucide` package.

### 15.9 App.razor CSS Updates

**File:** `samples/TechieRagWeb/Components/App.razor` (MODIFY)

Add TrBlazeUI CSS references in `<head>`:

```html
<!-- TrBlazeUI: Theme MUST come before component CSS -->
<link rel="stylesheet" href="styles/theme.css" />
<link rel="stylesheet" href="_content/TrBlazeUI.Components/trblazeui.css" />
```

### 15.10 Updated File Structure

```
samples/TechieRagWeb/
+-- Components/
|   +-- App.razor                          (MODIFY - add TrBlazeUI CSS)
|   +-- Routes.razor                       (unchanged)
|   +-- _Imports.razor                     (MODIFY - add TrBlazeUI usings)
|   +-- Layout/
|   |   +-- MainLayout.razor              (REWRITE - TrBlazeUI + PortalHost)
|   |   +-- NavMenu.razor                 (REWRITE - TrBlazeUI nav with Lucide icons)
|   +-- Pages/
|       +-- Home.razor                     (REWRITE - TrBlazeUI)
|       +-- Settings.razor                 (REWRITE - TrBlazeUI)
|       +-- LlmSettings.razor             (NEW)
|       +-- Ingestion.razor                (REWRITE - TrBlazeUI)
|       +-- TextIngestion.razor            (REWRITE - TrBlazeUI)
|       +-- Chat.razor                     (REWRITE - full LLM-powered RAG chat)
|       +-- LlmPlayground.razor            (NEW)
|       +-- ToolDemo.razor                 (NEW)
|       +-- TokenUsage.razor               (NEW)
|       +-- QdrantAdmin.razor              (REWRITE - TrBlazeUI)
+-- Services/
|   +-- TechieRagManager.cs                (MODIFY - support LLM config reload)
|   +-- TechieRagConfigService.cs          (MODIFY - add LLM config persistence)
|   +-- DockerContainerService.cs          (unchanged)
|   +-- QdrantAdminService.cs              (unchanged)
+-- Program.cs                             (MODIFY - add TrBlazeUI services)
+-- appsettings.json                       (MODIFY - add LLM config section)
+-- wwwroot/
|   +-- styles/
|       +-- theme.css                      (NEW - TrBlazeUI theme via *setup-theme)
```

---

## 16. Implementation Phases

### Phase 1: Core Abstractions & Models (Foundation)
**Components:** #1, #2, #3
**Files:** All new interfaces + all new model classes + config extensions
**Estimated effort:** Small - defining contracts only

### Phase 2: Supporting Services (Infrastructure)
**Components:** #4, #5, #6, #7
**Files:** TokenUsageTracker, RetryHandler, FallbackLlmHandler, InMemoryConversationMemory, PromptTemplateEngine
**Estimated effort:** Medium - core logic implementation

### Phase 3: LLM Provider Implementations
**Components:** #8, #9, #10, #11, #12, #13
**Files:** All 6 LLM provider classes
**Estimated effort:** Large - each provider has unique API format
**Parallel opportunity:** Each provider can be implemented independently

### Phase 4: Integration (Wiring Everything Together)
**Components:** #14, #15, #16, #17
**Files:** Modified ITechieRag, TechieRagClient, TechieRagBuilder, ServiceCollectionExtensions
**Estimated effort:** Medium - orchestration logic

### Phase 5: Agent Loop
**Components:** #18
**Files:** AgentLoopRunner, ToolRegistry
**Estimated effort:** Medium - multi-turn loop logic

### Phase 6: Sample App (TechieRagWeb v2 with TrBlazeUI)
**Components:** #19, #20, #21, #22, #23, #24, #25
**Files:** All TechieRagWeb pages (6 rewritten + 4 new + layout + config)
**Estimated effort:** Large - complete UI rewrite with TrBlazeUI
**CRITICAL:** Use `/trblazeui` skill for ALL Blazor UI work. Invoke `*integrate` first
for project setup, then use `*generate-page`, `*generate-form`, `*generate-dashboard`
commands for each page.
**Parallel opportunity:** Each page can be built independently after TrBlazeUI integration

### Phase 7: Testing
**Components:** #26
**Files:** Manual testing across all providers
**Estimated effort:** Medium - requires live provider access

---

## 16. Backward Compatibility

### Guarantees
1. **All existing v1 methods continue to work unchanged** - IngestAsync, SearchAsync, etc.
2. **Existing configuration is valid** - No LLM config = embedding/retrieval only mode
3. **LlmSource.None is the default** - Apps that don't configure an LLM work exactly as before
4. **No breaking changes to existing interfaces** - Only additive changes to ITechieRag
5. **TechieRag.Embedded is untouched** - No changes required

### Migration Path for Existing Apps
```csharp
// v1 code (continues to work as-is):
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .Build();

// v2 upgrade (just add LLM config):
var rag = new TechieRagBuilder()
    .UseOllama()                    // Embedding (unchanged)
    .UseSqliteVec()                // Vector Store (unchanged)
    .UseAnthropicLlm("sk-...")    // NEW: LLM provider
    .WithUsageTracking()           // NEW: Token tracking
    .Build();

// Now you can use both old and new methods:
await rag.IngestAsync("doc.pdf");                    // v1 method
var answer = await rag.AskAsync("What is in doc?"); // v2 method
```

---

## 17. Coding Standards Reference

All implementation MUST follow these standards (from `docs/Coding-Standards.md` and `docs/trrag-refactoring-roadmap.md`):

1. **No underscores** in any names - use PascalCase or camelCase
2. **XML documentation** on every public class, method, and property
3. **camelCase** for private fields: `private readonly HttpClient httpClient;`
4. **PascalCase** for everything else: classes, methods, properties, constants
5. **Async suffix** on all async methods: `CompleteAsync`, `ChatStreamAsync`
6. **One class per file**, file name matches class name
7. **File-scoped namespaces**
8. **Nullable reference types** enabled
9. **ConfigureAwait(false)** in library code
10. **Early returns** for validation, keep methods < 20 lines where possible

---

---

## 18. TrBlazeUI Quick Reference for Orchestrator

This section ensures the orchestrator agent doesn't miss TrBlazeUI integration.

### Orchestrator Checklist for Phase 6

1. **FIRST:** Invoke `/trblazeui` skill, then run `*integrate` to set up TrBlazeUI in TechieRagWeb
2. Run `*setup-theme` to generate `wwwroot/styles/theme.css`
3. For EACH page (new or rewritten):
   - Invoke `/trblazeui` skill
   - Use `*generate-page {description}` or `*generate-form {description}` as appropriate
   - Ensure ALL form elements use TrBlazeUI components (never raw HTML)
   - Ensure ALL styling uses Tailwind CSS classes via `Class` parameter (never inline styles)
4. Verify `<PortalHost />` is in MainLayout.razor
5. Verify `AddTrBlazeUIPrimitives()` and `AddScoped<ToastService>()` are in Program.cs
6. Verify TrBlazeUI `@using` statements are in `_Imports.razor`

### Key TrBlazeUI Component Mappings

| Raw HTML (NEVER USE) | TrBlazeUI Replacement |
|-----------------------|----------------------|
| `<input type="text">` | `<Input @bind-Value="val" />` |
| `<input type="password">` | `<Input Type="InputType.Password" @bind-Value="val" />` |
| `<input type="number">` | `<Input Type="InputType.Number" @bind-Value="val" />` |
| `<textarea>` | `<Textarea @bind-Value="val" />` |
| `<select>/<option>` | `<Select TValue="string"><SelectTrigger>...<SelectContent><SelectItem>...` |
| `<button>` | `<Button OnClick="Handler">Text</Button>` |
| `<input type="checkbox">` | `<Checkbox @bind-Checked="val" />` |
| `<label>` | `<Label>` or `<FieldLabel>` |
| `<table>` | `<DataTable TData="MyType">` with `<DataTableColumn>` |
| Custom modal div | `<Dialog>` or `<Sheet>` or `<AlertDialog>` |
| Custom toast/alert | `ToastService.Success()` / `.Error()` / `.Warning()` |
| `<h1>`-`<h4>` | `<H1>`-`<H4>` (Typography components) |
| `<p>` | `<P>` (Typography) |
| `style="..."` | `Class="tailwind-classes"` |

---

*This specification was produced via BMAD-METHOD brainstorming session with Business Analyst Mary on 2026-02-17.*
