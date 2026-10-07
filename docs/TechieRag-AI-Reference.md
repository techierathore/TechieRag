# TechieRag v3 - AI Agent Reference Guide

Updated 2026-09-25 for the phase-2 packages (`TechieRag.Telemetry`, `TechieRag.Local`, `TechieRag.Agents`) and the phase-2 features of the core; 2026-10-01 for OpenAI-compatible request headers and the OpenCode Go connector, the `CodeSignInRequired` sign-in code, and keys only through code (an appsettings section holding a key is refused). Every signature below is copied from the source; the section "Phase-2 Features (v3)" gives one call example per feature.

## Overview

TechieRag is a complete RAG (Retrieval-Augmented Generation) + LLM management platform for .NET. It provides:
- **Embeddings** - Generate vector embeddings from text (8 API providers, plus the embedded ONNX models)
- **Vector Storage** - Store and search vectors (3 backends)
- **Document Processing** - Ingest PDFs, Office files, Markdown, text, HTML, code, etc. (13 processors)
- **LLM Completions** - Chat, streaming, structured output (6 API providers, a local in-process model, a ChatGPT subscription)
- **Typed Streaming** - `LlmStreamEvent` (text delta, tool call, completed) from every provider, and the streaming agent loop
- **Tool/Function Calling** - Full agent loop with tool execution, MCP tool servers, flows
- **Agents** - Microsoft Agent Framework over TechieRag (`TechieRag.Agents`), and agentic retrieval on the classic loop
- **Local Model** - Qwen2.5 0.5B on phones, Phi-3 mini on desktops, any ONNX Runtime GenAI model by name (`TechieRag.Local`)
- **Provider Routing** - `UseLlmForModel("claude-sonnet-4-5")` picks the service from the model name
- **Connectors and Web** - GitHub/GitLab repositories, IMAP/mbox mail (plus mail actions: move, Gmail label, Trash, dry run), Confluence; URL and site ingestion behind an SSRF guard
- **Reranking** - Cohere, Jina, or the embedded ONNX cross-encoder
- **Workspaces and Persistence** - SQLite/PostgreSQL conversation and workspace stores
- **Token Management** - Usage tracking, cost estimation, budgets
- **Conversation Memory** - Multi-turn conversation history management
- **Resilience** - Retry, fallback, circuit breaker
- **Telemetry** - Opt-in OpenTelemetry exporters (`TechieRag.Telemetry`)

## Package Information

### NuGet Source

Packages are published on **nuget.org** — the default feed every .NET SDK already has. No account, no token, no `nuget.config` edit is needed to install them.

### Available Packages

| Package | Purpose |
|---------|---------|
| `TechieRag` | Core library - embeddings, vector stores, document processing, LLM providers, tools and agent loop, MCP, flows, connectors, web ingestion, persistence, all services (`net10.0`, `net8.0`) |
| `TechieRag.Embedded` | ONNX-based embedded embedding provider and reranker (no external API; the models download once, then work offline). Carries the native ONNX Runtime wiring for MAUI heads (`net10.0`) |
| `TechieRag.Telemetry` | Opt-in OpenTelemetry exporters (OTLP, console) for the core's traces and metrics; the core links no exporter (`net10.0`, `net8.0`) |
| `TechieRag.Local` | In-process local language model behind `UseLocalLlm()` on ONNX Runtime GenAI: Windows, Apple-silicon macOS, Linux, Android, iOS, Mac Catalyst. Depends on `TechieRag.Embedded` for downloads and native wiring (`net10.0`) |
| `TechieRag.Agents` | Agents on Microsoft Agent Framework over TechieRag: `TechieRagAgentBuilder`, `ITechieRagAgent`, the seam adapters, `AddTechieRagAgent` (`net10.0`, `net8.0`) |

### Install Commands

```bash
dotnet add package TechieRag
# Optional: embedded embeddings and reranker (no Ollama/API needed; the model downloads once, then works offline)
dotnet add package TechieRag.Embedded
# Optional: OpenTelemetry exporters (opt-in; nothing is exported until tracing or metrics is enabled)
dotnet add package TechieRag.Telemetry
# Optional: in-process local language model (the model downloads once after the user accepts its terms)
dotnet add package TechieRag.Local
# Optional: agents on Microsoft Agent Framework over TechieRag
dotnet add package TechieRag.Agents
```

### NuGet Configuration

No `nuget.config` change is required. If the project already has a `nuget.config` with `<clear />`, make sure nuget.org is listed — that is the only source TechieRag needs:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <!-- Optional, maintainers only: internal pre-release feed (see below). Public consumers do not add this. -->
    <!-- <add key="github-techierathore" value="https://nuget.pkg.github.com/techierathore/index.json" /> -->
  </packageSources>
</configuration>
```

### GitHub Packages (optional, internal pre-release feed)

Public consumers never need this. It is an internal development feed for maintainers working on TechieRag itself who want pre-release builds. Only configure it when the human explicitly asks for internal pre-release builds:
- Source URL: `https://nuget.pkg.github.com/techierathore/index.json`
- Authentication: GitHub PAT with `read:packages` scope (username = GitHub username)
- Install: `dotnet add package TechieRag --source github-techierathore --prerelease`

---

## Architecture

```
YOUR APPLICATION
    |
    v
ITechieRag (Public API - all RAG + LLM methods)        TechieRag.Agents: ITechieRagAgent (Microsoft Agent Framework over ITechieRag)
    |
    +-- IEmbeddingProvider (embeddings)               <-- TechieRag.Embedded: UseEmbedded() (bge-m3 desktops, all-MiniLM-L6-v2 phones)
    +-- IVectorStore (vector storage)
    +-- IDocumentProcessor[] (document ingestion)     <-- Connectors (repository, email, Confluence), Web (URL, site crawl, SSRF guard)
    +-- IReranker (optional rerank stage)             <-- Cohere, Jina, or TechieRag.Embedded's ONNX cross-encoder
    +-- ILlmProvider (completions, chat, typed streaming, tools)
    |       6 API providers | TechieRag.Local: LocalLlmProvider | ChatGptSubscriptionLlmProvider | ModelRouter (UseLlmForModel)
    +-- IToolHandler (ToolRegistry, McpToolHandler, RegisterKnowledgeBase) --> AgentLoopRunner (RunAsync / RunStreamAsync), FlowRunner
    +-- ITokenTracker (usage tracking)
    +-- IConversationMemory / IConversationStore / IWorkspaceStore (memory, persistence, workspaces)
    +-- IPromptTemplate (RAG prompt construction)
    +-- TechieRagTelemetry (ActivitySource + Meter)   <-- TechieRag.Telemetry: AddTechieRagTelemetry() exports them
```

---

## Quick Start Examples

### 1. Minimal Setup (Embedding + Vector Store Only)

```csharp
using TechieRag;

var rag = new TechieRagBuilder()
    .UseOllama()                // Embedding via Ollama
    .UseSqliteVec()             // SQLite vector store
    .Build();

await rag.InitializeAsync();
await rag.IngestAsync("./documents/myfile.pdf");
var results = await rag.SearchAsync("search query", topK: 5);
```

### 2. Full RAG + LLM (Ask Questions About Documents)

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()                                    // Embedding
    .UseSqliteVec()                                 // Vector Store
    .UseOpenAICompatibleLlm(                        // LLM
        "https://api.openai.com/v1", "sk-...", "gpt-4o")
    .WithUsageTracking()                            // Token tracking
    .Build();

await rag.InitializeAsync();
await rag.IngestDirectoryAsync("./documents");

// Auto-RAG: search + generate
var response = await rag.AskAsync("What is this project about?");
Console.WriteLine(response.Answer);
Console.WriteLine($"Sources: {string.Join(", ", response.Sources.Select(s => s.Chunk.Metadata["SourceFile"]))}");
Console.WriteLine($"Tokens: {response.Usage.TotalTokens}");
```

### 3. Streaming RAG Chat

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseAnthropicLlm("sk-ant-...", "claude-sonnet-4-5-20250929")
    .WithConversationMemory()
    .Build();

await rag.InitializeAsync();

await foreach (var token in rag.AskStreamAsync("Explain vector databases"))
{
    Console.Write(token);
}
```

### 4. Direct LLM Access (No RAG)

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseGeminiLlm("AIza...", "gemini-2.0-flash")
    .Build();

var llm = rag.GetLlmProvider()!;

// Simple completion
var response = await llm.CompleteAsync("Write a haiku about coding");
Console.WriteLine(response.Content);

// Streaming completion
await foreach (var token in llm.CompleteStreamAsync("Tell me a story"))
{
    Console.Write(token);
}

// Typed/structured output
var analysis = await llm.CompleteAsync<SentimentAnalysis>(
    "Analyze: 'I love this library!'");

public class SentimentAnalysis
{
    public string Sentiment { get; set; } = "";
    public float Score { get; set; }
    public string Explanation { get; set; } = "";
}
```

### 5. Tool Calling with Agent Loop

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseOpenAICompatibleLlm("https://api.openai.com/v1", "sk-...", "gpt-4o")
    .WithTools(tools =>
    {
        tools.Register(
            "get_weather",
            "Gets current weather for a city",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""",
            async (argsJson, ct) =>
            {
                var args = JsonSerializer.Deserialize<WeatherArgs>(argsJson)!;
                return $"Weather in {args.City}: 25C, Sunny";
            });

        tools.Register(
            "calculate",
            "Evaluates a math expression",
            """{"type":"object","properties":{"expression":{"type":"string"}},"required":["expression"]}""",
            async (argsJson, ct) =>
            {
                var args = JsonSerializer.Deserialize<CalcArgs>(argsJson)!;
                var result = new DataTable().Compute(args.Expression, null);
                return result?.ToString() ?? "Error";
            });
    })
    .Build();

// Agent loop runs automatically - LLM decides which tools to call
var response = await rag.AskAsync(
    "What's the weather in Delhi and what is 42 * 17?");
Console.WriteLine(response.Answer);
```

### 6. Token Budget Management

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

var tracker = rag.GetTokenTracker();
tracker.OnBudgetAlert += (_, alert) =>
{
    Console.WriteLine(alert.IsExceeded
        ? "BUDGET EXCEEDED!"
        : $"Warning: {alert.Status.CostUtilization:P0} of budget used.");
};

var usage = tracker.GetSessionUsage();
Console.WriteLine($"Tokens: {usage.TotalTokens:N0}, Cost: ${usage.TotalEstimatedCostUsd:F2}");
```

### 7. Primary + Fallback LLM

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

// If OpenAI fails, automatically falls back to local Ollama
var response = await rag.AskAsync("What is quantum computing?");
```

---

## TechieRagBuilder Methods (Fluent API)

### Embedding Providers

| Method | Description |
|--------|-------------|
| `UseOllama(endpoint, model)` | Ollama embedding (default: localhost:11434, bge-m3) |
| `UseLmStudio(endpoint, model)` | LM Studio embedding |
| `UseOnnx(modelPath)` | ONNX Runtime local embedding |
| `UseAzureOpenAI(endpoint, apiKey, model)` | Azure OpenAI embedding |
| `UseHttpEmbedding(endpoint, apiKey, model)` | Generic HTTP embedding API |
| `UseEmbedded()` | ONNX embedded (requires TechieRag.Embedded package) |

### Vector Stores

| Method | Description |
|--------|-------------|
| `UseSqliteVec(connectionString)` | SQLite with vec extension; `UseSqliteVec()` with no path uses the per-app default database (`DataRoot.DefaultDatabasePath`) |
| `UsePgVector(connectionString)` | PostgreSQL with pgvector extension |
| `UseQdrant(endpoint, apiKey)` | Qdrant vector database |

### LLM Providers

| Method | Description |
|--------|-------------|
| `UseOllamaLlm(endpoint, model)` | Ollama (default: localhost:11434, llama3.2) |
| `UseLmStudioLlm(endpoint, model)` | LM Studio (default: localhost:1234) |
| `UseOpenAICompatibleLlm(endpoint, apiKey, model)` | OpenAI-compatible REST API |
| `UseOpenAICompatibleLlm(endpoint, apiKey, model, headers, sessionHeader?)` | v3: same, with extra request headers and a per-conversation session header (Phase-2 Features, 6) |
| `UseAzureAIFoundryLlm(endpoint, apiKey, model, apiVersion)` | Azure AI Foundry |
| `UseGeminiLlm(apiKey, model)` | Google Gemini (default: gemini-2.0-flash) |
| `UseAnthropicLlm(apiKey, model)` | Anthropic Claude (default: claude-sonnet-4-5-20250929) |
| `UseCustomLlmProvider(factory)` | Custom ILlmProvider implementation |
| `UseLlm(source, endpoint, apiKey, model, temperature, maxTokens)` | Generic LLM configuration |
| `UseLlmForModel(modelName, apiKey?)` | v3: route by model name through `ModelRouter` (see Phase-2 Features, 7) |
| `UseChatGptSubscriptionLlm(signInCallback, options?)` | v3: the user's ChatGPT subscription, device-code sign-in (Phase-2 Features, 6) |
| `UseLocalLlm(...)` | v3: in-process local model, `TechieRag.Local` package (Phase-2 Features, 2) |

### Supporting Features

| Method | Description |
|--------|-------------|
| `WithFallbackLlm(configure)` | Configure fallback LLM provider |
| `WithApiKeys(llm?, embedding?, vectorStore?, rerank?, llmFallback?)` | v3: set the keys of configured sections in code; the way keys reach an `AddTechieRag(section, rag => ...)` setup, since a section may hold none |
| `WithLlmHeaders(headers)` | v3: extra request headers for the OpenAI-compatible LLM, in code |
| `WithUsageTracking(configure?)` | Enable token usage tracking and budgets |
| `WithConversationMemory()` | Enable conversation history management |
| `WithPromptTemplate(systemPrompt?, contextTemplate?)` | Customize RAG prompt templates |
| `WithCustomPromptTemplate(factory)` | Custom IPromptTemplate implementation |
| `WithResilience(configure?)` | Configure retry, timeout, circuit breaker |
| `WithToolHandler(handler)` | Register IToolHandler for tool calling (a `ToolRegistry`, an `McpToolHandler`, a composite) |
| `WithTools(configure)` | Register tools with delegate-based handlers |
| `WithLogging(loggerFactory)` | `ILoggerFactory` for every component |
| `WithReranker(source, apiKey, model?, endpoint?, topN, candidateCount)` | v3: Cohere or Jina rerank stage (Phase-2 Features, 9) |
| `WithReranker(factory, topN, candidateCount)` | v3: custom `IReranker` |
| `UseEmbeddedReranker(modelDirectory?, topN, candidateCount)` | v3: ONNX cross-encoder rerank, `RerankSource.LocalOnnx` (`TechieRag.Embedded`) |
| `WithPersistence(provider, connectionString, defaultUserId)` | v3: SQLite/PostgreSQL conversation and workspace stores (Phase-2 Features, 10) |
| `UseModelRoot(path)` | v3: the folder every local model downloads to (`TechieRag.Embedded`; Phase-2 Features, 3) |

---

## Phase-2 Features (v3)

One subsection per feature: the package and namespaces it needs, the public signatures (copied from the source), and one call example. `rag` is an `ITechieRag` built as in the Quick Start.

### 1. Typed streaming: `ChatStreamEventsAsync` and `RunStreamAsync`

Package `TechieRag`; namespaces `TechieRag.Abstractions`, `TechieRag.Models`, `TechieRag.Services`, `TechieRag.Llm`.

```csharp
// ILlmProvider - additive with a default implementation; the six built-in providers, the local model
// and the subscription provider override it. Order: TextDelta*, ToolCall*, then exactly one Completed.
IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages,
    LlmCompletionOptions? options = null, CancellationToken cancellationToken = default);

public enum LlmStreamEventKind { TextDelta, ToolCall, Completed }
public sealed class LlmStreamEvent
{
    public required LlmStreamEventKind Kind { get; init; }
    public string? Text { get; init; }          // TextDelta
    public ToolCall? ToolCall { get; init; }    // ToolCall: id, name, complete arguments JSON
    public TokenUsage? Usage { get; init; }     // Completed
    public string? FinishReason { get; init; }  // Completed
    public string? ModelName { get; init; }     // Completed
    public static LlmStreamEvent FromText(string text);
    public static LlmStreamEvent FromToolCall(ToolCall toolCall);
    public static LlmStreamEvent FromCompleted(TokenUsage usage, string finishReason, string modelName);
}

// TechieRag.Llm.LlmStreamEventExtensions - the text-only projection (what ChatStreamAsync returns)
public static IAsyncEnumerable<string> ToTextStreamAsync(this IAsyncEnumerable<LlmStreamEvent> events,
    CancellationToken cancellationToken = default);

// TechieRag.Services.AgentLoopRunner - the classic and the streaming agent loop
public AgentLoopRunner(ILlmProvider llmProvider, IToolHandler toolHandler,
    ILogger<AgentLoopRunner>? logger = null, int maxIterations = 10);
public Task<LlmResponse> RunAsync(List<ChatMessage> messages, LlmCompletionOptions? options = null,
    IProgress<AgentStep>? progress = null, CancellationToken cancellationToken = default);
public IAsyncEnumerable<AgentStreamEvent> RunStreamAsync(List<ChatMessage> messages, LlmCompletionOptions? options = null,
    IProgress<AgentStep>? progress = null, CancellationToken cancellationToken = default);

public enum AgentStreamEventKind { TextDelta, ToolCallRequested, ToolExecuted, Completed }
public sealed class AgentStreamEvent
{
    public required AgentStreamEventKind Kind { get; init; }
    public required int Iteration { get; init; }
    public string? Text { get; init; }             // TextDelta
    public ToolCall? ToolCall { get; init; }       // ToolCallRequested, ToolExecuted
    public ToolResult? ToolResult { get; init; }   // ToolExecuted
    public LlmResponse? Response { get; init; }    // Completed: the final answer; Usage is summed over the run
    public bool MaxIterationsReached { get; init; }
}
```

```csharp
var llm = rag.GetLlmProvider()!;
var messages = new List<ChatMessage> { ChatMessage.User("Summarise the release notes") };

await foreach (var e in llm.ChatStreamEventsAsync(messages))
{
    switch (e.Kind)
    {
        case LlmStreamEventKind.TextDelta: Console.Write(e.Text); break;
        case LlmStreamEventKind.ToolCall: Console.WriteLine($"tool requested: {e.ToolCall!.Name}"); break;
        case LlmStreamEventKind.Completed: Console.WriteLine($"{e.Usage!.TotalTokens} tokens, {e.FinishReason}"); break;
    }
}

// The streaming agent loop: text arrives as it is written, tools run as the model asks for them
var runner = new AgentLoopRunner(llm, toolRegistry);
await foreach (var step in runner.RunStreamAsync(messages))
{
    if (step.Kind == AgentStreamEventKind.TextDelta) Console.Write(step.Text);
    else if (step.Kind == AgentStreamEventKind.ToolExecuted) Console.WriteLine($"[{step.ToolCall!.Name}: {step.ToolResult!.Content}]");
    else if (step.Kind == AgentStreamEventKind.Completed) Console.WriteLine($"done: {step.Response!.Usage.TotalTokens} tokens");
}
```

A custom `ILlmProvider` that implements `ChatStreamAsync` on top of the typed method must override `ChatStreamEventsAsync` too, or the two defaults call each other.

### 2. Local model: `TechieRag.Local`

Package `TechieRag.Local` (depends on `TechieRag.Embedded`); namespace `TechieRag.Local`. One provider, `LocalLlmProvider : ILlmProvider`, over ONNX Runtime GenAI on Windows, Linux (x64, Arm64), Apple-silicon macOS, Android, iOS and Mac Catalyst (an Intel Mac throws `PlatformNotSupportedException` on load). The weights are downloaded once into the model root after the user accepts the licence terms, never packed. `SupportsToolCalling` is false; a request with tools is refused. A MAUI app writes no native wiring: the package's `buildTransitive` targets add the ONNX Runtime GenAI native library on Android, iOS and Mac Catalyst.

| Model | Id | Where it is the default | Download | Licence |
|-------|----|-------------------------|----------|---------|
| Qwen2.5 0.5B Instruct | `qwen2.5-0.5b-instruct` (`LocalModel.Qwen25Instruct05B`) | Android and iOS (phones) | 333 MB | Apache-2.0 |
| Phi-3 mini 4k Instruct | `phi-3-mini-4k-instruct` (`LocalModel.Phi3Mini4kInstruct`) | Windows, macOS, Linux, Mac Catalyst | 2.7 GB | MIT |

```csharp
// TechieRag.Local.LocalLlmBuilderExtensions - four overloads
public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, Action<LocalLlmOptions>? configure = null);   // LocalModel.PlatformDefault
public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, string modelId, Action<LocalLlmOptions>? configure = null);
public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, DirectoryInfo modelFolder, LocalChatTemplate chatTemplate,
    int contextLength = 4_096, Action<LocalLlmOptions>? configure = null);   // a folder the host placed: nothing downloaded, no terms asked
public static TechieRagBuilder UseLocalLlm(this TechieRagBuilder builder, LocalModel model, Action<LocalLlmOptions>? configure = null);

// TechieRag.Local.LocalLlm
public static void Register(Action<LocalLlmOptions>? configure = null);   // makes LlmSource.Local and the "local/<model>" route work from configuration
public static LocalLlmProvider Create(string? modelId = null, ILoggerFactory? loggerFactory = null, Action<LocalLlmOptions>? configure = null);

// TechieRag.Local.LocalModel
public static LocalModel Qwen25Instruct05B { get; }
public static LocalModel Phi3Mini4kInstruct { get; }
public static LocalModel PlatformDefault { get; }
public static IReadOnlyList<LocalModel> All { get; }
public static bool IsPhonePlatform { get; }
public static LocalModel? FromId(string? id);
public static LocalModel FromHuggingFace(string repository, string? folder = null, string? version = null);   // any ONNX Runtime GenAI model by name
public string Id { get; }  public string DisplayName { get; }  public string LicenceName { get; }  public Uri? TermsUrl { get; }
public LocalChatTemplate ChatTemplate { get; }  public int ContextLength { get; }  public bool RunsOnPhones { get; }
public LocalModelTerms GetTerms();
public Task<LocalModelTerms> GetTermsAsync(CancellationToken cancellationToken = default);

public enum LocalChatTemplate { ChatMl, Phi3, Llama3, Gemma, ModelDefined }

// TechieRag.Local.LocalLlmOptions
public sealed class LocalLlmOptions
{
    public ILoggerFactory? LoggerFactory { get; set; }
    public LocalModel? Model { get; set; }
    public int? ContextSize { get; set; }
    public int? Threads { get; set; }
    public int MaxTokens { get; set; } = 512;
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.95f;
    public bool TermsAccepted { get; set; }                                                        // the host already showed the terms
    public Func<LocalModelTerms, CancellationToken, Task<bool>>? ConfirmTermsAsync { get; set; }   // ask the user before the first download
}
public sealed record LocalModelTerms(string ModelId, string DisplayName, string LicenceName, Uri? TermsUrl) { public long DownloadBytes { get; init; } }

// Exceptions (namespace TechieRag.Local)
public sealed class LocalModelTermsNotAcceptedException : InvalidOperationException { public LocalModelTerms Terms { get; } }   // no request was sent
public sealed class LocalModelMemoryException : InvalidOperationException { public string ModelId { get; } public long RequiredBytes { get; } public long AvailableBytes { get; } }
public sealed class LocalPromptTooLongException : ArgumentException { public string ModelId { get; } public int PromptTokens { get; } public int ContextTokens { get; } }   // thrown before inference
public sealed class LocalModelIntegrityException : IOException   // a downloaded file's hash did not match; the file was deleted
```

```csharp
using TechieRag.Local;

var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .UseLocalLlm(o => o.ConfirmTermsAsync = (terms, ct) =>
        ShowTermsDialogAsync(terms.DisplayName, terms.LicenceName, terms.TermsUrl, terms.DownloadBytes, ct))
    .Build();

try
{
    var answer = await rag.AskAsync("What does the contract say about notice periods?");
}
catch (LocalModelTermsNotAcceptedException ex) { /* the user declined ex.Terms; nothing was downloaded */ }
catch (LocalModelMemoryException ex) { /* ex.RequiredBytes > ex.AvailableBytes: pick a smaller model */ }
catch (LocalPromptTooLongException ex) { /* ex.PromptTokens > ex.ContextTokens: trim the prompt or topK */ }

// A named model instead of the default: by id, or any ONNX Runtime GenAI model on Hugging Face
// (licence shown through ConfirmTermsAsync first, every file checked against Hugging Face's fingerprint)
builder.UseLocalLlm("qwen2.5-0.5b-instruct");
builder.UseLocalLlm(LocalModel.FromHuggingFace("microsoft/Phi-3-mini-4k-instruct-onnx",
    folder: "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4", version: null), o => o.TermsAccepted = true);

// From configuration: call once at startup, then "Llm": { "Source": "Local", "Model": "qwen2.5-0.5b-instruct" }
LocalLlm.Register();
```

### 3. Embedded embeddings on phones and desktops: `TechieRag.Embedded`

Package `TechieRag.Embedded`; namespace `TechieRag.Embedded` (`ModelRoot` is in the core's `TechieRag.Models`). `UseEmbedded()` picks the platform default: **bge-m3** (1024 dimensions, multilingual, a 2.3 GB first download) on Windows, macOS, Mac Catalyst and Linux; **all-MiniLM-L6-v2** (384 dimensions, English, 91 MB) on Android and iOS, where bge-m3 is refused with `NotSupportedException`. The files go to `<ModelRoot>/<model>`; the size is raised before the first byte; a cancelled download resumes from its `.part` file. A MAUI app writes **no native wiring**: the package's `buildTransitive` targets add the ONNX Runtime native library on Android, iOS and Mac Catalyst (knobs `TechieRagDisableOnnxNativeWiring`, `TechieRagDisableOnnxCustomOpsStub`, `TechieRagOnnxRuntimeVersion`). `TECHIERAG_MODEL_BASE_URL` redirects every download to a mirror.

```csharp
// TechieRag.Embedded.TechieRagBuilderExtensions
public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder);                        // EmbeddedModel.PlatformDefault
public static TechieRagBuilder UseEmbedded(this TechieRagBuilder builder, EmbeddedModel model);   // EmbeddedModel.BgeM3 or EmbeddedModel.MiniLM
public static TechieRagBuilder UseModelRoot(this TechieRagBuilder builder, string modelRoot);     // same as ModelRoot.Set(path); call before the first model loads
public static TechieRagBuilder UseEmbeddedModel(this TechieRagBuilder builder, string modelDirectory, int dimensions = 384, int maxSequenceLength = 512);
public static TechieRagBuilder UseEmbeddedReranker(this TechieRagBuilder builder, string? modelDirectory = null, int topN = 0, int candidateCount = 20);

// TechieRag.Embedded.EmbeddedModel
public static EmbeddedModel BgeM3 { get; }       // "bge-m3", 1024 dimensions, desktops only
public static EmbeddedModel MiniLM { get; }      // "all-minilm-l6-v2", 384 dimensions, allowed on phones
public static EmbeddedModel PlatformDefault { get; }
public static IReadOnlyList<EmbeddedModel> All { get; }
public static bool IsPhonePlatform { get; }
public static EmbeddedModel? FromName(string? name);
public string Name { get; }  public int Dimensions { get; }  public int MaxSequenceLength { get; }  public bool RunsOnPhones { get; }
public long DownloadBytes { get; }  public string DownloadSize { get; }
public string GetModelDirectory();  public bool IsDownloaded();  public IReadOnlyList<ModelDownloadFile> GetDownloadFiles();

// TechieRag.Models.ModelRoot (core) - one folder for every local model: embedder, reranker, local LLM
public const string EnvironmentVariable = "TECHIERAG_MODEL_ROOT";   // host override without code
public static string DefaultPath { get; }     // <LocalApplicationData>/TechieRag/models
public static string Current { get; }
public static void Set(string? path);
public static string GetModelDirectory(string modelName);

// TechieRag.Embedded.ModelDownloadService - the one downloader (embedder, reranker, local model)
public static ModelDownloadService Instance { get; }
public event EventHandler<ModelDownloadSizeEventArgs>? DownloadSizeKnown;   // before the first byte; set e.Decline = true to refuse
public event EventHandler<ModelDownloadProgress>? ProgressChanged;
public ModelDownloadProgress Progress { get; }
public bool IsDownloading { get; }  public bool IsModelReady { get; }
public Task DownloadAsync(string modelName, string destinationDirectory, IReadOnlyList<ModelDownloadFile> files, CancellationToken cancellationToken = default);

public sealed class ModelDownloadSizeEventArgs : EventArgs
{ public string ModelName { get; } public long TotalBytes { get; } public string DisplaySize { get; } public int FileCount { get; } public string DestinationDirectory { get; } public bool Decline { get; set; } }
public class ModelDownloadProgress
{ public ModelDownloadStatus Status; public string? CurrentFile; public long BytesDownloaded; public long TotalBytes; public int OverallBytesProgressPercent; public string StatusMessage; public string? ErrorMessage; }
public enum ModelDownloadStatus { NotStarted, Checking, Downloading, Completed, Failed }
public sealed class ModelDownloadDeclinedException : OperationCanceledException { public string ModelName { get; } public long TotalBytes { get; } }
```

```csharp
using TechieRag.Embedded;

ModelDownloadService.Instance.DownloadSizeKnown += (_, e) =>
    e.Decline = !UserAgreesToDownload(e.ModelName, e.DisplaySize);      // "91 MB" on a phone, "2.3 GB" on a desktop, before any byte
ModelDownloadService.Instance.ProgressChanged += (_, p) =>
    progressBar.Value = p.OverallBytesProgressPercent;                  // a cancelled download resumes from its .part file

var rag = new TechieRagBuilder()
    .UseModelRoot(Path.Combine(FileSystem.AppDataDirectory, "models"))   // optional; the default is the per-user app-data folder
    .UseEmbedded()                                                       // bge-m3 on desktops, all-MiniLM-L6-v2 on Android and iOS
    .UseSqliteVec()
    .Build();
await rag.InitializeAsync();
```

### 4. Agents package: `TechieRag.Agents`

Package `TechieRag.Agents` (Microsoft Agent Framework); namespaces `TechieRag.Agents`, `TechieRag.Agents.Interop`, `TechieRag.Agents.DependencyInjection`. The agent retrieves from the same `ITechieRag` through the core's agentic contract (`TechieRag.Agentic`, below), so the classic loop and the MAF agent share one tested retrieval behaviour.

```csharp
// TechieRag.Agents.TechieRagAgentBuilder
public TechieRagAgentBuilder(ITechieRag rag);
public TechieRagAgentBuilder UseLmStudio(string endpoint, string model);
public TechieRagAgentBuilder UseOllama(string endpoint = "http://localhost:11434", string model = "llama3.2");
public TechieRagAgentBuilder UseOpenAI(string apiKey, string model = "gpt-4o-mini", string endpoint = "https://api.openai.com");
public TechieRagAgentBuilder UseOpenAICompatible(string endpoint, string model, string? apiKey = null);
public TechieRagAgentBuilder UseConfiguredLlm();                          // rag's own ILlmProvider through LlmProviderChatClient
public TechieRagAgentBuilder UseCustomChatClient(Func<IChatClient> factory);
public TechieRagAgentBuilder UseRetrievalSource(IRetrievalSource source);
public TechieRagAgentBuilder WithRetrieval(Action<RetrievalToolOptions> configure);
public TechieRagAgentBuilder WithToolHandler(IToolHandler handler);       // any TechieRag tool handler, adapted
public TechieRagAgentBuilder WithTools(params AITool[] tools);
public TechieRagAgentBuilder WithInstructions(string value);
public TechieRagAgentBuilder WithAdditionalInstructions(string? value);
public TechieRagAgentBuilder WithMaxToolIterations(int max = 8);
public TechieRagAgentBuilder WithTrace(IProgress<AgentStep> progress);
public TechieRagAgentBuilder WithChatOptions(Action<ChatOptions> configure);
public TechieRagAgentBuilder WithChatHistoryProvider(ChatHistoryProvider provider);
public TechieRagAgentBuilder WithPrefetch(bool enabled = true);
public TechieRagAgentBuilder WithLogging(ILoggerFactory factory);
public TechieRagAgentBuilder WithName(string value);
public ITechieRagAgent Build();

// TechieRag.Agents.ITechieRagAgent
AIAgent Agent { get; }
ITechieRag Rag { get; }
Task<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);
Task<AgentRagResponse> AskAsync(string question, AgentSession? session = null, CancellationToken cancellationToken = default);
Task<AgentRagResponse> AskAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, AgentSession? session = null, CancellationToken cancellationToken = default);
IAsyncEnumerable<RagStreamEvent> AskStreamAsync(string question, AgentSession? session = null, CancellationToken cancellationToken = default);

public sealed class AgentRagResponse
{
    public required string Answer { get; init; }
    public required IReadOnlyList<SearchResult> Sources { get; init; }
    public required IReadOnlyList<RetrievalTrace> Searches { get; init; }
    public required IReadOnlyList<ToolApprovalRequestContent> PendingApprovals { get; init; }
    public required AgentResponse Raw { get; init; }
}

// TechieRag.Agents.DependencyInjection - registers ITechieRagAgent and a keyed AIAgent ("techierag"); needs AddTechieRag first
public static IServiceCollection AddTechieRagAgent(this IServiceCollection services, Action<TechieRagAgentBuilder> configure);

// TechieRag.Agents.Interop - the seam adapters, public API
public LlmProviderChatClient(ILlmProvider provider);                                   // ILlmProvider -> IChatClient (streams over ChatStreamEventsAsync)
public static IList<AITool> ToolHandlerFunctions.From(IToolHandler handler);           // IToolHandler -> AITool[] (RequiresConfirmation -> approval)
public AIToolHandler(IEnumerable<AITool> tools);                                       // AITool[] -> IToolHandler
public static AIToolHandler AIToolHandler.FromAgent(AIAgent agent, AgentSession? session = null);   // an agent as one tool
public static AIAgent AgentStepReporter.WithAgentSteps(this AIAgent agent, IProgress<AgentStep> progress, int? maxIterations = null);
public ConversationMemoryChatHistoryProvider(IConversationMemory memory);              // IConversationMemory -> ChatHistoryProvider
```

```csharp
using TechieRag.Agents;
using TechieRag.Agents.Interop;

var agent = new TechieRagAgentBuilder(rag)
    .UseLmStudio("http://localhost:1234", "qwen3-8b")   // or .UseConfiguredLlm() to reuse rag's ILlmProvider
    .WithToolHandler(toolRegistry)
    .WithTrace(new Progress<AgentStep>(step => Console.WriteLine(step)))
    .Build();

var session = await agent.CreateSessionAsync();
var reply = await agent.AskAsync("Which invoices are overdue?", session);
Console.WriteLine(reply.Answer);
Console.WriteLine($"{reply.Searches.Count} searches, {reply.Sources.Count} sources");

await foreach (var ev in agent.AskStreamAsync("And last month?", session))
{
    if (ev.Type == RagStreamEventType.Token) Console.Write(ev.Token);
}

// ASP.NET Core
builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));
builder.Services.AddTechieRagAgent(a => a.UseConfiguredLlm().WithMaxToolIterations(6));

// Seam adapters: one provider configuration and one tool catalogue for both worlds
IChatClient client = new LlmProviderChatClient(rag.GetLlmProvider()!);
IList<AITool> tools = ToolHandlerFunctions.From(toolRegistry);
IToolHandler agentAsTool = AIToolHandler.FromAgent(agent.Agent);
```

**Agentic retrieval on the classic loop** (core, namespace `TechieRag.Agentic`, no extra package). The model decides when and how often to search; every search is traced and every chunk gets a stable reference (`S1`, `S2`, ...).

```csharp
public static ToolRegistry RegisterKnowledgeBase(this ToolRegistry registry, IRetrievalSource source, RetrievalToolOptions? options, RetrievalTurnState state);
public TechieRagRetrievalSource(ITechieRag rag, bool rerank = false);          // IRetrievalSource over ITechieRag
public sealed class RetrievalToolOptions { TopK = 5; MaxTopK = 20; MaxSearchesPerTurn = 4; WeakScoreThreshold = 0.55f; NoneScoreThreshold = 0.35f; Rerank; MaxChunkChars = 1500; DocumentFilter; IncludeScores = true; }
public sealed class RetrievalTurnState { void BeginTurn(); int SearchesUsed; IReadOnlyList<SearchResult> Collected; IReadOnlyList<RetrievalTrace> Searches; string? GetRef(string chunkId); }
public static class AgenticInstructions { public const string Default; public static string WithDomainGuidance(string? extra); }
```

```csharp
using TechieRag.Agentic;

var state = new RetrievalTurnState();
var registry = new ToolRegistry()
    .RegisterKnowledgeBase(new TechieRagRetrievalSource(rag), new RetrievalToolOptions { TopK = 5 }, state);
var runner = new AgentLoopRunner(rag.GetLlmProvider()!, registry);

state.BeginTurn();                                               // once per user turn
var messages = new List<ChatMessage>
{
    ChatMessage.System(AgenticInstructions.Default),
    ChatMessage.User("Who signed the 2025 lease?")
};
var response = await runner.RunAsync(messages);
// state.Collected: the chunks the model retrieved (cite by state.GetRef(chunk.Id)); state.Searches: the trace
```

### 5. Subscription sign-in: `UseChatGptSubscriptionLlm`

Package `TechieRag`; namespaces `TechieRag`, `TechieRag.Llm`, `TechieRag.Models`, `TechieRag.Abstractions`. The user's own ChatGPT subscription is the LLM, reached through OpenAI's device-code sign-in instead of an API key. The host drives the browser; the library waits for the authorisation and keeps the session in an `ISubscriptionSessionStore` the host supplies (in-memory by default). **Only ChatGPT permits this today** (checked 2026-09-24): the other vendors in the catalog are rows with `Permitted = false` and have no builder method. Vendor terms are dated facts in `LlmConnectorCatalog`, never rules in code; show them before sign-in.

```csharp
// TechieRag.TechieRagBuilder
public TechieRagBuilder UseChatGptSubscriptionLlm(Func<SubscriptionSignInPrompt, CancellationToken, Task> signInCallback,
    ChatGptSubscriptionOptions? options = null);

public sealed record SubscriptionSignInPrompt { string ConnectorName; Uri VerificationUri; string UserCode; DateTimeOffset ExpiresAt; }   // TechieRag.Models

// TechieRag.Llm.ChatGptSubscriptionOptions
public sealed class ChatGptSubscriptionOptions
{
    public string Model { get; set; } = "gpt-6-luna";
    public ISubscriptionSessionStore? SessionStore { get; set; }          // null: InMemorySubscriptionSessionStore (lost on restart)
    public string ClientId { get; set; } = CodexPublicClientId;
    public Uri Issuer { get; set; } = new("https://auth.openai.com");
    public Uri BackendEndpoint { get; set; } = new("https://chatgpt.com/backend-api/codex");
    public TimeSpan SignInTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public string DefaultInstructions { get; set; } = "You are a helpful assistant.";
}

// TechieRag.Abstractions.ISubscriptionSessionStore - implement over the platform's secure store
Task<SubscriptionSession?> LoadAsync(string connectorName, CancellationToken cancellationToken = default);
Task SaveAsync(SubscriptionSession session, CancellationToken cancellationToken = default);
Task ClearAsync(string connectorName, CancellationToken cancellationToken = default);
public sealed record SubscriptionSession { string ConnectorName; string AccessToken; string? RefreshToken; string? IdToken; ... }   // TechieRag.Models

// TechieRag.Llm.ChatGptSubscriptionLlmProvider : ILlmProvider
public Task SignInAsync(CancellationToken cancellationToken = default);    // sign in ahead of the first model call
public Task SignOutAsync(CancellationToken cancellationToken = default);

// TechieRag.Llm.SubscriptionSignInException : InvalidOperationException - switch on Code, never on the message
public string Code { get; }
public const string CodeExpired = "SubscriptionSignInExpired";
public const string CodeRejected = "SubscriptionSignInRejected";
public const string CodeSessionRejected = "SubscriptionSessionRejected";
public const string CodeSignInRequired = "SubscriptionSignInRequired";   // the sign-in callback threw (e.g. no window during a model turn); InnerException is the callback's
public const string CodeNotPermitted = "SubscriptionNotPermitted";

// The sign-in callback MAY throw when it cannot show the prompt now; the model call then fails with
// CodeSignInRequired (a SubscriptionSignInException the callback throws keeps its own code).
public const string LlmConnectorCatalog.ChatGptSubscriptionName = "chatgpt-subscription";   // the connector key; never hardcode the string

// TechieRag.Llm.LlmConnectorCatalog rows with Source == LlmSource.Subscription carry the vendor's terms
public sealed record SubscriptionTerms { bool Permitted; string Terms; string AppliesTo; DateOnly CheckedOn; IReadOnlyList<string> Sources; string? BuilderMethod; }
public static ILlmProvider LlmProviderFactory.CreateSubscription(ModelRoute route, Func<SubscriptionSignInPrompt, CancellationToken, Task> signInCallback,
    ISubscriptionSessionStore? sessionStore = null, ILoggerFactory? loggerFactory = null);
```

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .UseChatGptSubscriptionLlm(
        async (prompt, ct) =>
        {
            // Open prompt.VerificationUri in the system browser, show prompt.UserCode, and return; the library waits.
            await Browser.OpenAsync(prompt.VerificationUri);
            ShowSignInCode(prompt.UserCode, prompt.ExpiresAt);
        },
        new ChatGptSubscriptionOptions { Model = "gpt-6-luna", SessionStore = new SecureStorageSessionStore() })
    .Build();

try
{
    var reply = await rag.AskAsync("Summarise my notes from this week");
}
catch (SubscriptionSignInException ex) when (ex.Code == SubscriptionSignInException.CodeExpired)
{
    // the code expired before the user finished; ask again
}
catch (SubscriptionSignInException ex) when (ex.Code == SubscriptionSignInException.CodeSignInRequired)
{
    // the session is gone and the callback could not show a code now: tell the user to sign in again
}

// Which vendors permit it: dated facts from the catalog, shown before sign-in
foreach (var connector in LlmConnectorCatalog.All.Where(c => c.Source == LlmSource.Subscription))
{
    var terms = connector.Subscription!;
    Console.WriteLine($"{connector.DisplayName}: {(terms.Permitted ? "permitted" : "not permitted")} (checked {terms.CheckedOn})");
}
```

### 6. Provider routing: `UseLlmForModel`, `ModelRouter`, `LlmProviderFactory.CreateForModel`

Package `TechieRag`; namespace `TechieRag.Llm`. An application persists one string, the model name, and the catalog picks the service: `claude-sonnet-4-5` routes to Anthropic, `gemini-2.0-flash` to Google, `gpt-4o` to OpenAI. Open-weight names are served by several services, so they must be qualified with the connector (`groq/llama-3.3-70b-versatile`, `local/qwen2.5-0.5b-instruct`); an unqualified one throws `InvalidOperationException`.

```csharp
// TechieRag.TechieRagBuilder
public TechieRagBuilder UseLlmForModel(string modelName, string? apiKey = null);

// TechieRag.Llm.ModelRouter
public static ModelRoute? Resolve(string? modelName);
public static ModelRoute Require(string modelName);
public sealed record ModelRoute(LlmConnectorDescriptor Connector, string ModelId) { string? Endpoint; LlmSource Source; }

// TechieRag.Llm.LlmProviderFactory
public static ILlmProvider Create(ModelRoute route, string? apiKey, ILoggerFactory? loggerFactory = null, int maxTokens = 2048, int? contextTokens = null);
public static ILlmProvider CreateForModel(string modelName, string? apiKey, ILoggerFactory? loggerFactory = null, int maxTokens = 2048, int? contextTokens = null);

// TechieRag.Llm.LlmConnectorCatalog
public static IReadOnlyList<LlmConnectorDescriptor> All { get; }
public static LlmConnectorDescriptor? Find(string? name);
public static LlmConnectorDescriptor Require(string name);
public sealed record LlmConnectorDescriptor { string Name; string DisplayName; LlmSource Source; string? Endpoint; IReadOnlyList<string> ModelPrefixes; string? DefaultModel; bool RequiresApiKey = true; string? SessionHeader; SubscriptionTerms? Subscription; }
public const string OpenCodeGoName = "opencode-go";   // OpenCode Go: https://opencode.ai/zen/go/v1, session header x-opencode-session

// TechieRag.LlmConfig - configuration can name the connector instead of pasting its URL
public string? Connector { get; set; }   // e.g. "groq"; an explicit Endpoint still wins
public Dictionary<string, string> Headers { get; set; }   // extra headers on every OpenAI-compatible request; set in code (WithLlmHeaders), refused in a configuration section; one named here replaces the library's own
public string? SessionHeader { get; set; }   // header carrying LlmCompletionOptions.SessionId; null = the connector's own (opencode-go: x-opencode-session)
public int? MaxContextTokens { get; set; }   // context window; Ollama gets it as options.num_ctx; null (default) = the runtime's own

// TechieRag.Models.LlmCompletionOptions
public string? SessionId { get; set; }   // stable per conversation; sent in the session header; null = one id per provider instance

// TechieRag.TechieRagBuilder - any OpenAI-compatible service that needs more than a key
public TechieRagBuilder UseOpenAICompatibleLlm(string endpoint, string apiKey, string model,
    IReadOnlyDictionary<string, string>? headers, string? sessionHeader = null);
```

OpenCode Go refuses a request without `x-opencode-session` (HTTP 400 `MissingSessionID`). The `opencode-go` connector sends it; pass the same `SessionId` on every turn of one conversation. Only Go's `chat/completions` models work through it (Kimi, GLM, DeepSeek, …); its `/responses` and `/messages` models need those wire formats. OpenAI-compatible requests carry `User-Agent: TechieRag/<version>` unless `Headers` names another, as OpenCode Go asks each client to name itself.

Ollama sizes its context window per request and defaults to a small one (2k–4k tokens), silently dropping the front of a longer prompt. Set `LlmConfig.MaxContextTokens`, or pass `contextTokens` to `Create` / `CreateForModel`, and the Ollama provider sends it as `options.num_ctx`. It is unset by default because Ollama allocates the whole window up front. A reply Ollama cut off at the token limit reports `FinishReason == "length"`, as the other providers do.

```csharp
var route = ModelRouter.Require("ollama/llama3.2");
var ollama = LlmProviderFactory.Create(route, null, contextTokens: 32768);   // sends options.num_ctx: 32768
var reply = await ollama.ChatAsync(messages, new LlmCompletionOptions { MaxTokens = 4000 });
if (reply.FinishReason == "length") { /* truncated: raise MaxTokens or continue */ }
```

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseConnectorLlm(LlmConnectorCatalog.OpenCodeGoName, openCodeGoKey, "kimi-k2.7-code")
    .Build();

var provider = LlmProviderFactory.CreateForModel("opencode-go/kimi-k3", openCodeGoKey);
var reply = await provider.ChatAsync(messages, new LlmCompletionOptions { SessionId = conversation.Id });
```

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseLlmForModel("claude-sonnet-4-5", apiKey: anthropicKey)   // routes to Anthropic
    .Build();

var route = ModelRouter.Require("groq/llama-3.3-70b-versatile");   // Connector.Name "groq", Source OpenAICompatible
var gemini = LlmProviderFactory.CreateForModel("gemini-2.0-flash", googleKey);
```

### 7. Connectors and web ingestion

Package `TechieRag`; namespaces `TechieRag.Connectors`, `TechieRag.Connectors.Repository`, `TechieRag.Connectors.Email`, `TechieRag.Connectors.Http`, `TechieRag.Web`. A connector lists items, keeps a `ConnectorSyncState` so the next run fetches only what changed, and stops cleanly at a budget with a `LimitCode` (the state is kept; the next run resumes). Every outbound HTTP call goes through the connect-time SSRF guard: private, loopback and link-local targets are refused, also after a redirect or a DNS rebinding.

```csharp
// TechieRag.Connectors.ConnectorRunner
public ConnectorRunner(ILogger<ConnectorRunner>? logger = null, TimeProvider? timeProvider = null);
public Task<ConnectorRunResult> RunAsync(IDataConnector connector, ConnectorSyncState? previousSync = null,
    ConnectorRunOptions? options = null, CancellationToken cancellationToken = default);
public Task<ConnectorRunResult> RunAsync(IDataConnector connector, ConnectorSyncState? previousSync, ConnectorRunOptions? options,
    Func<ConnectorDocument, CancellationToken, Task> onDocument, CancellationToken cancellationToken = default);
public sealed record ConnectorRunResult(IReadOnlyList<ConnectorDocument> Documents, IReadOnlyList<ConnectorItem> Unchanged,
    IReadOnlyList<ConnectorItemFailure> Failures, ConnectorSyncState Sync, bool ReachedLimit = false) { public string? LimitCode { get; init; } }

// TechieRag.Connectors.ConnectorIngestionExtensions - run and ingest into rag in one call
public static Task<ConnectorIngestionResult> IngestConnectorAsync(this ITechieRag rag, IDataConnector connector,
    ConnectorSyncState? previousSync = null, ConnectorRunOptions? options = null, CancellationToken cancellationToken = default);
public sealed record ConnectorIngestionResult(IReadOnlyList<string> DocumentIds, IReadOnlyList<ConnectorItemFailure> Skipped,
    ConnectorSyncState Sync, bool ReachedLimit = false) { public string? LimitCode { get; init; } }

public sealed class ConnectorRunOptions { MaxItems = 500; MaxPages = 200; MaxItemBytes = 2 MB; MaxTotalBytes = 64 MB; RequestDelay = 100 ms; MaxConsecutiveFailures = 25; ReportUnchanged; }

// TechieRag.Connectors.ConnectorErrorCodes - switch on these, never on the English message
public const string RunByteBudgetReached = "ConnectorRunByteBudgetReached";     // LimitCode
public const string RunItemLimitReached = "ConnectorRunItemLimitReached";       // LimitCode
public const string RunPageLimitReached = "ConnectorRunPageLimitReached";       // LimitCode
public const string ImapLiteralTooLarge = "ConnectorImapLiteralTooLarge";       // ConnectorException.ErrorCode
public const string ImapResponseLineTooLong = "ConnectorImapResponseLineTooLong";   // ConnectorException.ErrorCode

// TechieRag.Connectors.Repository - GitHub or GitLab
public RepositoryConnector(IConnectorTransport transport, RepositoryConnectorOptions options, ILogger<RepositoryConnector>? logger = null);
public sealed class RepositoryConnectorOptions { RepositoryHost Host = GitHub; string ProjectPath; string? Branch; string? ApiBaseUrl; string? WebBaseUrl; string? AccessToken; IList<string> IncludeGlobs; IList<string> ExcludeGlobs; int PageSize = 100; }
public enum RepositoryHost { GitHub, GitLab }
public HttpConnectorTransport(HttpClient httpClient, ILogger<HttpConnectorTransport>? logger = null, bool blockPrivateTargets = true);   // TechieRag.Connectors.Http

// TechieRag.Connectors.Email - IMAP or mbox
public EmailConnector(IMailTransport transport, EmailConnectorOptions options, IEnumerable<IDocumentProcessor>? attachmentProcessors = null, ILogger<EmailConnector>? logger = null);
public static ImapMailTransport ImapMailTransport.Create(ImapMailboxOptions options, ILogger<ImapMailTransport>? logger = null);
public sealed class ImapMailboxOptions { string Host; int Port = 993; string Username; string Password; bool UseOAuthBearer; TimeSpan Timeout = 60 s; long MaxMessageBytes = 64 MB; }
public MboxMailTransport(string path);
public sealed class EmailConnectorOptions { IList<string> Folders = ["INBOX"]; DateTimeOffset? SinceUtc; string? SenderContains; string? SubjectContains; string? AccountAddress; bool IncludeSentByMe; bool IncludeSpam; bool IncludeAttachments; IList<string> AttachmentExtensions; long MaxAttachmentBytes = 8 MB; bool StripQuotedReplies = true; bool StripSignatures = true; int PageSize = 50; }

// TechieRag.Connectors.Email - mail actions on the same IMAP login (the reader above stays read-only)
public static ImapMailActions ImapMailActions.Create(ImapMailboxOptions options, ILogger<ImapMailActions>? logger = null);
public Task<IReadOnlyList<MailActionResult>> ApplyAsync(IEnumerable<MailAction> actions, CancellationToken cancellationToken = default);
public Task<IReadOnlyList<MailActionPlan>> DryRunAsync(IEnumerable<MailAction> actions, CancellationToken cancellationToken = default);   // sends no command that changes the mailbox
public Task<IReadOnlyList<MailActionResult>> MoveAsync / TrashAsync / AddLabelAsync / RemoveLabelAsync(IEnumerable<MailMessageRef> messages, ...);
public sealed record MailMessageRef(string Folder, string Uid, string? UidValidity = null) { static FromHeader(MailHeader); static FromConnectorItemId(string); }
public sealed record MailAction(MailMessageRef Message, MailActionKind Kind, string? Target = null) { static Move(m, folder); AddLabel(m, label); RemoveLabel(m, label); Trash(m); }
public sealed record MailActionResult(MailAction Action, MailActionOutcome Outcome /* Done, Skipped, Failed */, MailMoveStrategy Strategy, string? TargetFolder, string? Code, string? Reason);
public sealed record MailActionPlan(MailAction Action, MailActionOutcome Outcome, MailMoveStrategy Strategy /* Move, CopyAndExpunge */, string? TargetFolder, IReadOnlyList<string> Commands, string? Code, string? Reason);
// MailActionCodes: MoveNotSupported, LabelsNotSupported, TrashFolderNotFound, TargetFolderNotFound, MessageNotFound, UidValidityChanged, AlreadyInFolder, ServerRefused, CopiedNotRemoved, InvalidArgument, FolderNotFound, ConnectionLost
// Trash = the \Trash special-use folder or [Gmail]/Trash, never a permanent delete; move = UID MOVE, else UID COPY + \Deleted + UID EXPUNGE of that UID; labels = Gmail X-GM-LABELS only

// TechieRag.Web.WebIngestionExtensions
public static Task<string> IngestUrlAsync(this ITechieRag rag, string url, IWebContentFetcher fetcher, CancellationToken cancellationToken = default);   // returns the document id
public static Task<WebIngestionResult> IngestSiteAsync(this ITechieRag rag, string seedUrl, IWebContentFetcher fetcher,
    WebCrawlOptions? options = null, CancellationToken cancellationToken = default);
public sealed class WebCrawlOptions { int MaxDepth = 1; int MaxPages = 25; bool SameHostOnly = true; TimeSpan RequestDelay = 250 ms; bool BlockPrivateNetworkTargets = true; }

// TechieRag.Web.HttpWebContentFetcher : IWebContentFetcher (8 MB page cap)
public HttpWebContentFetcher(HttpClient httpClient, ILogger<HttpWebContentFetcher>? logger = null, bool blockPrivateTargets = true);
public static HttpClient CreateDefaultClient(bool blockPrivateTargets = true);          // an HttpClient over the guarded handler
public static SocketsHttpHandler CreateGuardedHandler(bool blockPrivateTargets = true);  // the SSRF guard, for your own HttpClient
```

```csharp
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using TechieRag.Connectors.Http;
using TechieRag.Connectors.Repository;
using TechieRag.Web;

// A GitHub repository, incrementally: keep result.Sync and pass it back as previousSync next time
var transport = new HttpConnectorTransport(new HttpClient());
var repo = new RepositoryConnector(transport, new RepositoryConnectorOptions
{
    Host = RepositoryHost.GitHub,
    ProjectPath = "techierathore/TechieRag",
    Branch = "main",
    AccessToken = config["GitHub:Token"],
    IncludeGlobs = ["docs/**/*.md"]
});
var result = await rag.IngestConnectorAsync(repo, previousSync: savedState, new ConnectorRunOptions { MaxItems = 200 });
if (result.ReachedLimit && result.LimitCode == ConnectorErrorCodes.RunItemLimitReached)
{
    // the budget ended the run early; result.Sync resumes it next time
}
savedState = result.Sync;

// Run without ingesting, to inspect the documents first
var run = await new ConnectorRunner().RunAsync(repo, savedState, new ConnectorRunOptions());

// Email over IMAP (or new MboxMailTransport("archive.mbox") for an exported mailbox)
var mail = new EmailConnector(
    ImapMailTransport.Create(new ImapMailboxOptions { Host = "imap.example.com", Username = user, Password = secret }),
    new EmailConnectorOptions { Folders = ["INBOX"], SinceUtc = DateTimeOffset.UtcNow.AddDays(-30), IncludeAttachments = true });
await rag.IngestConnectorAsync(mail);

// Act on mail over the same login: dry run first, then apply; one result per message
var mailActions = ImapMailActions.Create(new ImapMailboxOptions { Host = "imap.gmail.com", Username = user, Password = appPassword });
var message = MailMessageRef.FromConnectorItemId(item.Id);   // or MailMessageRef.FromHeader(header)
MailAction[] actions = [MailAction.Move(message, "Archive"), MailAction.AddLabel(message, "Receipts"), MailAction.Trash(other)];
var plans = await mailActions.DryRunAsync(actions);          // plans[i].Commands, .Strategy, .TargetFolder; mailbox unchanged
var results = await mailActions.ApplyAsync(actions);
foreach (var failed in results.Where(r => r.Outcome == MailActionOutcome.Failed))
{
    // switch on failed.Code, e.g. MailActionCodes.LabelsNotSupported on a non-Gmail server
}

// Web pages: one URL, or a crawl from a seed; both go through the SSRF guard
var fetcher = new HttpWebContentFetcher(HttpWebContentFetcher.CreateDefaultClient());
var documentId = await rag.IngestUrlAsync("https://example.com/handbook", fetcher);
var site = await rag.IngestSiteAsync("https://example.com/docs/", fetcher, new WebCrawlOptions { MaxDepth = 2, MaxPages = 50 });
```

### 8. Reranking: `WithReranker`, `UseEmbeddedReranker`, `SearchOptions.Rerank`

Package `TechieRag` (Cohere, Jina, custom) or `TechieRag.Embedded` (the ONNX cross-encoder bge-reranker-v2-m3, `RerankSource.LocalOnnx`, downloaded once). The vector search fetches `candidateCount` chunks, the reranker orders them and returns `topN`. Configuration alone cannot select `LocalOnnx`: `"Rerank": { "Source": "LocalOnnx" }` throws at `Build()` unless the Embedded package's `UseEmbeddedReranker()` supplied the reranker.

```csharp
// TechieRag.TechieRagBuilder
public TechieRagBuilder WithReranker(RerankSource source, string apiKey, string? model = null, string? endpoint = null, int topN = 0, int candidateCount = 20);   // Cohere or Jina
public TechieRagBuilder WithReranker(Func<IReranker> factory, int topN = 0, int candidateCount = 20);                                                       // RerankSource.Custom
public enum RerankSource { None, Cohere, Jina, LocalOnnx, Custom }

// TechieRag.Embedded.TechieRagBuilderExtensions
public static TechieRagBuilder UseEmbeddedReranker(this TechieRagBuilder builder, string? modelDirectory = null, int topN = 0, int candidateCount = 20);   // sets RerankSource.LocalOnnx

// TechieRag.ITechieRag
Task<IReadOnlyList<SearchResult>> SearchAsync(string query, SearchOptions? options, CancellationToken cancellationToken = default);
IReranker? GetReranker();
public class SearchOptions { public int TopK { get; set; } = 5; public string? DocumentFilter { get; set; } public bool? Rerank { get; set; } }   // TechieRag.Models
```

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .UseEmbeddedReranker()                                       // RerankSource.LocalOnnx: bge-reranker-v2-m3, downloads once
    // .WithReranker(RerankSource.Cohere, apiKey: cohereKey)   // or an API reranker
    .Build();

var hits = await rag.SearchAsync("termination clause", new SearchOptions { TopK = 5, Rerank = true });
```

### 9. Workspaces and persistence: `WithPersistence`, `GetWorkspaceManager`

Package `TechieRag`; namespaces `TechieRag`, `TechieRag.Services`, `TechieRag.Models`. Conversation threads and workspaces (a name, a system prompt, a model, pinned documents) are stored in SQLite or PostgreSQL in tables the library creates on first use (`TrThread`, `TrMessage`, `TrWorkspace`, `TrWorkspaceDocument`).

```csharp
// TechieRag.TechieRagBuilder
public TechieRagBuilder WithPersistence(StoreProvider provider, string connectionString, string defaultUserId = "default");
public enum StoreProvider { None, Sqlite, Postgres }

// TechieRag.ITechieRag - null when WithPersistence was not called
IConversationStore? GetConversationStore();
Services.WorkspaceManager? GetWorkspaceManager();

// TechieRag.Services.WorkspaceManager
public Task<Workspace> CreateWorkspaceAsync(string name, Action<Workspace>? configure = null, CancellationToken cancellationToken = default);
public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default);
public Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
public Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);
public Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
public Task<string> IngestTextAsync(...);  public Task<string> IngestFileAsync(...);  public Task AddExistingDocumentAsync(...);
public IWorkspaceStore GetStore();
public event EventHandler<ContextTruncatedEventArgs>? ContextTruncated;
```

```csharp
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec("Data Source=app.db")
    .UseOllamaLlm()
    .WithPersistence(StoreProvider.Sqlite, "Data Source=ws.db")
    .Build();
await rag.InitializeAsync();

var workspaces = rag.GetWorkspaceManager()!;
var contracts = await workspaces.CreateWorkspaceAsync("Contracts", w => w.SystemPrompt = "Answer from the contracts only.");
var all = await workspaces.ListWorkspacesAsync();
```

### 10. MCP tool servers and flows

Package `TechieRag`; namespaces `TechieRag.Mcp`, `TechieRag.Orchestration`. An MCP server's tools join the agent loop as one `IToolHandler`; a trust policy decides what a server may do. A flow is a persisted graph of agent, tool, condition, handoff and terminal nodes, serialised as JSON and run by `FlowRunner`; its refusals are `FlowMessage` codes with arguments, never English sentences, so the host can localise.

```csharp
// TechieRag.Mcp
public sealed class McpServerConfig
{
    public required string Name { get; init; }
    public required McpTransportKind Transport { get; init; }        // Stdio or Http
    public string? Command { get; init; }                            // Stdio
    public IReadOnlyList<string> Arguments { get; init; } = [];      // Stdio
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Endpoint { get; init; }                           // Http
    public IReadOnlyDictionary<string, string> Headers { get; init; }
    public int TimeoutSeconds { get; init; } = 60;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];   // empty: every tool the server lists
}
public sealed class McpTrustPolicy { public static McpTrustPolicy Strict { get; } bool AllowLocalProcessLaunch; IReadOnlyList<string> AllowedCommandDirectories; bool AllowPlaintextHttp; int MaxToolResultCharacters = 100000; }
public static McpClient McpClient.Create(McpServerConfig config, McpTrustPolicy policy, ILoggerFactory? loggerFactory = null);
public static Task<McpToolHandler> McpToolHandler.CreateAsync(IEnumerable<McpClient> clients, McpTrustPolicy policy,
    ILogger<McpToolHandler>? logger = null, CancellationToken cancellationToken = default);   // McpToolHandler : IToolHandler, IAsyncDisposable
public static string McpToolHandler.QualifyToolName(string serverName, string toolName);

// TechieRag.Orchestration
public static FlowDefinition FlowSerializer.FromJson(string json);
public static string FlowSerializer.ToJson(FlowDefinition flow);
public static bool FlowSerializer.TryFromJson(string? json, out FlowDefinition? flow, out string? error);
public FlowAgent(string id, ILlmProvider llmProvider, IToolHandler? tools = null);
public InMemoryFlowAgentResolver(params FlowAgent[] agents);            // IFlowAgentResolver
public FlowRuntime(IFlowAgentResolver agents);
public FlowRunner(FlowDefinition flow, FlowRuntime runtime, ILogger<FlowRunner>? logger = null, int depth = 0);
public Task<FlowRunResult> RunAsync(string input, IReadOnlyDictionary<string, string>? variables = null,
    IProgress<AgentStep>? progress = null, CancellationToken cancellationToken = default);
```

```csharp
using TechieRag.Mcp;
using TechieRag.Orchestration;

// MCP: a local server over stdio (the policy must allow launching a process)
var policy = new McpTrustPolicy { AllowLocalProcessLaunch = true };
var server = McpClient.Create(new McpServerConfig
{
    Name = "filesystem",
    Transport = McpTransportKind.Stdio,
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "./docs"]
}, policy);
await using var mcpTools = await McpToolHandler.CreateAsync([server], policy);

var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()
    .UseOllamaLlm()
    .WithToolHandler(mcpTools)          // the server's tools, named "<server>.<tool>", in every AskAsync agent loop
    .Build();

// Flows: a persisted graph, run with the agents it names
var flow = FlowSerializer.FromJson(await File.ReadAllTextAsync("triage.flow.json"));
var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("triage", rag.GetLlmProvider()!, mcpTools)));
var outcome = await new FlowRunner(flow, runtime).RunAsync("A customer reports a login failure");
```

### 11. Telemetry: `TechieRag.Telemetry`

Package `TechieRag.Telemetry`; namespace `TechieRag.Telemetry`. The core records traces and metrics through one `ActivitySource` and one `Meter`, both named `TechieRag` (`techierag.llm.*`, `techierag.ingestion.*`, `techierag.search.*`; the activity `TechieRag.Search`), gated by `TechieRagTelemetry.Enabled` (`EnableTelemetry` in configuration). Nothing leaves the process until this package is installed and tracing or metrics is enabled; a non-local endpoint is refused unless `AllowRemoteEndpoint` is set.

```csharp
// TechieRag.Telemetry.TechieRagTelemetryServiceCollectionExtensions
public static IServiceCollection AddTechieRagTelemetry(this IServiceCollection services, Action<TechieRagTelemetryOptions>? configure = null);

public sealed class TechieRagTelemetryOptions
{
    public const string DefaultEndpoint = "http://localhost:4318";
    public bool EnableTracing { get; set; }                                            // off by default: nothing is exported
    public bool EnableMetrics { get; set; }
    public TechieRagTelemetrySink Sink { get; set; } = TechieRagTelemetrySink.Otlp;   // or TechieRagTelemetrySink.Console
    public Uri Endpoint { get; set; } = new(DefaultEndpoint);
    public bool AllowRemoteEndpoint { get; set; }
    public TimeSpan MetricExportInterval { get; set; } = TimeSpan.FromSeconds(15);
    public string ServiceName { get; set; } = "TechieRag";
}
```

```csharp
using TechieRag.Telemetry;

builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));
builder.Services.AddTechieRagTelemetry(o =>
{
    o.EnableTracing = true;
    o.EnableMetrics = true;
    o.Endpoint = new Uri("http://localhost:4318");   // an OTLP collector; Sink = TechieRagTelemetrySink.Console prints instead
});
```

### 12. Configuration: `AddTechieRag(IConfiguration)` maps every field except keys

`AddTechieRag(IConfiguration)` and `AddTechieRag(TechieRagConfig)` go through one mapper, so every bound field reaches the builder: the embedding `Dimensions`, `ApiFormat`, `ApiPath` and `RequestDelayMs`, the whole `Prompt` section, `Rerank`, `Persistence`, `Resilience`, `UsageTracking`, `LlmFallback`, `EnableTelemetry` and `Llm.Connector`. Pass the `TechieRag` section, not the root configuration.

**Keys and dependencies are injected only through code.** A section holding any `ApiKey` (embedding, vector store, LLM, fallback LLM, reranker) or `Headers` value is refused with an `InvalidOperationException` naming the setting, never its value. Pass keys with the second overload, which maps the section and then runs your code:

```csharp
// TechieRag.DependencyInjection.ServiceCollectionExtensions
public static IServiceCollection AddTechieRag(this IServiceCollection services, IConfiguration configuration, Action<TechieRagBuilder> configure);

// TechieRag.TechieRagBuilder
public TechieRagBuilder WithApiKeys(string? llm = null, string? embedding = null, string? vectorStore = null, string? rerank = null, string? llmFallback = null);
public TechieRagBuilder WithLlmHeaders(IReadOnlyDictionary<string, string> headers);

builder.Services.AddTechieRag(
    builder.Configuration.GetSection("TechieRag"),
    rag => rag.WithApiKeys(llm: secrets.OpenAiKey, vectorStore: secrets.QdrantKey));
```

`Llm.Source` accepts two new values:
- `Local` - the in-process model from `TechieRag.Local`. Call `LocalLlm.Register()` once at startup, before the first `Build()`; then `"Llm": { "Source": "Local", "Model": "qwen2.5-0.5b-instruct" }` needs no endpoint and no key. `"Model": "local/qwen2.5-0.5b-instruct"` reaches it through `UseLlmForModel` as well.
- `Subscription` - a consumer subscription reached through the vendor's sign-in flow. **It cannot be configured from appsettings alone**: the library needs the sign-in callback only the host can provide (open the browser, show the code), so register it in code with `UseChatGptSubscriptionLlm` inside `AddTechieRag(Action<TechieRagBuilder>)`.

```csharp
// Program.cs
LocalLlm.Register();                                                              // only when appsettings says "Source": "Local"
builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));

// A subscription needs the host's sign-in callback, so it is registered in code, not from appsettings alone
builder.Services.AddTechieRag(rag => rag
    .UseEmbedded()
    .UseSqliteVec()
    .UseChatGptSubscriptionLlm(async (prompt, ct) => await OpenSignInAsync(prompt.VerificationUri, prompt.UserCode, ct)));
```

---

## Phase-3 Additions (Sevak feedback, cluster A)

Every member below is additive: no existing call changes meaning except where a row says so (the per-app SQLite default and the tool-registry replacement).

### Workspaces: one pinned search, the workspace rerank rule, `IWorkspaceManager`

Package `TechieRag`; namespaces `TechieRag.Abstractions`, `TechieRag.Models`. Pinned documents are retrieved with ONE search filtered to the whole pinned set (`SearchOptions.DocumentFilters`, `IVectorStore.SearchDocumentsAsync`). **Rerank rule:** every workspace retrieval, pinned results included, follows `Workspace.RerankEnabled`; the library-wide `Rerank.Enabled` never overrides it inside a workspace. Services built on workspaces take `IWorkspaceManager`, which `WorkspaceManager` implements and `AddTechieRag` registers with `TryAdd`, so a test double needs no library client.

```csharp
// One search over a set of documents
var hits = await rag.SearchAsync("refund window", new SearchOptions { TopK = 10, DocumentFilters = ["doc-1", "doc-2"], Rerank = false });
var raw = await vectorStore.SearchDocumentsAsync(queryVector, 10, ["doc-1", "doc-2"]);

// A host service depends on the interface; a test passes a double
public sealed class WorkspaceTitles(IWorkspaceManager workspaces)
{
    public async Task<IReadOnlyList<string>> AllAsync() => (await workspaces.ListWorkspacesAsync()).Select(w => w.Name).ToList();
}
IWorkspaceManager manager = rag.GetWorkspaceManager()!;   // GetWorkspaceManager() keeps returning WorkspaceManager
```

### Connectors: partial runs, typed outcomes, public metadata builder, re-sync by key

Package `TechieRag`; namespace `TechieRag.Connectors`. A cancelled run throws `ConnectorRunCanceledException` (still an `OperationCanceledException`) whose `PartialResult` carries the sync state and per-item reasons gathered so far; a failing run carries the same on `ConnectorException.PartialResult`. Through `IngestConnectorAsync` both also carry `PartialIngestion`. The partial sync state keeps the previous `LastRunUtc`, so the next run resumes. Each `ConnectorItemFailure` has a typed `Outcome` (`ConnectorItemOutcome.Failed` or `ConnectorItemOutcome.Skipped`); byte sizes in reasons are invariant digits with no thousands separator. `ConnectorIngestionExtensions.BuildMetadata` and `ConnectorIngestionExtensions.ConnectorDocumentKey` are public, and `IngestConnectorAsync` ingests each item under its key, so a re-synced item replaces its earlier document.

```csharp
try
{
    var result = await rag.IngestConnectorAsync(connector, previousSync, options, ct);
    await SaveSyncAsync(result.Sync);
    var skipped = result.Skipped.Where(f => f.Outcome == ConnectorItemOutcome.Skipped);
}
catch (ConnectorRunCanceledException cancelled)
{
    await SaveSyncAsync(cancelled.PartialIngestion?.Sync ?? cancelled.PartialResult.Sync);   // resume next time
}
catch (ConnectorException failed) when (failed.PartialResult is not null)
{
    await SaveSyncAsync(failed.PartialResult.Sync);
}

var metadata = ConnectorIngestionExtensions.BuildMetadata(connector, item);
var key = ConnectorIngestionExtensions.ConnectorDocumentKey(connector, item);
```

### Text ingestion under a caller key: `IngestTextAsync(text, name, sourceKey, metadata)`

Package `TechieRag`. Ingesting again under the same key replaces the earlier document (same document id, workspace memberships kept); the key is stored as `DocumentMetadataKeys.SourceKey`. A null key behaves like the original overload.

```csharp
var id = await rag.IngestTextAsync(noteText, "Meeting notes", sourceKey: "notes/2026-10-06", metadata: null);
```

### Mail parsing notes: `ParsedMailMessage.Notes`

Package `TechieRag`; namespace `TechieRag.Connectors.Email`. A part nested deeper than `MimeParser.MaxNestingDepth`, or beyond `MimeParser.MaxAttachments`, is listed as a `MailParseNote` (`Code` from `MailParseCodes`, `PartName`, `Depth`) instead of vanishing. Existing callers are unaffected.

```csharp
var parsed = MimeParser.Parse(rawBytes);
foreach (var note in parsed.Notes.Where(n => n.Code == MailParseCodes.NestingTooDeep))
{
    logger.LogWarning("Skipped {Part} at depth {Depth}", note.PartName, note.Depth);
}
```

Mail synced through `EmailConnector` carries the same notes on the item, under `EmailConnector.ParseNotesMetadataKey` (`"MailParseNotes"`), as `code: part name (depth n)` entries joined by `"; "`. The key is absent when the whole message was read.

```csharp
var document = await emailConnector.FetchAsync(item, ct);
if (document.Item.Metadata?.TryGetValue(EmailConnector.ParseNotesMetadataKey, out var notes) == true)
{
    logger.LogWarning("Mail {Name} was not read in full: {Notes}", document.Item.Name, notes);
}
```

### Tools: registering a name again replaces it

`ToolRegistry.Register` with a name already registered (case-insensitive) replaces the definition as well as the handler, keeping its position, so the tool list sent to the model holds each name once.

```csharp
tools.Register("get_weather", "Gets the weather", schema, args => "sunny");
tools.Register("get_weather", "Gets the weather in Celsius", schema, args => "21 C");   // one get_weather, the new one
```

### Per-app SQLite database: `DataRoot`, `UseSqliteVec()`, `WithPersistence(StoreProvider.Sqlite)`

Package `TechieRag`; namespace `TechieRag.Models`. When an app names no database folder, an existing `techierag.db` in the folder the app runs from is used (a warning names the folder; the file is never moved); otherwise the database is `<per-user TechieRag folder>/data/<app name>/techierag.db`, beside `models/`. Order: `DataRoot.Set` in code, then the data folder beside the model root (so `ModelRoot.Set` or `TECHIERAG_MODEL_ROOT` moves it), then `<LocalApplicationData>/TechieRag/data`. No new environment variable. `DataRoot.DefaultDatabasePath` returns the location; `DataRoot.SetAppName` overrides the entry-assembly name. Applies to `UseSqliteVec()`, the `VectorStore.ConnectionString` default, `WithPersistence(StoreProvider.Sqlite)` / Persistence `Provider: Sqlite` with no connection string, and the parameterless `SqliteVecStore(dimensions)`, `SqliteConversationStore()` and `SqliteWorkspaceStore()`. A path the caller sets is used exactly as given.

```csharp
DataRoot.SetAppName("HelpDesk");                       // optional; defaults to the entry assembly name
var rag = new TechieRagBuilder()
    .UseEmbedded()
    .UseSqliteVec()                                    // no folder: per-app default
    .WithPersistence(StoreProvider.Sqlite)             // same per-app database
    .Build();
Console.WriteLine(DataRoot.DefaultDatabasePath);       // ...\TechieRag\data\HelpDesk\techierag.db
```

### Flows: host step names and model use

Package `TechieRag`; namespace `TechieRag.Orchestration`. `FlowNodeCatalog.CreateNode(kind, id, stepName)` names the node; a host should pass its own, localized name, because the default is the catalogue's English label. `FlowDefinition.UsesLanguageModel()` and `FlowDefinition.GetLanguageModelSteps()` say whether, and which, steps call a model (methods, so they are never persisted).

```csharp
var node = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, id: null, stepName: localizer["StepTriage"]);
if (flow.UsesLanguageModel())
{
    var modelSteps = flow.GetLanguageModelSteps().Select(n => n.DisplayName);
}
```

---

## ITechieRag Interface - Complete Method Reference

### Document Processing (v1)

```csharp
// Initialize the client (must be called before other operations)
Task InitializeAsync(CancellationToken ct = default);

// Ingest a single file
Task<IngestionStats> IngestAsync(string filePath, CancellationToken ct = default);

// Ingest all files in a directory
Task<IngestionStats> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*",
    CancellationToken ct = default);

// Ingest raw text directly
Task<IngestionStats> IngestTextAsync(string text, string documentId,
    Dictionary<string, string>? metadata = null, CancellationToken ct = default);

// Search for similar content
Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5,
    string? documentFilter = null, CancellationToken ct = default);

// Delete a document and its vectors
Task DeleteDocumentAsync(string documentId, CancellationToken ct = default);
```

### Auto-RAG Methods (v2)

```csharp
// RAG: search + generate answer
Task<RagResponse> AskAsync(string question, int topK = 5,
    string? systemPrompt = null, string? documentFilter = null,
    LlmCompletionOptions? options = null, CancellationToken ct = default);

// RAG with streaming response
IAsyncEnumerable<string> AskStreamAsync(string question, int topK = 5,
    string? systemPrompt = null, string? documentFilter = null,
    LlmCompletionOptions? options = null, CancellationToken ct = default);

// RAG chat with conversation history
Task<RagResponse> ChatWithRagAsync(string userMessage,
    IReadOnlyList<ChatMessage>? conversationHistory = null,
    int topK = 5, string? systemPrompt = null,
    LlmCompletionOptions? options = null, CancellationToken ct = default);

// RAG chat with streaming
IAsyncEnumerable<string> ChatWithRagStreamAsync(string userMessage,
    IReadOnlyList<ChatMessage>? conversationHistory = null,
    int topK = 5, string? systemPrompt = null,
    LlmCompletionOptions? options = null, CancellationToken ct = default);
```

### Direct Access (v2)

```csharp
// Get the LLM provider for direct use
ILlmProvider? GetLlmProvider();

// Get the token usage tracker
ITokenTracker GetTokenTracker();

// Get conversation memory (if configured)
IConversationMemory? GetConversationMemory();
```

### Added in v3

```csharp
// Search with options (Rerank = true runs the configured reranker)
Task<IReadOnlyList<SearchResult>> SearchAsync(string query, SearchOptions? options,
    CancellationToken cancellationToken = default);

// Streaming RAG that yields the sources first, then tokens, then the completed answer
IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(...);   // RagStreamEventType: Sources, Token, Completed

// Optional stages and stores (null when not configured)
IReranker? GetReranker();
IConversationStore? GetConversationStore();
Services.WorkspaceManager? GetWorkspaceManager();

// Extension methods from other namespaces
Task<string> IngestUrlAsync(string url, IWebContentFetcher fetcher, CancellationToken ct = default);                                  // TechieRag.Web
Task<WebIngestionResult> IngestSiteAsync(string seedUrl, IWebContentFetcher fetcher, WebCrawlOptions? options = null, CancellationToken ct = default);   // TechieRag.Web
Task<ConnectorIngestionResult> IngestConnectorAsync(IDataConnector connector, ConnectorSyncState? previousSync = null,
    ConnectorRunOptions? options = null, CancellationToken ct = default);                                                            // TechieRag.Connectors
```

---

## ILlmProvider Interface

Access via `rag.GetLlmProvider()` for direct LLM operations without RAG context.

```csharp
public interface ILlmProvider
{
    string Name { get; }
    string ModelName { get; }
    bool SupportsToolCalling { get; }
    bool SupportsStreaming { get; }

    // Single prompt completion
    Task<LlmResponse> CompleteAsync(string prompt,
        LlmCompletionOptions? options = null, CancellationToken ct = default);

    // Streaming completion
    IAsyncEnumerable<string> CompleteStreamAsync(string prompt,
        LlmCompletionOptions? options = null, CancellationToken ct = default);

    // Multi-turn chat
    Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null, CancellationToken ct = default);

    // Streaming chat
    IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null, CancellationToken ct = default);

    // Typed/structured output (JSON deserialization)
    Task<T> CompleteAsync<T>(string prompt,
        LlmCompletionOptions? options = null, CancellationToken ct = default) where T : class;

    // Token estimation
    int EstimateTokenCount(string text);

    // Telemetry event
    event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
}
```

---

## Models Reference

### ChatMessage

```csharp
public class ChatMessage
{
    public required string Role { get; set; }    // "system", "user", "assistant", "tool"
    public string? Content { get; set; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; init; }

    // Factory methods
    public static ChatMessage System(string content);
    public static ChatMessage User(string content);
    public static ChatMessage Assistant(string content);
    public static ChatMessage Tool(string toolCallId, string content);
}
```

### LlmCompletionOptions

```csharp
public class LlmCompletionOptions
{
    public float? Temperature { get; set; }           // 0.0 - 2.0
    public int? MaxTokens { get; set; }
    public float? TopP { get; set; }
    public float? FrequencyPenalty { get; set; }
    public float? PresencePenalty { get; set; }
    public IReadOnlyList<string>? StopSequences { get; set; }
    public string? SystemPrompt { get; set; }
    public bool JsonMode { get; set; }
    public string? JsonSchema { get; set; }
    public IReadOnlyList<ToolDefinition>? Tools { get; set; }
    public string? ToolChoice { get; set; }           // "auto", "none", "required"
    public int? Seed { get; set; }
}
```

### LlmResponse

```csharp
public class LlmResponse
{
    public string? Content { get; set; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }
    public bool HasToolCalls { get; }
    public required TokenUsage Usage { get; set; }
    public string FinishReason { get; set; }          // "stop", "tool_calls", "length"
    public string ModelName { get; set; }
    public ChatMessage ToChatMessage();
}
```

### RagResponse

```csharp
public class RagResponse
{
    public required string Answer { get; set; }
    public required IReadOnlyList<SearchResult> Sources { get; set; }
    public required TokenUsage Usage { get; set; }
    public required string Query { get; set; }
    public string ModelName { get; set; }
}
```

### TokenUsage & Budget

```csharp
public class TokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; }
    public decimal EstimatedCostUsd { get; set; }
    public string ModelName { get; set; }
    public string ProviderName { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TokenUsageSummary
{
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalTokens { get; }
    public decimal TotalEstimatedCostUsd { get; set; }
    public int OperationCount { get; set; }
}

public class UsageBudget
{
    public long MaxTotalTokens { get; set; }          // 0 = unlimited
    public decimal MaxCostUsd { get; set; }           // 0 = unlimited
    public float AlertThreshold { get; set; }         // 0.0-1.0, default 0.8
    public bool BlockOnExceeded { get; set; }
}
```

### ToolDefinition, ToolCall, ToolResult

```csharp
public class ToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string ParametersSchema { get; set; }  // JSON Schema
    public bool RequiresConfirmation { get; set; }
}

public class ToolCall
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string ArgumentsJson { get; set; }
    public T GetArguments<T>() where T : class;
}

public class ToolResult
{
    public required string ToolCallId { get; set; }
    public required string Content { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

## Configuration via appsettings.json

### Full Configuration

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
      "Model": "gpt-4o",
      "Temperature": 0.7,
      "MaxTokens": 2048
    },
    "LlmFallback": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.2",
      "MaxContextTokens": 32768
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
    "Rerank": {
      "Enabled": true,
      "Source": "Cohere",
      "Model": null,
      "TopN": 0,
      "CandidateCount": 20
    },
    "Persistence": {
      "Provider": "Sqlite",
      "ConnectionString": "Data Source=ws.db",
      "DefaultUserId": "default"
    },
    "EnableTelemetry": true
  }
}
```

**Keys never go in this section.** `AddTechieRag(IConfiguration)` refuses any `ApiKey` or `Headers` value with an error naming the setting (never its value). Pass keys in code:

```csharp
builder.Services.AddTechieRag(
    builder.Configuration.GetSection("TechieRag"),
    rag => rag.WithApiKeys(llm: secrets.OpenAiKey, rerank: secrets.CohereKey)   // also embedding:, vectorStore:, llmFallback:
              .WithLlmHeaders(new Dictionary<string, string> { ["User-Agent"] = "myapp/1.0" }));
```

`AddTechieRag(TechieRagConfig)` with a config object built in code may carry keys. The library reads no key from an environment variable.

v3 notes: `Embedding.Dimensions`, the `Prompt` section, `Rerank` and `Persistence` are all bound and applied. `Llm.Connector` names a catalog connector (`"groq"`) instead of pasting its URL. `Llm.Source: "Local"` needs `LocalLlm.Register()` at startup; `Llm.Source: "Subscription"` cannot be configured from appsettings alone (see Phase-2 Features, 12).

### Minimal Configuration (Embedding Only)

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

### LLM Source Values

| LlmSource Value | Provider |
|-----------------|----------|
| `None` | No LLM (embedding/retrieval only - v1 compatibility) |
| `Ollama` | Ollama local server |
| `LmStudio` | LM Studio local server |
| `OpenAICompatible` | OpenAI-compatible REST API |
| `AzureAIFoundry` | Azure AI Foundry |
| `GoogleGemini` | Google Gemini API |
| `Anthropic` | Anthropic Claude API |
| `Subscription` | v3: a consumer subscription through the vendor's sign-in flow (ChatGPT today). Needs the host's sign-in callback, so it is registered in code with `UseChatGptSubscriptionLlm`, not from appsettings alone |
| `Local` | v3: the in-process model from `TechieRag.Local`; no endpoint, no key. Call `LocalLlm.Register()` once before `Build()` |

---

## Dependency Injection (ASP.NET Core)

### Option A: Using appsettings.json

```csharp
// Program.cs - pass the TechieRag section
builder.Services.AddTechieRag(builder.Configuration.GetSection("TechieRag"));
```

### Option B: Using Builder (Fluent API)

```csharp
// Program.cs
builder.Services.AddTechieRag(rag =>
{
    rag.UseOllama()
       .UseSqliteVec()
       .UseOpenAICompatibleLlm("https://api.openai.com/v1", "sk-...", "gpt-4o")
       .WithUsageTracking()
       .WithConversationMemory();
});
```

### Injecting into Services/Pages

```csharp
// In a Blazor page or service:
@inject ITechieRag Rag

// Or via constructor injection:
public class MyService
{
    private readonly ITechieRag rag;

    public MyService(ITechieRag rag)
    {
        this.rag = rag;
    }

    public async Task<string> GetAnswer(string question)
    {
        var response = await rag.AskAsync(question);
        return response.Answer;
    }
}
```

---

## IToolHandler Interface

For custom tool handler implementations:

```csharp
public interface IToolHandler
{
    IReadOnlyList<ToolDefinition> ToolDefinitions { get; }
    Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct = default);
}
```

### ToolRegistry (Built-in Implementation)

```csharp
// Register tools with lambda handlers via the builder:
builder.WithTools(tools =>
{
    tools.Register(
        name: "tool_name",
        description: "What this tool does (helps LLM decide when to use it)",
        parametersSchema: """{"type":"object","properties":{"param1":{"type":"string"}},"required":["param1"]}""",
        handler: async (argumentsJson, cancellationToken) =>
        {
            // Parse arguments
            var args = JsonSerializer.Deserialize<MyArgs>(argumentsJson)!;
            // Execute logic
            var result = DoSomething(args.Param1);
            // Return string result (will be sent back to LLM)
            return JsonSerializer.Serialize(result);
        });
});
```

---

## IConversationMemory Interface

```csharp
public interface IConversationMemory
{
    string ConversationId { get; }
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetTrimmedHistoryAsync(int maxTokens,
        Func<string, int> tokenCounter, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task StartNewConversationAsync(string? conversationId = null,
        string? systemMessage = null, CancellationToken ct = default);
}
```

---

## ITokenTracker Interface

```csharp
public interface ITokenTracker
{
    void RecordUsage(TokenUsage usage);
    TokenUsageSummary GetSessionUsage();
    IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel();
    decimal GetEstimatedCost();
    void SetBudget(UsageBudget budget);
    BudgetStatus? GetBudgetStatus();
    void Reset();
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;
    event EventHandler<TokenUsage>? OnUsageRecorded;
}
```

---

## IPromptTemplate Interface

```csharp
public interface IPromptTemplate
{
    IReadOnlyList<ChatMessage> BuildRagPrompt(string userQuery,
        IReadOnlyList<SearchResult> searchResults, string? systemPrompt = null);

    IReadOnlyList<ChatMessage> BuildRagChatPrompt(string userMessage,
        IReadOnlyList<SearchResult> searchResults,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        string? systemPrompt = null);
}
```

---

## Common Implementation Patterns

### Pattern 1: RAG-Powered Chat Page (Blazor)

```csharp
@page "/chat"
@inject ITechieRag Rag

<h1>Ask a Question</h1>
<input @bind="question" />
<button @onclick="AskQuestion">Ask</button>

@if (answer != null)
{
    <div>@answer</div>
    <h4>Sources:</h4>
    @foreach (var source in sources)
    {
        <p>@source.Chunk.Metadata["SourceFile"] (Relevance: @source.Score:P0)</p>
    }
}

@code {
    private string question = "";
    private string? answer;
    private IReadOnlyList<SearchResult> sources = [];

    private async Task AskQuestion()
    {
        var response = await Rag.AskAsync(question, topK: 5);
        answer = response.Answer;
        sources = response.Sources;
    }
}
```

### Pattern 2: Streaming Chat (Blazor)

```csharp
@code {
    private string currentResponse = "";
    private bool isStreaming;

    private async Task StreamAnswer()
    {
        isStreaming = true;
        currentResponse = "";

        await foreach (var token in Rag.AskStreamAsync(question))
        {
            currentResponse += token;
            StateHasChanged();
        }

        isStreaming = false;
    }
}
```

### Pattern 3: Multi-Turn Conversation

```csharp
@code {
    private List<ChatMessage> history = new();

    private async Task SendMessage(string userMessage)
    {
        history.Add(ChatMessage.User(userMessage));

        var response = await Rag.ChatWithRagAsync(
            userMessage,
            conversationHistory: history,
            topK: 5);

        history.Add(ChatMessage.Assistant(response.Answer));
    }
}
```

### Pattern 4: Background Service with RAG

```csharp
public class RagBackgroundService : BackgroundService
{
    private readonly ITechieRag rag;

    public RagBackgroundService(ITechieRag rag) => this.rag = rag;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await rag.InitializeAsync(stoppingToken);
        await rag.IngestDirectoryAsync("./knowledge-base", cancellationToken: stoppingToken);

        // Ready to answer queries
    }
}
```

### Pattern 5: Custom Tool Handler Class

```csharp
public class AstrologyToolHandler : IToolHandler
{
    public IReadOnlyList<ToolDefinition> ToolDefinitions => new[]
    {
        new ToolDefinition
        {
            Name = "calculate_birth_chart",
            Description = "Calculates astrological birth chart for given date and location",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "birthDate": { "type": "string", "description": "Date in YYYY-MM-DD" },
                    "birthTime": { "type": "string", "description": "Time in HH:MM" },
                    "location": { "type": "string", "description": "City name" }
                },
                "required": ["birthDate", "location"]
            }
            """
        }
    };

    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
    {
        var args = toolCall.GetArguments<BirthChartArgs>();
        var chart = await CalculateChart(args.BirthDate, args.BirthTime, args.Location);

        return new ToolResult
        {
            ToolCallId = toolCall.Id,
            Content = JsonSerializer.Serialize(chart),
            IsSuccess = true
        };
    }
}

// Register:
builder.WithToolHandler(new AstrologyToolHandler());
```

---

## Coding Standards

When generating code that uses TechieRag, follow these conventions:

1. **PascalCase** for classes, methods, properties, constants
2. **camelCase** for private fields: `private readonly ITechieRag rag;`
3. **No underscores** in any names
4. **Async suffix** on all async methods
5. **XML documentation** on public members
6. **File-scoped namespaces**
7. **Nullable reference types** enabled
8. **ConfigureAwait(false)** in library code (not in Blazor/UI code)
9. **Early returns** for validation
10. **One class per file**, file name matches class name

---

## Backward Compatibility

- All v1 methods (IngestAsync, SearchAsync, etc.) work unchanged
- When `Llm.Source` is `None` (default), LLM methods throw `InvalidOperationException`
- No changes needed to existing v1 code
- v2 upgrade: just add LLM configuration to existing builder

---

## Namespace Reference

| Namespace | Contains |
|-----------|----------|
| `TechieRag` | ITechieRag, TechieRagClient, TechieRagBuilder, TechieRagConfig, LlmSource, RerankSource, StoreProvider |
| `TechieRag.Abstractions` | ILlmProvider, IToolHandler, IConversationMemory, ITokenTracker, IPromptTemplate, IVectorStore, IEmbeddingProvider, IReranker, IConversationStore, IWorkspaceStore, ISubscriptionSessionStore |
| `TechieRag.Models` | ChatMessage, LlmResponse, RagResponse, RagStreamEvent, LlmStreamEvent, AgentStreamEvent, LlmCompletionOptions, SearchOptions, ToolDefinition, ToolCall, ToolResult, TokenUsage, UsageBudget, SearchResult, Workspace, SubscriptionSignInPrompt, SubscriptionSession, ModelRoot |
| `TechieRag.Llm` | OllamaLlmProvider, LmStudioLlmProvider, OpenAICompatibleLlmProvider, AzureAIFoundryLlmProvider, GoogleGeminiLlmProvider, AnthropicLlmProvider, ChatGptSubscriptionLlmProvider, ChatGptSubscriptionOptions, SubscriptionSignInException, LlmProviderFactory, ModelRouter, ModelRoute, LlmConnectorCatalog, LlmConnectorDescriptor, LlmStreamEventExtensions |
| `TechieRag.Services` | TokenUsageTracker, InMemoryConversationMemory, AgentLoopRunner, ToolRegistry, PromptTemplateEngine, RetryHandler, FallbackLlmHandler, WorkspaceManager |
| `TechieRag.Agentic` | RegisterKnowledgeBase (ToolRegistry extension), TechieRagRetrievalSource, IRetrievalSource, RetrievalToolOptions, RetrievalTurnState, RetrievalTrace, AgenticInstructions, KnowledgeBaseTools |
| `TechieRag.Connectors` (+ `.Repository`, `.Email`, `.Http`) | ConnectorRunner, IngestConnectorAsync, ConnectorRunOptions, ConnectorSyncState, ConnectorErrorCodes, RepositoryConnector, EmailConnector, ImapMailTransport, ImapMailActions, MailActionCodes, MboxMailTransport, HttpConnectorTransport |
| `TechieRag.Web` | IngestUrlAsync, IngestSiteAsync, HttpWebContentFetcher, WebCrawlOptions, WebIngestionResult |
| `TechieRag.Mcp` | McpClient, McpServerConfig, McpTrustPolicy, McpToolHandler |
| `TechieRag.Orchestration` | FlowDefinition, FlowSerializer, FlowRunner, FlowRuntime, FlowAgent, InMemoryFlowAgentResolver, FlowMessage |
| `TechieRag.DependencyInjection` | ServiceCollectionExtensions (AddTechieRag) |
| `TechieRag.Embedded` (package `TechieRag.Embedded`) | UseEmbedded, UseModelRoot, UseEmbeddedReranker, EmbeddedModel, ModelDownloadService, OnnxCrossEncoderReranker |
| `TechieRag.Local` (package `TechieRag.Local`) | UseLocalLlm, LocalLlm, LocalLlmProvider, LocalModel, LocalLlmOptions, LocalModelTerms, LocalChatTemplate, LocalModelTermsNotAcceptedException, LocalModelMemoryException, LocalPromptTooLongException |
| `TechieRag.Agents` (+ `.Interop`, `.DependencyInjection`; package `TechieRag.Agents`) | TechieRagAgentBuilder, ITechieRagAgent, AgentRagResponse, LlmProviderChatClient, ToolHandlerFunctions, AIToolHandler, AgentStepReporter, ConversationMemoryChatHistoryProvider, AddTechieRagAgent |
| `TechieRag.Telemetry` (package `TechieRag.Telemetry`) | AddTechieRagTelemetry, TechieRagTelemetryOptions, TechieRagTelemetrySink |

---

*This reference describes TechieRag v3 (2026-09-25): the v2 core plus the phase-2 features and the `TechieRag.Telemetry`, `TechieRag.Local` and `TechieRag.Agents` packages. The consumer-facing guide is `docs/TechieRag-UsageGuide.md`; the maintainer's map is `docs/TechieRag-DevGuide.md`.*
