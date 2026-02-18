# TechieRag v2 - AI Agent Reference Guide

## Overview

TechieRag is a complete RAG (Retrieval-Augmented Generation) + LLM management platform for .NET. It provides:
- **Embeddings** - Generate vector embeddings from text (6 providers)
- **Vector Storage** - Store and search vectors (3 backends)
- **Document Processing** - Ingest PDFs, Markdown, text, HTML, etc. (9 processors)
- **LLM Completions** - Chat, streaming, structured output (6 providers)
- **Tool/Function Calling** - Full agent loop with tool execution
- **Token Management** - Usage tracking, cost estimation, budgets
- **Conversation Memory** - Multi-turn conversation history management
- **Resilience** - Retry, fallback, circuit breaker

## Package Information

### NuGet Source

Packages are hosted on **GitHub Packages**:
- Source URL: `https://nuget.pkg.github.com/techierathore/index.json`
- Username: GitHub username
- Authentication: GitHub PAT with `read:packages` scope

### Available Packages

| Package | Purpose |
|---------|---------|
| `TechieRag` | Core library - embeddings, vector stores, document processing, LLM providers, all services |
| `TechieRag.Embedded` | ONNX-based embedded embedding provider (no external API needed) |

### NuGet Configuration

If the project has an existing `nuget.config`, add the TechieRag source to it. Otherwise create:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="TechieRag" value="https://nuget.pkg.github.com/techierathore/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <TechieRag>
      <add key="Username" value="GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="GITHUB_PAT_WITH_READ_PACKAGES" />
    </TechieRag>
  </packageSourceCredentials>
</configuration>
```

### Install Commands

```bash
dotnet add package TechieRag
# Optional: for embedded/offline embeddings (no Ollama/API needed)
dotnet add package TechieRag.Embedded
```

---

## Architecture

```
YOUR APPLICATION
    |
    v
ITechieRag (Public API - all RAG + LLM methods)
    |
    +-- IEmbeddingProvider (embeddings)
    +-- IVectorStore (vector storage)
    +-- IDocumentProcessor[] (document ingestion)
    +-- ILlmProvider (LLM completions, chat, streaming, tools)
    +-- ITokenTracker (usage tracking)
    +-- IConversationMemory (conversation history)
    +-- IPromptTemplate (RAG prompt construction)
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

### Supporting Features

| Method | Description |
|--------|-------------|
| `WithFallbackLlm(configure)` | Configure fallback LLM provider |
| `WithUsageTracking(configure?)` | Enable token usage tracking and budgets |
| `WithConversationMemory()` | Enable conversation history management |
| `WithPromptTemplate(systemPrompt?, contextTemplate?)` | Customize RAG prompt templates |
| `WithCustomPromptTemplate(factory)` | Custom IPromptTemplate implementation |
| `WithResilience(configure?)` | Configure retry, timeout, circuit breaker |
| `WithToolHandler(handler)` | Register IToolHandler for tool calling |
| `WithTools(configure)` | Register tools with delegate-based handlers |

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
    }
  }
}
```

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

---

## Dependency Injection (ASP.NET Core)

### Option A: Using appsettings.json

```csharp
// Program.cs
builder.Services.AddTechieRag(builder.Configuration);
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
| `TechieRag` | ITechieRag, TechieRagClient, TechieRagBuilder, TechieRagConfig |
| `TechieRag.Abstractions` | ILlmProvider, IToolHandler, IConversationMemory, ITokenTracker, IPromptTemplate, IVectorStore, IEmbeddingProvider |
| `TechieRag.Models` | ChatMessage, LlmResponse, RagResponse, LlmCompletionOptions, ToolDefinition, ToolCall, ToolResult, TokenUsage, UsageBudget, SearchResult |
| `TechieRag.Llm` | OllamaLlmProvider, LmStudioLlmProvider, OpenAICompatibleLlmProvider, AzureAIFoundryLlmProvider, GoogleGeminiLlmProvider, AnthropicLlmProvider |
| `TechieRag.Services` | TokenUsageTracker, InMemoryConversationMemory, AgentLoopRunner, ToolRegistry, PromptTemplateEngine, RetryHandler, FallbackLlmHandler |
| `TechieRag.DependencyInjection` | ServiceCollectionExtensions (AddTechieRag) |

---

*This reference was generated for TechieRag v2.0. For the full implementation specification, see `docs/techierag-v2-llm-implementation-spec.md`.*
