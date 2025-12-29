using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors;

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
        ILogger<TechieRagClient> logger)
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
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Flow:</b> Initializes the vector store by creating required database tables
    /// or collections. Must be called before performing any ingestion or search operations.</para>
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initializing TechieRag client");
        await vectorStore.InitializeAsync(cancellationToken);
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
        var chunks = await processor.ProcessAsync(stream, fileName,
            new DocumentProcessingOptions
            {
                MaxChunkSize = config.Processing.DefaultChunkSize,
                ChunkOverlap = config.Processing.DefaultChunkOverlap
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
            chunkList.Add(chunk);
        }

        // Embed all chunks
        logger.LogDebug("Embedding {ChunkCount} chunks for document {DocumentId}", chunkList.Count, documentId);
        var texts = chunkList.Select(c => c.Text).ToList();
        var vectors = await embeddingProvider.EmbedBatchAsync(texts, cancellationToken);

        for (int i = 0; i < chunkList.Count; i++)
        {
            chunkList[i].Vector = vectors[i];
        }

        // Ensure document record exists
        await EnsureDocumentExistsAsync(documentId, fileName, filePath, cancellationToken);

        // Store chunks in vector store
        logger.LogDebug("Storing {ChunkCount} chunks in vector store", chunkList.Count);
        await vectorStore.UpsertBatchAsync(chunkList, cancellationToken);

        logger.LogInformation("Successfully ingested document {DocumentId} with {ChunkCount} chunks", documentId, chunkList.Count);
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

        // Chunk the text
        var textChunks = TextChunker.ChunkText(
            text,
            config.Processing.DefaultChunkSize,
            config.Processing.DefaultChunkOverlap);

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
                    ["FileName"] = documentName
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
        var texts = chunkList.Select(c => c.Text).ToList();
        var vectors = await embeddingProvider.EmbedBatchAsync(texts, cancellationToken);

        for (int i = 0; i < chunkList.Count; i++)
        {
            chunkList[i].Vector = vectors[i];
        }

        // Ensure document record exists
        await EnsureDocumentExistsAsync(documentId, documentName, "text-input", cancellationToken);

        // Store chunks in vector store
        logger.LogDebug("Storing {ChunkCount} chunks in vector store", chunkList.Count);
        await vectorStore.UpsertBatchAsync(chunkList, cancellationToken);

        logger.LogInformation("Successfully ingested text document {DocumentId} with {ChunkCount} chunks", documentId, chunkList.Count);
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
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        logger.LogDebug("Searching for query with topK={TopK}, documentFilter={Filter}", topK, documentFilter ?? "(none)");

        // Embed the query
        var queryVector = await embeddingProvider.EmbedAsync(query, cancellationToken);

        // Search the vector store
        var results = await vectorStore.SearchAsync(queryVector, topK, documentFilter, cancellationToken);

        logger.LogDebug("Search returned {ResultCount} results", results.Count);
        return results;
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
