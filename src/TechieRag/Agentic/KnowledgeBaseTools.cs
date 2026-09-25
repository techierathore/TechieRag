using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechieRag.Models;

namespace TechieRag.Agentic;

/// <summary>
/// The agentic retrieval contract: the <c>search_knowledge_base</c> and <c>list_documents</c> tools,
/// their model-facing descriptions and JSON schemas, and the structured result every agent loop sees
/// (REQ-RAG-015 / BRD-83).
/// </summary>
/// <remarks>
/// <para>Plain types, zero packages: the classic loop binds them through
/// <see cref="ToolRegistryKnowledgeBaseExtensions.RegisterKnowledgeBase"/>, and <c>TechieRag.Agents</c>
/// binds the same methods to Microsoft Agent Framework, so both loops answer the same way.</para>
/// <para>Every string here is read by a model and is deliberately invariant English (REQ-RAG-050
/// policy). The result reports match quality as <c>strong</c>, <c>weak</c>, <c>none</c> or
/// <c>limit_reached</c> with a next-step hint; the instructions in <see cref="AgenticInstructions"/>
/// refer to those words literally so the prompt and the tool reinforce each other.</para>
/// </remarks>
public static class KnowledgeBaseTools
{
    /// <summary>The default search tool name.</summary>
    public const string SearchToolName = "search_knowledge_base";

    /// <summary>The default list tool name.</summary>
    public const string ListToolName = "list_documents";

    /// <summary>Status: the best passage cleared the strong threshold.</summary>
    public const string StatusStrong = "strong";

    /// <summary>Status: passages were found but none cleared the strong threshold.</summary>
    public const string StatusWeak = "weak";

    /// <summary>Status: nothing relevant was found.</summary>
    public const string StatusNone = "none";

    /// <summary>Status: the per-turn search budget is spent; no search was run.</summary>
    public const string StatusLimitReached = "limit_reached";

    /// <summary>The search tool's model-facing description.</summary>
    public const string SearchDescription =
        "Search the user's ingested documents for passages relevant to a query. This is the only source of facts about those documents; "
        + "never answer questions about their contents from memory. Returns the best-matching passages, each with a citation ref (S1, S2, ...), "
        + "source document, page, relevance score from 0 to 1, and text. The result also reports match quality as \"strong\", \"weak\", \"none\", "
        + "or \"limit_reached\". If quality is \"weak\" or \"none\", do not answer yet: call this tool again with a different query. Rephrase using "
        + "other words the document would use, split a compound question into one concept per call, or restrict to one document using an id from "
        + "list_documents. Write the query as the words likely to appear in the relevant passage (3 to 12 words), not as a sentence or a question, "
        + "and never include instructions to yourself in it.";

    /// <summary>The search tool's JSON schema, with the parameter descriptions the model reads.</summary>
    public const string SearchParametersSchema = """
        {"type":"object","properties":{"query":{"type":"string","description":"Words likely to appear in the passage you need. One concept per call. Not the user's whole message."},"top_k":{"type":"integer","minimum":1,"maximum":20,"description":"How many passages to return, 1 to 20. Default 5. Use more for broad questions, fewer for a precise fact."},"document_id":{"type":"string","description":"Restrict the search to one document. Use an id from list_documents. Omit to search everything."}},"required":["query"]}
        """;

    /// <summary>The list tool's model-facing description.</summary>
    public const string ListDescription =
        "List the documents currently in the knowledge base, with id, name, and how many passages each contains. Call this when the user names "
        + "or asks about a specific document, asks what is available, or when search_knowledge_base returned weak results and restricting to one "
        + "document might help. Do not call it before every search.";

    /// <summary>The list tool's JSON schema: no parameters.</summary>
    public const string ListParametersSchema = """{"type":"object","properties":{}}""";

    /// <summary>Builds the search tool definition for an <c>IToolHandler</c> or <c>ToolRegistry</c>.</summary>
    /// <param name="options">The tool options; null uses the defaults.</param>
    /// <returns>The definition.</returns>
    public static ToolDefinition SearchDefinition(RetrievalToolOptions? options = null) => new()
    {
        Name = (options ?? new RetrievalToolOptions()).ToolName,
        Description = SearchDescription,
        ParametersSchema = SearchParametersSchema
    };

    /// <summary>Builds the list tool definition.</summary>
    /// <param name="options">The tool options; null uses the defaults.</param>
    /// <returns>The definition.</returns>
    public static ToolDefinition ListDefinition(RetrievalToolOptions? options = null) => new()
    {
        Name = (options ?? new RetrievalToolOptions()).ListToolName,
        Description = ListDescription,
        ParametersSchema = ListParametersSchema
    };

    /// <summary>Runs one search call and returns the structured JSON result the model reads.</summary>
    /// <param name="source">What to search.</param>
    /// <param name="options">Thresholds, budget and limits.</param>
    /// <param name="state">The conversation's retrieval state (budget, refs, collected results).</param>
    /// <param name="argumentsJson">The model's arguments: <c>query</c>, optional <c>top_k</c> and <c>document_id</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON with <c>status</c>, <c>best_score</c>, <c>searches_used</c>, <c>searches_remaining</c>, <c>results</c> and <c>hint</c>.</returns>
    /// <exception cref="ArgumentException">The arguments are not a JSON object with a non-empty <c>query</c>.</exception>
    public static async Task<string> ExecuteSearchAsync(
        IRetrievalSource source,
        RetrievalToolOptions options,
        RetrievalTurnState state,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);

        var arguments = SearchArguments.Parse(argumentsJson, options);
        var used = state.TryUseSearch(options.MaxSearchesPerTurn);
        if (used is null)
        {
            state.Record(new RetrievalTrace(arguments.Query, arguments.TopK, arguments.DocumentId, StatusLimitReached, null, [], TimeSpan.Zero));
            return BuildResult(StatusLimitReached, null, options.MaxSearchesPerTurn, options, [], state);
        }

        var watch = Stopwatch.StartNew();
        var results = await source.SearchAsync(arguments.Query, arguments.TopK, arguments.DocumentId, cancellationToken).ConfigureAwait(false);
        watch.Stop();

        var best = results.Count > 0 ? results.Max(r => r.Score) : (float?)null;
        var status = Classify(best, options);
        var refs = results.Select(state.Collect).ToList();
        state.Record(new RetrievalTrace(arguments.Query, arguments.TopK, arguments.DocumentId, status, best, refs, watch.Elapsed));

        return BuildResult(status, best, used.Value, options, results.Zip(refs).ToList(), state);
    }

    /// <summary>Runs one list call and returns the JSON document list the model reads.</summary>
    /// <param name="source">What to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON: <c>{"documents":[{"id","name","passages"}]}</c>.</returns>
    public static async Task<string> ExecuteListAsync(IRetrievalSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var documents = await source.ListDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var list = new JsonArray();
        foreach (var document in documents)
        {
            list.Add(new JsonObject
            {
                ["id"] = document.Id,
                ["name"] = document.Name,
                ["passages"] = document.ChunkCount
            });
        }

        return new JsonObject { ["documents"] = list }.ToJsonString();
    }

    /// <summary>Classifies a search by its best score.</summary>
    /// <param name="bestScore">The best score, or null for no results.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns>The status word.</returns>
    public static string Classify(float? bestScore, RetrievalToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (bestScore is not { } best) return StatusNone;

        if (options.Rerank)
        {
            // A reranker's scale is its own; the cosine thresholds mean nothing on it.
            return options.RerankWeakThreshold is { } rerankThreshold && best < rerankThreshold ? StatusWeak : StatusStrong;
        }

        if (best >= options.WeakScoreThreshold) return StatusStrong;
        return best >= options.NoneScoreThreshold ? StatusWeak : StatusNone;
    }

    private static string BuildResult(
        string status,
        float? best,
        int used,
        RetrievalToolOptions options,
        IReadOnlyList<(SearchResult Result, string Ref)> results,
        RetrievalTurnState state)
    {
        var json = new JsonObject { ["status"] = status };
        if (options.IncludeScores)
        {
            json["best_score"] = best is { } score ? Math.Round(score, 2) : null;
        }

        json["searches_used"] = used;
        json["searches_remaining"] = Math.Max(0, options.MaxSearchesPerTurn - used);
        json["results"] = BuildResults(results, options);
        json["hint"] = Hint(status, options);
        return json.ToJsonString();
    }

    private static JsonArray BuildResults(IReadOnlyList<(SearchResult Result, string Ref)> results, RetrievalToolOptions options)
    {
        var array = new JsonArray();
        foreach (var (result, reference) in results)
        {
            var chunk = result.Chunk;
            var item = new JsonObject
            {
                ["ref"] = reference,
                ["document"] = DocumentName(chunk),
                ["document_id"] = chunk.DocumentId,
                ["page"] = chunk.PageNumber,
                ["chunk_index"] = chunk.ChunkIndex
            };
            if (options.IncludeScores)
            {
                item["score"] = Math.Round(result.Score, 2);
            }

            item["text"] = Truncate(chunk.Text, options.MaxChunkChars);
            array.Add(item);
        }

        return array;
    }

    private static string Hint(string status, RetrievalToolOptions options) => status switch
    {
        StatusStrong => "Answer from these passages and cite them by ref. Search again only if the question has a part these passages do not cover.",
        StatusWeak => "No passage scored above " + WeakThreshold(options).ToString("0.##", CultureInfo.InvariantCulture)
            + ". Search again with different terminology, or narrow to a single document from list_documents. Do not answer from memory.",
        StatusLimitReached => "You have used all " + options.MaxSearchesPerTurn.ToString(CultureInfo.InvariantCulture)
            + " searches for this turn. Answer now from the passages already retrieved, and state clearly which parts of the question you could not find support for.",
        _ => "Nothing relevant was found. Try one more query using different words. If that also finds nothing, tell the user the documents do not appear to cover this and say what you searched for."
    };

    /// <summary>
    /// The threshold <see cref="Classify"/> applied when it said weak, so the hint quotes the same number:
    /// the rerank threshold when reranking, else the cosine one.
    /// </summary>
    private static float WeakThreshold(RetrievalToolOptions options) =>
        options.Rerank && options.RerankWeakThreshold is { } rerankThreshold ? rerankThreshold : options.WeakScoreThreshold;

    private static string DocumentName(TextChunk chunk)
    {
        foreach (var key in new[] { "DocumentName", "FileName", DocumentMetadataKeys.SourceName })
        {
            if (chunk.Metadata.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } name)
            {
                return name;
            }
        }

        return chunk.DocumentId;
    }

    private static string Truncate(string text, int maxChars) =>
        maxChars > 0 && text.Length > maxChars ? text[..maxChars] + "…" : text;

    /// <summary>The parsed and clamped arguments of one search call.</summary>
    private sealed record SearchArguments(string Query, int TopK, string? DocumentId)
    {
        public static SearchArguments Parse(string argumentsJson, RetrievalToolOptions options)
        {
            using var document = ParseObject(argumentsJson);
            var root = document.RootElement;

            var query = root.TryGetProperty("query", out var queryElement) && queryElement.ValueKind == JsonValueKind.String
                ? queryElement.GetString()?.Trim()
                : null;
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentException("search_knowledge_base needs a non-empty \"query\" string.", nameof(argumentsJson));
            }

            var topK = Math.Clamp(ReadTopK(root) ?? options.TopK, 1, Math.Max(1, options.MaxTopK));
            var documentId = options.DocumentFilter ?? ReadDocumentId(root);
            return new SearchArguments(query, topK, documentId);
        }

        private static JsonDocument ParseObject(string argumentsJson)
        {
            try
            {
                var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
                document.Dispose();
            }
            catch (JsonException)
            {
                // Falls through to the uniform message below: the model needs one clear sentence, not a parser position.
            }

            throw new ArgumentException("search_knowledge_base arguments must be a JSON object with a \"query\" string.", nameof(argumentsJson));
        }

        private static int? ReadTopK(JsonElement root)
        {
            if (!root.TryGetProperty("top_k", out var element)) return null;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number)) return number;
            return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static string? ReadDocumentId(JsonElement root) =>
            root.TryGetProperty("document_id", out var element) && element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } id
                ? id
                : null;
    }
}
