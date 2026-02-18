using Microsoft.Extensions.Logging;

namespace TechieRag;

/// <summary>
/// Root configuration object for TechieRag library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Centralizes all configuration options for embedding, vector storage,
/// and document processing. Can be populated via fluent builder, object initializer, or appsettings.json.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder or bound from IConfiguration.
/// Passed to TechieRagClient and used to instantiate providers.</para>
/// <para><b>Dependencies:</b> None - this is a plain configuration object.</para>
/// </remarks>
public class TechieRagConfig
{
    /// <summary>
    /// Gets or sets the embedding provider configuration.
    /// </summary>
    /// <remarks>
    /// Controls which embedding provider to use (Ollama, LM Studio, ONNX, Azure OpenAI, or OpenAI)
    /// and its associated connection settings.
    /// </remarks>
    public EmbeddingConfig Embedding { get; set; } = new();

    /// <summary>
    /// Gets or sets the vector store configuration.
    /// </summary>
    /// <remarks>
    /// Controls which vector database to use (SQLite-vec, PGVector, or Qdrant)
    /// and its connection string.
    /// </remarks>
    public VectorStoreConfig VectorStore { get; set; } = new();

    /// <summary>
    /// Gets or sets the document processing configuration.
    /// </summary>
    /// <remarks>
    /// Controls chunking parameters including default chunk size and overlap
    /// for document ingestion.
    /// </remarks>
    public ProcessingConfig Processing { get; set; } = new();

    /// <summary>
    /// Gets or sets whether telemetry (logging, metrics) is enabled.
    /// </summary>
    /// <remarks>
    /// When enabled, TechieRag will emit telemetry data for monitoring
    /// embedding operations, vector store queries, and ingestion statistics.
    /// </remarks>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>Gets or sets the LLM provider configuration.</summary>
    public LlmConfig Llm { get; set; } = new();

    /// <summary>Gets or sets the LLM fallback provider configuration (optional).</summary>
    public LlmConfig? LlmFallback { get; set; }

    /// <summary>Gets or sets the usage tracking configuration.</summary>
    public UsageTrackingConfig UsageTracking { get; set; } = new();

    /// <summary>Gets or sets the prompt template configuration.</summary>
    public PromptConfig Prompt { get; set; } = new();

    /// <summary>Gets or sets the resilience/retry configuration.</summary>
    public ResilienceConfig Resilience { get; set; } = new();

    /// <summary>
    /// Internal logger factory set by the builder or DI container.
    /// </summary>
    /// <remarks>
    /// This property is set internally by TechieRagBuilder.WithLogging()
    /// or by the DI container during service resolution.
    /// </remarks>
    internal ILoggerFactory? LoggerFactory { get; set; }
}

/// <summary>
/// Configuration for embedding provider selection and settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines all settings needed to connect to and use
/// an embedding provider for generating vector representations of text.</para>
/// <para><b>Code Flow:</b> Read by TechieRagBuilder to instantiate the appropriate
/// IEmbeddingProvider implementation.</para>
/// </remarks>
public class EmbeddingConfig
{
    /// <summary>
    /// Gets or sets the embedding source type.
    /// </summary>
    /// <remarks>
    /// Determines which embedding provider implementation will be used.
    /// Default is Ollama for easy local development.
    /// </remarks>
    public EmbeddingSource Source { get; set; } = EmbeddingSource.Ollama;

    /// <summary>
    /// Gets or sets the API endpoint URL.
    /// </summary>
    /// <remarks>
    /// Required for Ollama, LM Studio, Azure OpenAI, and OpenAI sources.
    /// For Ollama: typically http://localhost:11434
    /// For LM Studio: typically http://localhost:1234
    /// For Azure OpenAI: your deployment endpoint URL
    /// For OpenAI: https://api.openai.com
    /// </remarks>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the API key for authentication.
    /// </summary>
    /// <remarks>
    /// Required for Azure OpenAI and OpenAI sources.
    /// Not needed for local providers (Ollama, LM Studio, ONNX).
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the embedding model name.
    /// </summary>
    /// <remarks>
    /// Default is "bge-m3" which produces 1024-dimensional vectors.
    /// Other common models include "text-embedding-3-small" for OpenAI.
    /// </remarks>
    public string Model { get; set; } = "bge-m3";

    /// <summary>
    /// Gets or sets the local model file path.
    /// </summary>
    /// <remarks>
    /// Required only for ONNX source.
    /// Should point to the directory containing the ONNX model files.
    /// </remarks>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Gets or sets the API format for HTTP embedding providers.
    /// </summary>
    /// <remarks>
    /// Only used when Source is Http.
    /// Default is OpenAI format which is widely supported.
    /// </remarks>
    public HttpApiFormat ApiFormat { get; set; } = HttpApiFormat.OpenAI;

    /// <summary>
    /// Gets or sets the custom API path for HTTP embedding providers.
    /// </summary>
    /// <remarks>
    /// Only used when Source is Http.
    /// Default paths per format:
    /// - OpenAI: /v1/embeddings
    /// - Ollama: /api/embeddings
    /// - Simple: /embed
    /// Set this to override the default path.
    /// </remarks>
    public string? ApiPath { get; set; }

    /// <summary>
    /// Gets or sets the vector dimensions for the embedding model.
    /// </summary>
    /// <remarks>
    /// Default is 1024 for BGE-M3 model.
    /// Set this when using models with different dimensions.
    /// </remarks>
    public int Dimensions { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the delay between HTTP embedding requests in milliseconds.
    /// </summary>
    /// <remarks>
    /// Default is 200ms. Helps prevent overwhelming HTTP embedding servers.
    /// Set to 0 to disable delay between requests.
    /// Increase if the embedding server is getting overloaded.
    /// </remarks>
    public int RequestDelayMs { get; set; } = 200;
}

/// <summary>
/// Configuration for vector store selection and connection.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines all settings needed to connect to and use
/// a vector database for storing and searching embeddings.</para>
/// <para><b>Code Flow:</b> Read by TechieRagBuilder to instantiate the appropriate
/// IVectorStore implementation.</para>
/// </remarks>
public class VectorStoreConfig
{
    /// <summary>
    /// Gets or sets the vector store type.
    /// </summary>
    /// <remarks>
    /// Determines which vector store implementation will be used.
    /// Default is SqliteVec for zero-configuration local development.
    /// </remarks>
    public VectorStoreType Type { get; set; } = VectorStoreType.SqliteVec;

    /// <summary>
    /// Gets or sets the connection string or endpoint URL for the vector store.
    /// </summary>
    /// <remarks>
    /// For SqliteVec: SQLite connection string (e.g., "Data Source=techierag.db")
    /// For PgVector: PostgreSQL connection string
    /// For Qdrant: HTTP endpoint URL (e.g., "http://localhost:6334")
    /// </remarks>
    public string ConnectionString { get; set; } = "Data Source=techierag.db";

    /// <summary>
    /// Gets or sets the API key for vector store authentication.
    /// </summary>
    /// <remarks>
    /// Required for Qdrant when API key authentication is enabled.
    /// Not needed for SqliteVec or PgVector (they use connection string auth).
    /// </remarks>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Configuration for document processing and chunking.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Controls how documents are split into chunks for embedding.
/// Proper chunking is essential for effective semantic search.</para>
/// <para><b>Code Flow:</b> Read by document processors during ingestion to determine
/// how to split document content into manageable chunks.</para>
/// </remarks>
public class ProcessingConfig
{
    /// <summary>
    /// Gets or sets the default chunk size in characters.
    /// </summary>
    /// <remarks>
    /// Larger chunks preserve more context but may reduce retrieval precision.
    /// Smaller chunks improve precision but may lose important context.
    /// Default of 500 characters provides a good balance for most use cases.
    /// </remarks>
    public int DefaultChunkSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the default overlap between chunks in characters.
    /// </summary>
    /// <remarks>
    /// Overlap helps maintain context across chunk boundaries.
    /// This ensures important information at chunk boundaries is captured
    /// in multiple chunks for better retrieval.
    /// </remarks>
    public int DefaultChunkOverlap { get; set; } = 50;
}

/// <summary>
/// Supported embedding provider sources.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enumerates all supported embedding providers,
/// enabling type-safe configuration of the embedding source.</para>
/// </remarks>
public enum EmbeddingSource
{
    /// <summary>
    /// Local ONNX model inference.
    /// </summary>
    /// <remarks>
    /// Runs entirely locally without network calls.
    /// Requires the ONNX model files to be available on disk.
    /// </remarks>
    Onnx,

    /// <summary>
    /// Embedded BGE-M3 model with auto-download.
    /// </summary>
    /// <remarks>
    /// Uses the TechieRag.Embedded package which automatically downloads
    /// the BGE-M3 model (~2.3GB) on first use. No configuration needed.
    /// </remarks>
    Embedded,

    /// <summary>
    /// Ollama local model server.
    /// </summary>
    /// <remarks>
    /// Connects to a locally running Ollama instance.
    /// Default endpoint is http://localhost:11434.
    /// </remarks>
    Ollama,

    /// <summary>
    /// LM Studio local model server.
    /// </summary>
    /// <remarks>
    /// Connects to a locally running LM Studio server.
    /// Default endpoint is http://localhost:1234.
    /// </remarks>
    LmStudio,

    /// <summary>
    /// Azure OpenAI cloud service.
    /// </summary>
    /// <remarks>
    /// Connects to Azure OpenAI for cloud-based embeddings.
    /// Requires endpoint and API key configuration.
    /// </remarks>
    AzureOpenAI,

    /// <summary>
    /// OpenAI cloud service.
    /// </summary>
    /// <remarks>
    /// Connects to OpenAI API for cloud-based embeddings.
    /// Requires API key configuration.
    /// </remarks>
    OpenAI,

    /// <summary>
    /// Generic HTTP embedding service.
    /// </summary>
    /// <remarks>
    /// Connects to any HTTP-based embedding service (ONNX containers, custom deployments, etc.).
    /// Supports OpenAI-compatible and Ollama-compatible API formats.
    /// Configure endpoint, API path, and format via EmbeddingConfig.
    /// </remarks>
    Http
}

/// <summary>
/// API format for HTTP embedding providers.
/// </summary>
/// <remarks>
/// Different embedding services use different API formats.
/// This enum allows selecting the appropriate format for your deployment.
/// </remarks>
public enum HttpApiFormat
{
    /// <summary>
    /// OpenAI-compatible format.
    /// </summary>
    /// <remarks>
    /// POST to /v1/embeddings with { "input": "...", "model": "..." }
    /// Returns { "data": [{ "embedding": [...] }] }
    /// Used by: LM Studio, vLLM, text-embeddings-inference, many ONNX servers
    /// </remarks>
    OpenAI,

    /// <summary>
    /// Ollama-compatible format.
    /// </summary>
    /// <remarks>
    /// POST to /api/embeddings with { "prompt": "...", "model": "..." }
    /// Returns { "embedding": [...] }
    /// Used by: Ollama
    /// </remarks>
    Ollama,

    /// <summary>
    /// Simple JSON array format.
    /// </summary>
    /// <remarks>
    /// POST to custom path with { "text": "..." } or { "texts": [...] }
    /// Returns { "embedding": [...] } or { "embeddings": [[...], [...]] }
    /// Used by: Simple custom ONNX deployments
    /// </remarks>
    Simple
}

/// <summary>
/// Supported vector database types.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enumerates all supported vector stores,
/// enabling type-safe configuration of the vector database.</para>
/// </remarks>
public enum VectorStoreType
{
    /// <summary>
    /// SQLite with sqlite-vec extension (embedded).
    /// </summary>
    /// <remarks>
    /// Zero-configuration embedded database perfect for local development
    /// and small to medium scale deployments.
    /// </remarks>
    SqliteVec,

    /// <summary>
    /// PostgreSQL with pgvector extension.
    /// </summary>
    /// <remarks>
    /// Production-ready option for PostgreSQL environments.
    /// Requires pgvector extension to be installed on the database server.
    /// </remarks>
    PgVector,

    /// <summary>
    /// Qdrant vector database.
    /// </summary>
    /// <remarks>
    /// High-performance dedicated vector database.
    /// Can be run locally via Docker or as a managed cloud service.
    /// </remarks>
    Qdrant
}

/// <summary>Supported LLM provider sources.</summary>
public enum LlmSource
{
    /// <summary>No LLM configured (embedding/retrieval only mode).</summary>
    None,
    /// <summary>Ollama local model server.</summary>
    Ollama,
    /// <summary>LM Studio local model server.</summary>
    LmStudio,
    /// <summary>OpenAI-compatible REST API.</summary>
    OpenAICompatible,
    /// <summary>Azure AI Foundry (formerly Azure OpenAI).</summary>
    AzureAIFoundry,
    /// <summary>Google Gemini API.</summary>
    GoogleGemini,
    /// <summary>Anthropic Claude API.</summary>
    Anthropic
}

/// <summary>Configuration for LLM provider selection and settings.</summary>
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

/// <summary>Configuration for token usage tracking and budgets.</summary>
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

/// <summary>Configuration for prompt templates used in RAG operations.</summary>
public class PromptConfig
{
    /// <summary>Gets or sets the default system prompt for RAG operations.</summary>
    public string SystemPrompt { get; set; } =
        "You are a helpful assistant. Answer the user's question based on the provided context. " +
        "If the context doesn't contain relevant information, say so. " +
        "Cite the source documents when possible.";

    /// <summary>Gets or sets the template for formatting context chunks.</summary>
    public string ContextChunkTemplate { get; set; } =
        "[Source {index}: {source} (relevance: {score:P0})]\n{text}";

    /// <summary>Gets or sets the maximum number of context chunks to include.</summary>
    public int MaxContextChunks { get; set; } = 5;

    /// <summary>Gets or sets the maximum tokens to allocate for context.</summary>
    public int MaxContextTokens { get; set; } = 4000;
}

/// <summary>Configuration for retry and resilience behavior.</summary>
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

    /// <summary>Gets or sets whether to automatically handle rate limiting.</summary>
    public bool HandleRateLimiting { get; set; } = true;

    /// <summary>Gets or sets the circuit breaker failure threshold.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Gets or sets the circuit breaker recovery time in seconds.</summary>
    public int CircuitBreakerRecoverySeconds { get; set; } = 30;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
