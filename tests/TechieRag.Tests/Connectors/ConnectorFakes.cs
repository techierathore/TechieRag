using TechieRag.Abstractions;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using TechieRag.Connectors.Http;
using TechieRag.Models;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// A connector whose listing and fetching are scripted, so the runner's own behaviour can be
/// asserted without any source at all.
/// </summary>
internal sealed class FakeDataConnector : IDataConnector
{
    private readonly List<List<ConnectorItem>> pages = [];
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> failures = new(StringComparer.Ordinal);
    private readonly List<ConnectorItemFailure> listFailures = [];

    public string SourceType => "fake";

    public string SourceName => "fake source";

    public bool ListsEntireSource { get; set; } = true;

    public List<string> Fetched { get; } = [];

    public List<ConnectorListRequest> ListRequests { get; } = [];

    public FakeDataConnector Page(params ConnectorItem[] items)
    {
        pages.Add([.. items]);
        foreach (var item in items)
        {
            texts.TryAdd(item.Id, $"text of {item.Id}");
        }

        return this;
    }

    public FakeDataConnector WithText(string id, string text)
    {
        texts[id] = text;
        return this;
    }

    public FakeDataConnector WithFailure(string id, Exception error)
    {
        failures[id] = error;
        return this;
    }

    public FakeDataConnector WithListFailure(string reason)
    {
        listFailures.Add(new ConnectorItemFailure("listing", "listing", reason));
        return this;
    }

    public Task<ConnectorPage> ListAsync(ConnectorListRequest request, CancellationToken cancellationToken = default)
    {
        ListRequests.Add(request);
        var index = request.Cursor is null ? 0 : int.Parse(request.Cursor);

        if (index >= pages.Count)
        {
            return Task.FromResult(ConnectorPage.Empty);
        }

        var next = index + 1 < pages.Count ? (index + 1).ToString() : null;
        return Task.FromResult(new ConnectorPage(pages[index], next, index == 0 ? listFailures : []));
    }

    public Task<ConnectorDocument> FetchAsync(ConnectorItem item, CancellationToken cancellationToken = default)
    {
        Fetched.Add(item.Id);

        return failures.TryGetValue(item.Id, out var error)
            ? throw error
            : Task.FromResult(new ConnectorDocument(item, texts.GetValueOrDefault(item.Id, string.Empty)));
    }
}

/// <summary>
/// An HTTP transport whose answers are declared by URL fragment, so connector paging and error
/// mapping are provable without a host.
/// </summary>
internal sealed class FakeConnectorTransport : IConnectorTransport
{
    private readonly List<(string Fragment, ConnectorHttpResponse Response)> routes = [];

    public List<ConnectorHttpRequest> Requests { get; } = [];

    public FakeConnectorTransport Route(
        string urlFragment,
        string body,
        int status = 200,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        routes.Add((urlFragment, new ConnectorHttpResponse(status, body, headers)));
        return this;
    }

    public Task<ConnectorHttpResponse> GetAsync(
        ConnectorHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        foreach (var (fragment, response) in routes)
        {
            if (request.Url.Contains(fragment, StringComparison.Ordinal))
            {
                return Task.FromResult(response);
            }
        }

        return Task.FromResult(new ConnectorHttpResponse(404, "{\"message\":\"no route in fake\"}"));
    }
}

/// <summary>A mail transport backed by declared messages rather than a server.</summary>
internal sealed class FakeMailTransport : IMailTransport
{
    private readonly Dictionary<string, List<(MailHeader Header, byte[] Raw)>> folders =
        new(StringComparer.Ordinal);

    public string MailboxName => "fake.mail.test";

    public List<(string Folder, MailSearchCriteria Criteria, int Skip, int Take)> Searches { get; } = [];

    public List<string> Fetched { get; } = [];

    public FakeMailTransport Message(
        string folder,
        string uid,
        string subject,
        string from,
        string raw,
        DateTimeOffset? date = null)
    {
        if (!folders.TryGetValue(folder, out var list))
        {
            list = [];
            folders[folder] = list;
        }

        list.Add((
            new MailHeader(folder, uid, "1", subject, from, "someone@example.test", date, raw.Length, $"<{uid}@test>"),
            System.Text.Encoding.UTF8.GetBytes(raw)));

        return this;
    }

    public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. folders.Keys]);

    public Task<MailSearchPage> SearchAsync(
        string folder,
        MailSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        Searches.Add((folder, criteria, skip, take));

        if (!folders.TryGetValue(folder, out var list))
        {
            return Task.FromResult(new MailSearchPage([], false));
        }

        var matches = list
            .Where(entry => criteria.SinceUtc is not { } since || entry.Header.Date is null || entry.Header.Date >= since)
            .ToList();

        var slice = matches.Skip(skip).Take(take).Select(e => e.Header).ToList();
        return Task.FromResult(new MailSearchPage(slice, skip + slice.Count < matches.Count));
    }

    public Task<byte[]> FetchAsync(MailHeader header, CancellationToken cancellationToken = default)
    {
        Fetched.Add(header.Uid);

        var entry = folders[header.Folder].First(e => e.Header.Uid == header.Uid);
        return Task.FromResult(entry.Raw);
    }
}

/// <summary>An IMAP byte pipe whose server side is a script, so the protocol itself is testable.</summary>
internal sealed class ScriptedImapConnection : IImapConnection
{
    private readonly Queue<object> scripted = new();

    public bool IsSecure { get; set; } = true;

    public List<string> Written { get; } = [];

    public bool IsDisposed { get; private set; }

    public ScriptedImapConnection Line(string line)
    {
        scripted.Enqueue(line);
        return this;
    }

    public ScriptedImapConnection Literal(string content)
    {
        scripted.Enqueue(System.Text.Encoding.Latin1.GetBytes(content));
        return this;
    }

    public Task<string> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(scripted.Count > 0 && scripted.Peek() is string ? (string)scripted.Dequeue() : "* OK ready");

    public Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        Written.Add(line);
        return Task.CompletedTask;
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(scripted.Count > 0 && scripted.Peek() is string ? (string?)scripted.Dequeue() : null);

    public Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken = default)
    {
        var bytes = (byte[])scripted.Dequeue();
        return Task.FromResult(bytes);
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>The smallest ITechieRag that records what was ingested.</summary>
internal sealed class RecordingRag : ITechieRag
{
    public List<(string Text, string Name, Dictionary<string, object>? Metadata)> Ingested { get; } = [];

    public Task<string> IngestTextAsync(
        string text,
        string documentName,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        Ingested.Add((text, documentName, metadata));
        return Task.FromResult($"doc-{Ingested.Count}");
    }

    public Task<string> IngestAsync(string filePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<string>> IngestDirectoryAsync(string directoryPath, string searchPattern = "*.*", CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>([]);

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>([]);

    public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new IngestionStats());

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<RagResponse> AskAsync(string question, int topK = 5, string? systemPrompt = null, string? documentFilter = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<string> AskStreamAsync(string question, int topK = 5, string? systemPrompt = null, string? documentFilter = null, LlmCompletionOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<RagResponse> ChatWithRagAsync(string userMessage, IReadOnlyList<ChatMessage>? conversationHistory = null, int topK = 5, string? systemPrompt = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<string> ChatWithRagStreamAsync(string userMessage, IReadOnlyList<ChatMessage>? conversationHistory = null, int topK = 5, string? systemPrompt = null, LlmCompletionOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public ILlmProvider? GetLlmProvider() => null;

    public ITokenTracker GetTokenTracker() => throw new NotSupportedException();

    public IConversationMemory? GetConversationMemory() => null;
}

/// <summary>A processor that returns a fixed text, standing in for a real attachment reader.</summary>
internal sealed class StubAttachmentProcessor : IDocumentProcessor
{
    private readonly string text;

    public StubAttachmentProcessor(string extension, string text)
    {
        SupportedExtensions = [extension];
        this.text = text;
    }

    public IReadOnlyList<string> SupportedExtensions { get; }

    public Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TextChunk>>(
            [new TextChunk { DocumentId = fileName, Text = text }]);
}
