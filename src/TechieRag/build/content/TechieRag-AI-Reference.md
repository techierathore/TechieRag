# TechieRag v3 - AI Agent Reference Guide

Updated 2026-09-25 for the phase-2 packages (`TechieRag.Telemetry`, `TechieRag.Local`, `TechieRag.Agents`) and the phase-2 features of the core. Every signature below is copied from the source; the section "Phase-2 Features (v3)" gives one call example per feature.

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
- **Connectors and Web** - GitHub/GitLab repositories, IMAP/mbox mail, Confluence; URL and site ingestion behind an SSRF guard
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
| `UseSqliteVec(connectionString)` | SQLite with vec extension (default: "Data Source=techierag.db") |
| `UsePgVector(connectionString)` | PostgreSQL with pgvector extension |
| `UseQdrant(endpoint, apiKey)` | Qdrant vector database |

### LLM Providers

| Method | Description |
|--------|-------------|
| `UseOllamaLlm(endpoint, model)` | Ollama (default: localhost:11434, llama3.2) |
| `UseLmStudioLlm(endpoint, model)` | LM Studio (default: localhost:1234) |
| `UseOpenAICompatibleLlm(endpoint, apiKey, model)` | OpenAI-compatible REST API |
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
public const string CodeNotPermitted = "SubscriptionNotPermitted";

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
public static ILlmProvider CreateForModel(string modelName, string? apiKey, ILoggerFactory? loggerFactory = null, int maxTokens = 2048);

// TechieRag.Llm.LlmConnectorCatalog
public static IReadOnlyList<LlmConnectorDescriptor> All { get; }
public static LlmConnectorDescriptor? Find(string? name);
public static LlmConnectorDescriptor Require(string name);
public sealed record LlmConnectorDescriptor { string Name; string DisplayName; LlmSource Source; string? Endpoint; IReadOnlyList<string> ModelPrefixes; string? DefaultModel; bool RequiresApiKey = true; SubscriptionTerms? Subscription; }

// TechieRag.LlmConfig - configuration can name the connector instead of pasting its URL
public string? Connector { get; set; }   // e.g. "groq"; an explicit Endpoint still wins
```

```csharp
var rag = new TechieRagBuilder()
    .UseOllama()
    .UseSqliteVec()
    .UseLlmForModel("claude-sonnet-4-5", apiKey: config["Anthropic:ApiKey"])   // routes to Anthropic
    .Build();

var route = ModelRouter.Require("groq/llama-3.3-70b-versatile");   // Connector.Name "groq", Source OpenAICompatible
var gemini = LlmProviderFactory.CreateForModel("gemini-2.0-flash", config["Google:ApiKey"]);
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
    // .WithReranker(RerankSource.Cohere, apiKey: config["Cohere:ApiKey"])   // or an API reranker
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

### 12. Configuration: `AddTechieRag(IConfiguration)` maps every field

`AddTechieRag(IConfiguration)` and `AddTechieRag(TechieRagConfig)` go through one mapper, so every bound field reaches the builder: `VectorStore.ApiKey` (Qdrant with a key), the embedding `Dimensions`, `ApiFormat`, `ApiPath` and `RequestDelayMs`, the whole `Prompt` section, `Rerank`, `Persistence`, `Resilience`, `UsageTracking`, `LlmFallback`, `EnableTelemetry` and `Llm.Connector`. Pass the `TechieRag` section, not the root configuration.

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
      "ApiKey": "sk-...",
      "Model": "gpt-4o",
      "Temperature": 0.7,
      "MaxTokens": 2048,
      "MaxContextTokens": 128000
    },
    "LlmFallback": {
      "Source": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.2"
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
      "ApiKey": "...",
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

v3 notes: `VectorStore.ApiKey` (Qdrant), `Embedding.Dimensions`, the `Prompt` section, `Rerank` and `Persistence` are all bound and applied. `Llm.Connector` names a catalog connector (`"groq"`) instead of pasting its URL. `Llm.Source: "Local"` needs `LocalLlm.Register()` at startup; `Llm.Source: "Subscription"` cannot be configured from appsettings alone (see Phase-2 Features, 12).

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
| `TechieRag.Connectors` (+ `.Repository`, `.Email`, `.Http`) | ConnectorRunner, IngestConnectorAsync, ConnectorRunOptions, ConnectorSyncState, ConnectorErrorCodes, RepositoryConnector, EmailConnector, ImapMailTransport, MboxMailTransport, HttpConnectorTransport |
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
