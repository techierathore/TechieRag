using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Diagnostics;
using TechieRag.Models;
using TechieRag.Processors;
using TechieRag.Services;

namespace TechieRag;

/// <summary>
/// Main client for TechieRag RAG (Retrieval-Augmented Generation) operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Orchestrates document processing, embedding, and vector storage
/// to provide a complete RAG solution. This is the primary class consumers interact with
/// for all document ingestion and semantic search operations.</para>
/// <para><b>Code Flow:</b> Created via TechieRagBuilder or DI container. Coordinates
/// IDocumentProcessor instances for parsing, IEmbeddingProvider for vectorization,
/// and IVectorStore for persistence and retrieval.</para>
/// <para><b>Design:</b> All operations are async and support cancellation. The client
/// manages document lifecycle from ingestion through search and deletion.</para>
/// </remarks>
public class TechieRagClient : ITechieRag
{
    private readonly IVectorStore vectorStore;
    private readonly IEmbeddingProvider embeddingProvider;
    private readonly IReadOnlyList<IDocumentProcessor> processors;
    private readonly TechieRagConfig config;
    private readonly ILogger<TechieRagClient> logger;
    private readonly ILlmProvider? llmProvider;
    private readonly ITokenTracker tokenTracker;
    private readonly IConversationMemory? conversationMemory;
    private readonly IPromptTemplate promptTemplate;
    private readonly IReranker? reranker;
    private readonly IChunker chunker;
    private readonly IConversationStore? conversationStore;
    private readonly WorkspaceManager? workspaceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieRagClient"/> class.
    /// </summary>
    /// <param name="vectorStore">The vector store for persisting and searching embeddings.</param>
    /// <param name="embeddingProvider">The embedding provider for generating vectors from text.</param>
    /// <param name="processors">Collection of document processors for parsing various file formats.</param>
    /// <param name="config">Configuration settings for the client.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    /// <remarks>
    /// <para><b>Construction:</b> Typically created via TechieRagBuilder.Build() rather than
    /// direct instantiation. The builder handles dependency resolution and configuration.</para>
    /// </remarks>
    public TechieRagClient(
        IVectorStore vectorStore,
        IEmbeddingProvider embeddingProvider,
        IEnumerable<IDocumentProcessor> processors,
        TechieRagConfig config,
        ILogger<TechieRagClient> logger,
        ILlmProvider? llmProvider = null,
        ITokenTracker? tokenTracker = null,
        IConversationMemory? conversationMemory = null,
        IPromptTemplate? promptTemplate = null,
        IReranker? reranker = null,
        IChunker? chunker = null,
        IConversationStore? conversationStore = null,
        IWorkspaceStore? workspaceStore = null)
    {
        ArgumentNullException.ThrowIfNull(vectorStore);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        this.vectorStore = vectorStore;
        this.embeddingProvider = embeddingProvider;
        this.processors = processors.ToList();
        this.config = config;
        this.logger = logger;
        this.llmProvider = llmProvider;
        this.tokenTracker = tokenTracker ?? new TokenUsageTracker();
        this.conversationMemory = conversationMemory;
        this.promptTemplate = promptTemplate ?? new PromptTemplateEngine(config.Prompt);
        this.reranker = reranker;
        this.chunker = chunker ?? Processors.Chunking.RecursiveChunker.Instance;
        this.conversationStore = conversationStore;
        workspaceManager = workspaceStore is not null
            ? new WorkspaceManager(this, workspaceStore, this.promptTemplate, config.Prompt, logger)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Flow:</b> Initializes the vector store by creating required database tables
    /// or collections. Must be called before performing any ingestion or search operations.</para>
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initializing TechieRag client");

        // REQ-FN-049: ConfigureAwait(false) here is not cosmetic. This is the first await on the
        // initialization chain a desktop host calls at launch, and without it the continuation is
        // posted back to whatever SynchronizationContext the caller was on — the exact shape that
        // deadlocked the MAUI launch delegate when a saved config made the awaited work genuinely
        // asynchronous. The two awaits below already had it; this one was the odd one out. Library
        // code never needs the caller's context (coding standards §Best Practices).
        await vectorStore.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (conversationStore is not null)
        {
            await conversationStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (workspaceManager is not null)
        {
            await workspaceManager.GetStore().InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("TechieRag client initialized successfully");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Determine file extension and find matching processor</description></item>
    /// <item><description>Process file to extract text chunks</description></item>
    /// <item><description>Generate unique document ID</description></item>
    /// <item><description>Set document ID and metadata on all chunks</description></item>
    /// <item><description>Generate embeddings for all chunks</description></item>
    /// <item><description>Store chunks with vectors in the vector store</description></item>
    /// <item><description>Ensure document record exists with metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when no processor supports the file extension.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    public async Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var processor = FindProcessor(extension);

        if (processor == null)
        {
            if (GenericTextProcessor.IsBinaryExtension(extension))
            {
                throw new NotSupportedException(
                    $"Cannot ingest binary file with extension '{extension}'. " +
                    "Binary files like images, executables, and archives cannot be processed for text search.");
            }
            throw new NotSupportedException($"No processor available for file extension '{extension}'");
        }

        logger.LogInformation("Ingesting file {FilePath} with processor {ProcessorType}", filePath, processor.GetType().Name);

        var documentId = Guid.NewGuid().ToString();
        var fileName = Path.GetFileName(filePath);

        using var stream = File.OpenRead(filePath);

        // Read before the processor consumes the stream. This is the one number the source artefact
        // itself carries, and it is the only place in the pipeline where it is still available:
        // downstream only chunks exist, and their combined length is inflated by chunk overlap.
        var fileSizeBytes = stream.Length;

        var chunks = await processor.ProcessAsync(stream, fileName,
            new DocumentProcessingOptions
            {
                MaxChunkSize = config.Processing.DefaultChunkSize,
                ChunkOverlap = config.Processing.DefaultChunkOverlap,
                Chunker = chunker
            }, cancellationToken);

        if (chunks.Count == 0)
        {
            logger.LogWarning("No chunks extracted from file {FilePath}", filePath);
            return documentId;
        }

        // Set document ID and metadata on chunks
        var chunkList = new List<TextChunk>();
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = documentId;
            chunk.Metadata["DocumentName"] = fileName;
            chunk.Metadata["SourcePath"] = filePath;
            chunk.Metadata["FileName"] = fileName;
            chunk.Metadata[DocumentMetadataKeys.FileSize] = fileSizeBytes;
            chunkList.Add(chunk);
        }

        // Embed all chunks
        logger.LogDebug("Embedding {ChunkCount} chunks for document {DocumentId}", chunkList.Count, documentId);
        await EmbedAndStampAsync(chunkList, cancellationToken);

        // Ensure document record exists
        await EnsureDocumentExistsAsync(documentId, fileName, filePath, cancellationToken);

        // Store chunks in vector store
        logger.LogDebug("Storing {ChunkCount} chunks in vector store", chunkList.Count);
        await vectorStore.UpsertBatchAsync(chunkList, cancellationToken);

        logger.LogInformation("Successfully ingested document {DocumentId} with {ChunkCount} chunks", documentId, chunkList.Count);
        TechieRagTelemetry.RecordIngestion(chunkList.Count, "file");
        return documentId;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Generate unique document ID</description></item>
    /// <item><description>Chunk the text using TextChunker</description></item>
    /// <item><description>Create TextChunk objects with metadata</description></item>
    /// <item><description>Generate embeddings for all chunks</description></item>
    /// <item><description>Store in vector store</description></item>
    /// </list>
    /// </remarks>
    public async Task<string> IngestTextAsync(
        string text,
        string documentName,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentException.ThrowIfNullOrEmpty(documentName);

        logger.LogInformation("Ingesting text document '{DocumentName}'", documentName);

        var documentId = Guid.NewGuid().ToString();

        // For text ingestion the text IS the artefact — pasted input, a page's readable content, a
        // transcript — so its UTF-8 byte count is the document's size. A caller that knows a truer
        // number (the original file it read the text out of) overrides it through the metadata
        // argument below, which is applied after this default.
        var textSizeBytes = (long)Encoding.UTF8.GetByteCount(text);

        // Chunk the text using the configured chunking strategy
        var textChunks = TextChunker.ChunkText(
            text,
            config.Processing.DefaultChunkSize,
            config.Processing.DefaultChunkOverlap,
            chunker);

        var chunkList = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = new Dictionary<string, object>
                {
                    ["DocumentName"] = documentName,
                    ["SourcePath"] = "text-input",
                    ["FileName"] = documentName,
                    [DocumentMetadataKeys.FileSize] = textSizeBytes
                }
            };

            // Add any additional metadata
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    chunk.Metadata[kvp.Key] = kvp.Value;
                }
            }

            chunkList.Add(chunk);
        }

        if (chunkList.Count == 0)
        {
            logger.LogWarning("No chunks created from text input for document '{DocumentName}'", documentName);
            return documentId;
        }

        // Embed all chunks
        logger.LogDebug("Embedding {ChunkCount} chunks for text document {DocumentId}", chunkList.Count, documentId);
        await EmbedAndStampAsync(chunkList, cancellationToken);

        // Ensure document record exists
        await EnsureDocumentExistsAsync(documentId, documentName, "text-input", cancellationToken);

        // Store chunks in vector store
        logger.LogDebug("Storing {ChunkCount} chunks in vector store", chunkList.Count);
        await vectorStore.UpsertBatchAsync(chunkList, cancellationToken);

        logger.LogInformation("Successfully ingested text document {DocumentId} with {ChunkCount} chunks", documentId, chunkList.Count);
        TechieRagTelemetry.RecordIngestion(chunkList.Count, "text");
        return documentId;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Enumerate files in directory matching the search pattern</description></item>
    /// <item><description>Filter to only files with supported extensions</description></item>
    /// <item><description>Ingest each file individually</description></item>
    /// <item><description>Continue on individual file failures, logging errors</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">Thrown when the specified directory does not exist.</exception>
    public async Task<IReadOnlyList<string>> IngestDirectoryAsync(
        string directoryPath,
        string searchPattern = "*.*",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        logger.LogInformation("Ingesting directory {DirectoryPath} with pattern '{SearchPattern}'", directoryPath, searchPattern);

        var supportedExtensions = GetSupportedExtensions();
        var files = Directory.EnumerateFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        logger.LogInformation("Found {FileCount} supported files in directory", files.Count);

        var documentIds = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var documentId = await IngestAsync(file, cancellationToken);
                documentIds.Add(documentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to ingest file {FilePath}", file);
                // Continue with other files
            }
        }

        logger.LogInformation("Successfully ingested {SuccessCount} of {TotalCount} files from directory", documentIds.Count, files.Count);
        return documentIds;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Generate embedding vector for the query text</description></item>
    /// <item><description>Perform vector similarity search in the store</description></item>
    /// <item><description>Return ranked results with similarity scores</description></item>
    /// </list>
    /// </remarks>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default) =>
        SearchAsync(
            query,
            new SearchOptions { TopK = topK, DocumentFilter = documentFilter },
            cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Resolve the effective rerank decision: <see cref="SearchOptions.Rerank"/>
    /// when set, otherwise the library-wide <c>Rerank.Enabled</c> configuration</description></item>
    /// <item><description>Generate the embedding vector for the query text</description></item>
    /// <item><description>Fetch candidates from the vector store (oversampled when reranking)</description></item>
    /// <item><description>Apply the reranker when it is both configured and enabled for this call</description></item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        SearchOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        options ??= new SearchOptions();
        var topK = options.TopK;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var useReranker = ResolveRerank(options);
        logger.LogDebug("Searching for query with topK={TopK}, documentFilter={Filter}, rerank={Rerank}",
            topK, options.DocumentFilter ?? "(none)", useReranker);

        // REQ-RAG-036: the span and the histogram cover embedding, retrieval and rerank together,
        // because that whole sequence is what a caller experiences as "the search took N ms".
        // Both are inert unless the host opted in - see TechieRagTelemetry.
        var searchActivity = TechieRagTelemetry.StartActivity("TechieRag.Search");
        var searchTimer = Stopwatch.StartNew();
        searchActivity?.SetTag("techierag.search.top.k", topK);
        searchActivity?.SetTag("techierag.reranked", useReranker);

        using var activityScope = searchActivity;

        var queryVector = await embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var fetchCount = useReranker ? Math.Max(topK, config.Rerank.CandidateCount) : topK;
        var results = await vectorStore
            .SearchAsync(queryVector, fetchCount, options.DocumentFilter, cancellationToken)
            .ConfigureAwait(false);

        if (useReranker && results.Count > 0)
        {
            results = await ApplyRerankAsync(query, results, topK, cancellationToken).ConfigureAwait(false);
        }

        searchTimer.Stop();
        searchActivity?.SetTag("techierag.search.result.count", results.Count);
        TechieRagTelemetry.RecordSearch(searchTimer.Elapsed, results.Count, useReranker);

        logger.LogDebug("Search returned {ResultCount} results", results.Count);
        return results;
    }

    /// <summary>
    /// Decides whether the rerank stage runs for a single search call.
    /// </summary>
    /// <param name="options">The per-call search options.</param>
    /// <returns>True when a reranker is configured and reranking is enabled for this call.</returns>
    /// <remarks>
    /// <para>A null <see cref="SearchOptions.Rerank"/> falls back to <c>config.Rerank.Enabled</c>,
    /// which is exactly the behaviour of the legacy overload (REQ-RAG-047 back-compat).</para>
    /// </remarks>
    private bool ResolveRerank(SearchOptions options)
    {
        var requested = options.Rerank ?? config.Rerank.Enabled;
        if (!requested) return false;

        if (reranker is null)
        {
            logger.LogWarning("Rerank was requested for this search but no IReranker is configured; " +
                              "returning vector-similarity order. Configure one via WithReranker or TechieRag:Rerank.");
            return false;
        }

        return true;
    }

    private async Task<IReadOnlyList<SearchResult>> ApplyRerankAsync(
        string query,
        IReadOnlyList<SearchResult> candidates,
        int topK,
        CancellationToken cancellationToken)
    {
        var topN = config.Rerank.TopN > 0 ? Math.Min(config.Rerank.TopN, topK) : topK;
        logger.LogDebug("Reranking {CandidateCount} candidates to top {TopN} with {Reranker}",
            candidates.Count, topN, reranker!.Name);
        return await reranker.RerankAsync(query, candidates, topN, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Flow:</b> Delegates to the vector store's DeleteByDocumentAsync method,
    /// which removes both the document record and all associated chunks.</para>
    /// </remarks>
    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        logger.LogInformation("Deleting document {DocumentId}", documentId);
        await vectorStore.DeleteByDocumentAsync(documentId, cancellationToken);
        logger.LogInformation("Successfully deleted document {DocumentId}", documentId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Flow:</b> Delegates to the vector store's ListDocumentsAsync method
    /// to retrieve all document metadata.</para>
    /// </remarks>
    /// <inheritdoc />
    /// <remarks>Taken from the configured provider, so it always describes what THIS client writes.</remarks>
    public string EmbeddingSignature => embeddingProvider.EmbeddingSignature;

    /// <inheritdoc />
    /// <remarks>
    /// Implemented here as well as on the interface. The interface carries a default so existing
    /// implementations keep compiling, but a default member is reachable only through the interface —
    /// a caller holding a concrete <see cref="TechieRagClient"/> could not call it at all, which is
    /// how most consumers and every test hold it.
    /// </remarks>
    public async Task<EmbeddingStalenessReport> DetectStaleEmbeddingsAsync(
        CancellationToken cancellationToken = default) =>
        EmbeddingStaleness.Analyze(
            await ListDocumentsAsync(cancellationToken).ConfigureAwait(false), EmbeddingSignature);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing all documents");
        var documents = await vectorStore.ListDocumentsAsync(cancellationToken);
        logger.LogDebug("Found {DocumentCount} documents", documents.Count);
        return documents;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Flow:</b> Retrieves statistics from the vector store and enriches
    /// with embedding provider information.</para>
    /// </remarks>
    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting vector store statistics");
        var storeStats = await vectorStore.GetStatsAsync(cancellationToken);

        // Create a new stats object with embedding provider name since IngestionStats has init-only properties
        return new IngestionStats
        {
            TotalDocuments = storeStats.TotalDocuments,
            TotalChunks = storeStats.TotalChunks,
            VectorStoreSizeBytes = storeStats.VectorStoreSizeBytes,
            LastIngestionTime = storeStats.LastIngestionTime,
            VectorStoreName = storeStats.VectorStoreName,
            EmbeddingProviderName = embeddingProvider.Name
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Warning:</b> This operation is irreversible and deletes all data
    /// from the vector store.</para>
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Clearing all data from vector store");
        await vectorStore.ClearAsync(cancellationToken);
        logger.LogInformation("Successfully cleared all data from vector store");
    }

    // === NEW: LLM-Powered RAG Methods ===

    /// <inheritdoc/>
    public async Task<RagResponse> AskAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(question);

        logger.LogInformation("AskAsync: Searching for relevant context (topK={TopK})", topK);
        var searchResults = await SearchAsync(question, topK, documentFilter, cancellationToken).ConfigureAwait(false);

        var messages = promptTemplate.BuildRagPrompt(question, searchResults, systemPrompt);
        var response = await llmProvider!.ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);

        return new RagResponse
        {
            Answer = response.Content ?? string.Empty,
            Sources = searchResults,
            Usage = response.Usage,
            Query = question,
            ModelName = response.ModelName
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(question);

        var searchResults = await SearchAsync(question, topK, documentFilter, cancellationToken).ConfigureAwait(false);
        var messages = promptTemplate.BuildRagPrompt(question, searchResults, systemPrompt);

        await foreach (var token in llmProvider!.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <inheritdoc/>
    public async Task<RagResponse> ChatWithRagAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(userMessage);

        // Use conversation memory if available and no explicit history provided
        if (conversationHistory is null && conversationMemory is not null)
        {
            conversationHistory = await conversationMemory.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        }

        var searchResults = await SearchAsync(userMessage, topK, cancellationToken: cancellationToken).ConfigureAwait(false);
        var messages = promptTemplate.BuildRagChatPrompt(userMessage, searchResults, conversationHistory, systemPrompt);

        var response = await llmProvider!.ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);

        // Update conversation memory if available
        if (conversationMemory is not null)
        {
            await conversationMemory.AddMessageAsync(ChatMessage.User(userMessage), cancellationToken).ConfigureAwait(false);
            await conversationMemory.AddMessageAsync(ChatMessage.Assistant(response.Content ?? string.Empty), cancellationToken).ConfigureAwait(false);
        }

        return new RagResponse
        {
            Answer = response.Content ?? string.Empty,
            Sources = searchResults,
            Usage = response.Usage,
            Query = userMessage,
            ModelName = response.ModelName
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatWithRagStreamAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(userMessage);

        if (conversationHistory is null && conversationMemory is not null)
        {
            conversationHistory = await conversationMemory.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        }

        var searchResults = await SearchAsync(userMessage, topK, cancellationToken: cancellationToken).ConfigureAwait(false);
        var messages = promptTemplate.BuildRagChatPrompt(userMessage, searchResults, conversationHistory, systemPrompt);

        var fullResponse = new StringBuilder();

        await foreach (var token in llmProvider!.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            fullResponse.Append(token);
            yield return token;
        }

        // Update conversation memory if available
        if (conversationMemory is not null)
        {
            await conversationMemory.AddMessageAsync(ChatMessage.User(userMessage), cancellationToken).ConfigureAwait(false);
            await conversationMemory.AddMessageAsync(ChatMessage.Assistant(fullResponse.ToString()), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(
        string question,
        int topK = 5,
        string? systemPrompt = null,
        string? documentFilter = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(question);

        var searchResults = await SearchAsync(question, topK, documentFilter, cancellationToken).ConfigureAwait(false);
        yield return RagStreamEvent.FromSources(searchResults);

        var messages = promptTemplate.BuildRagPrompt(question, searchResults, systemPrompt);
        var fullAnswer = new StringBuilder();

        await foreach (var token in llmProvider!.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            fullAnswer.Append(token);
            yield return RagStreamEvent.FromToken(token);
        }

        yield return RagStreamEvent.FromCompleted(fullAnswer.ToString());
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<RagStreamEvent> ChatWithRagStreamWithSourcesAsync(
        string userMessage,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        int topK = 5,
        string? systemPrompt = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureLlmConfigured();
        ArgumentException.ThrowIfNullOrEmpty(userMessage);

        if (conversationHistory is null && conversationMemory is not null)
        {
            conversationHistory = await conversationMemory.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        }

        var searchResults = await SearchAsync(userMessage, topK, cancellationToken: cancellationToken).ConfigureAwait(false);
        yield return RagStreamEvent.FromSources(searchResults);

        var messages = promptTemplate.BuildRagChatPrompt(userMessage, searchResults, conversationHistory, systemPrompt);
        var fullAnswer = new StringBuilder();

        await foreach (var token in llmProvider!.ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            fullAnswer.Append(token);
            yield return RagStreamEvent.FromToken(token);
        }

        if (conversationMemory is not null)
        {
            await conversationMemory.AddMessageAsync(ChatMessage.User(userMessage), cancellationToken).ConfigureAwait(false);
            await conversationMemory.AddMessageAsync(ChatMessage.Assistant(fullAnswer.ToString()), cancellationToken).ConfigureAwait(false);
        }

        yield return RagStreamEvent.FromCompleted(fullAnswer.ToString());
    }

    /// <inheritdoc/>
    public ILlmProvider? GetLlmProvider() => llmProvider;

    /// <inheritdoc/>
    public ITokenTracker GetTokenTracker() => tokenTracker;

    /// <inheritdoc/>
    public IConversationMemory? GetConversationMemory() => conversationMemory;

    /// <inheritdoc/>
    public IReranker? GetReranker() => reranker;

    /// <inheritdoc/>
    public IConversationStore? GetConversationStore() => conversationStore;

    /// <inheritdoc/>
    public WorkspaceManager? GetWorkspaceManager() => workspaceManager;

    private void EnsureLlmConfigured()
    {
        if (llmProvider is null)
        {
            throw new InvalidOperationException(
                "No LLM provider configured. Configure an LLM provider using TechieRagBuilder to use this feature.");
        }
    }

    /// <summary>
    /// Finds a document processor that supports the given file extension.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns>The matching processor, or null if none found.</returns>
    /// <remarks>
    /// If no specific processor is found for the extension, falls back to GenericTextProcessor
    /// for unknown file types (unless it's a known binary extension). GenericTextProcessor
    /// will detect and reject binary content with clear error messages.
    /// </remarks>
    private IDocumentProcessor? FindProcessor(string extension)
    {
        // First, try to find a processor that explicitly supports this extension
        var processor = processors.FirstOrDefault(p =>
            p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

        if (processor != null)
            return processor;

        // If no specific processor found, check if it's a known binary extension
        if (GenericTextProcessor.IsBinaryExtension(extension))
        {
            logger.LogWarning("File extension '{Extension}' is a known binary format and cannot be processed as text", extension);
            return null;
        }

        // Use GenericTextProcessor as fallback for unknown extensions
        // It will detect and reject binary content with proper error messages
        var genericProcessor = processors.OfType<GenericTextProcessor>().FirstOrDefault();
        if (genericProcessor != null)
        {
            logger.LogInformation("Using GenericTextProcessor as fallback for unknown extension '{Extension}'", extension);
        }
        return genericProcessor;
    }

    /// <summary>
    /// Gets all file extensions supported by the registered processors.
    /// </summary>
    /// <returns>HashSet of supported extensions in lowercase.</returns>
    private HashSet<string> GetSupportedExtensions()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processor in processors)
        {
            foreach (var ext in processor.SupportedExtensions)
            {
                extensions.Add(ext.ToLowerInvariant());
            }
        }
        return extensions;
    }

    /// <summary>
    /// Ensures a document record exists in the vector store for tracking purposes.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="name">The document name.</param>
    /// <param name="sourcePath">The source path of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para><b>Note:</b> Some vector stores (like SqliteVecStore) create document records
    /// automatically during chunk upsert. This method ensures the record exists for
    /// stores that may not do so automatically.</para>
    /// </remarks>
    /// <summary>
    /// Embeds a document's chunks and stamps each one with what produced its vector (REQ-RAG-052).
    /// </summary>
    /// <param name="chunkList">The chunks to embed, mutated in place.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when every chunk carries a vector and a signature.</returns>
    /// <remarks>
    /// <b>The two steps are together on purpose.</b> Every ingestion route embeds and then stores, so
    /// a route that embedded here and stamped somewhere else would eventually gain a path that forgot
    /// the stamp — and an unstamped document is indistinguishable from a pre-2026-08-04 one. Binding
    /// the stamp to the embedding call makes "vector present, signature absent" unreachable.
    /// </remarks>
    private async Task EmbedAndStampAsync(List<TextChunk> chunkList, CancellationToken cancellationToken)
    {
        var texts = chunkList.Select(chunk => chunk.Text).ToList();
        var vectors = await embeddingProvider.EmbedBatchAsync(texts, cancellationToken);
        var signature = embeddingProvider.EmbeddingSignature;

        for (var i = 0; i < chunkList.Count; i++)
        {
            chunkList[i].Vector = vectors[i];
            chunkList[i].Metadata[DocumentMetadataKeys.EmbeddingSignature] = signature;
        }
    }

    private async Task EnsureDocumentExistsAsync(
        string documentId,
        string name,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        // Create a placeholder chunk to trigger document creation if needed
        // The actual document record is typically created/updated by UpsertBatchAsync
        // This is primarily for documentation purposes - most stores handle this internally
        logger.LogDebug("Document record will be created for {DocumentId} during chunk storage", documentId);
        await Task.CompletedTask;
    }
}
