using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag;

/// <summary>
/// Main interface for TechieRag RAG (Retrieval-Augmented Generation) operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines the contract for all RAG operations including document ingestion,
/// semantic search, and document management. This is the primary interface consumers interact with.</para>
/// <para><b>Code Flow:</b> Implemented by TechieRagClient. Created via TechieRagBuilder or DI container.
/// Applications call these methods to ingest documents and perform semantic searches.</para>
/// <para><b>Design:</b> All operations are async and support cancellation for responsive applications.</para>
/// </remarks>
public interface ITechieRag
{
    /// <summary>
    /// Ingests a single file into the vector store.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to ingest.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The generated document ID for the ingested file.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> File is read, processed by appropriate IDocumentProcessor,
    /// chunked, embedded via IEmbeddingProvider, and stored in IVectorStore.</para>
    /// </remarks>
    Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests raw text content as a document.
    /// </summary>
    /// <param name="text">The text content to ingest.</param>
    /// <param name="documentName">A friendly name for the document.</param>
    /// <param name="metadata">Optional metadata to associate with the document.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The generated document ID.</returns>
    Task<string> IngestTextAsync(string text, string documentName, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests all matching files from a directory.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing documents.</param>
    /// <param name="searchPattern">File pattern to match (e.g., "*.pdf", "*.*").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of document IDs for all successfully ingested files.</returns>
    Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*", CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs semantic search across all ingested documents.
    /// </summary>
    /// <param name="query">The natural language query to search for.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked list of search results with relevance scores.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> Query is embedded, vector similarity search is performed,
    /// and results are ranked by relevance score (higher is better).</para>
    /// </remarks>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document and all its chunks from the vector store.
    /// </summary>
    /// <param name="documentId">The ID of the document to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all documents currently in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of all ingested documents with metadata.</returns>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves statistics about the current vector store state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including document count, chunk count, and storage size.</returns>
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all documents and chunks from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// <para><b>Warning:</b> This operation is irreversible and deletes all data.</para>
    /// </remarks>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the vector store and validates configuration.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// <para><b>Flow:</b> Creates database tables/collections if needed,
    /// validates embedding provider connectivity, and prepares for operations.</para>
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

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
}
