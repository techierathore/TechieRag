using TechieRag.Embedded;
using TechieRag.Llm;
using TechieRag.Local.HuggingFace;
using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// REQ-RAG-108 / BRD-166: <c>LocalModel.FromHuggingFace</c> against a stand-in Hugging Face on a loopback
/// server — licence first, only the runtime files, every fingerprint checked, gated and malformed names
/// refused, an unpinned name pinned once and reused offline. Real HTTP through the shared
/// <see cref="ModelDownloadService"/>.
/// </summary>
[Collection(ModelDownloadCollection.Name)]
public sealed class HuggingFaceModelTests : IDisposable
{
    private const string Repository = "Arm/gemma-test-onnx";

    private readonly LocalFileServer server = new();
    private readonly FakeHuggingFaceRepository repository;
    private readonly string root = Path.Combine(Path.GetTempPath(), "techierag-local-tests", "hf-" + Guid.NewGuid().ToString("N"));

    /// <summary>Publishes the stand-in repository.</summary>
    public HuggingFaceModelTests()
    {
        repository = new FakeHuggingFaceRepository(server, Repository);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        server.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The host's terms dialog receives the model card's licence, the terms URL (the model page at the
    /// resolved commit, when the repository has no LICENSE file) and the download size, at a moment when
    /// only Hugging Face's API has been asked and no model file has been requested.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 TermsShownBeforeAnyFileRequest")]
    public async Task TermsShownBeforeAnyFileRequest()
    {
        repository.Publish();
        var model = NewModel();
        LocalModelTerms? shown = null;
        List<string>? requestedWhenShown = null;
        var options = new LocalLlmOptions
        {
            ConfirmTermsAsync = (terms, _) =>
            {
                shown = terms;
                requestedWhenShown = server.Requests.Select(r => r.Path).ToList();
                return Task.FromResult(true);
            }
        };

        await EnsureAsync(model, options);

        Assert.NotNull(shown);
        Assert.Equal(("gemma", new Uri($"{server.BaseUrl}{Repository}/tree/{repository.Commit}"), repository.RuntimeBytes),
            (shown.LicenceName, shown.TermsUrl, shown.DownloadBytes));
        Assert.All(requestedWhenShown!, path => Assert.StartsWith("api/models/", path, StringComparison.Ordinal));
    }

    /// <summary>
    /// GetTermsAsync gives the complete terms from the API alone; a LICENSE file in the repository becomes
    /// the terms URL, pinned to the commit.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 TermsNameTheLicenceFile")]
    public async Task TermsNameTheLicenceFile()
    {
        repository.AddFile("LICENSE", "Apache License"u8.ToArray(), lfs: false);
        repository.Licence = "apache-2.0";
        repository.Publish();
        var model = NewModel();

        var terms = await Store().GetTermsAsync(model, LocalModelFormat.OnnxGenAi, CancellationToken.None);

        Assert.Equal(("apache-2.0", new Uri($"{server.BaseUrl}{Repository}/blob/{repository.Commit}/LICENSE"), repository.RuntimeBytes),
            (terms.LicenceName, terms.TermsUrl, terms.DownloadBytes));
        Assert.DoesNotContain(server.Requests, r => r.Path.Contains("/resolve/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Only the files ONNX Runtime GenAI loads are fetched: the README, the example script, the
    /// profiling trace and the benchmark folder are left on Hugging Face.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 OnlyRuntimeFilesRequested")]
    public async Task OnlyRuntimeFilesRequested()
    {
        repository.Publish();
        var model = NewModel();

        await EnsureAsync(model, Accepted());

        var fetched = server.Requests.Select(r => r.Path)
            .Where(p => p.Contains("/resolve/", StringComparison.Ordinal))
            .Select(p => p[(p.LastIndexOf('/') + 1)..])
            .Order(StringComparer.Ordinal);
        Assert.Equal(FakeHuggingFaceRepository.RuntimeFiles, fetched);
    }

    /// <summary>
    /// After the download the model's limits come from its own genai_config.json: 4,096-token context,
    /// 26 layers × 1 key/value head × 256 × 2 × 2 bytes = 26,624 bytes of cache per token.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 LimitsReadFromGenAiConfig")]
    public async Task LimitsReadFromGenAiConfig()
    {
        repository.Publish();
        var model = NewModel();

        await EnsureAsync(model, Accepted());

        Assert.Equal((4_096, 26_624L), (model.ContextLength, model.KvBytesPerToken));
    }

    /// <summary>A large file whose SHA-256 does not match the one Hugging Face lists is refused and deleted.</summary>
    [Fact(DisplayName = "REQ-RAG-108 TamperedLargeFileRefused")]
    public async Task TamperedLargeFileRefused()
    {
        repository.Publish(tamperedPath: "model.onnx.data");
        var model = NewModel();

        var error = await Assert.ThrowsAsync<LocalModelIntegrityException>(() => EnsureAsync(model, Accepted()));

        Assert.Equal(("model.onnx.data", "SHA-256"), (error.FileName, error.HashKind));
        Assert.False(File.Exists(Path.Combine(model.Resolution!.Directory, "model.onnx.data")));
    }

    /// <summary>
    /// A small file whose git blob SHA-1 does not match the <c>oid</c> Hugging Face lists is refused and deleted.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 TamperedSmallFileRefused")]
    public async Task TamperedSmallFileRefused()
    {
        repository.Publish(tamperedPath: "tokenizer_config.json");
        var model = NewModel();

        var error = await Assert.ThrowsAsync<LocalModelIntegrityException>(() => EnsureAsync(model, Accepted()));

        Assert.Equal(("tokenizer_config.json", "git blob SHA-1"), (error.FileName, error.HashKind));
        Assert.False(File.Exists(Path.Combine(model.Resolution!.Directory, "tokenizer_config.json")));
    }

    /// <summary>The git blob hash matches git's own: the empty blob is e69de29….</summary>
    [Fact(DisplayName = "REQ-RAG-108 GitBlobHashMatchesGit")]
    public async Task GitBlobHashMatchesGit()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "empty");
        await File.WriteAllBytesAsync(path, []);

        var hash = await LocalModelStore.HashGitBlobAsync(path, CancellationToken.None);

        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", hash);
    }

    /// <summary>
    /// A gated model is refused before its file list is read, with a message saying the library takes no
    /// Hugging Face token.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 GatedRepositoryRefused")]
    public async Task GatedRepositoryRefused()
    {
        repository.Gated = "manual";
        repository.Publish();
        var model = NewModel();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Store().PrepareAsync(model, CancellationToken.None));

        Assert.Contains("is gated", error.Message, StringComparison.Ordinal);
        Assert.Contains("does not take Hugging Face tokens", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Requests, r => r.Path.Contains("/tree/", StringComparison.Ordinal));
    }

    /// <summary>A repository Hugging Face does not serve publicly is refused with a message naming the three causes.</summary>
    [Fact(DisplayName = "REQ-RAG-108 MissingRepositoryRefused")]
    public async Task MissingRepositoryRefused()
    {
        var model = NewModel();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Store().PrepareAsync(model, CancellationToken.None));

        Assert.Contains("does not exist, is private, or needs a Hugging Face token", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Malformed names are refused before anything else: a repository id that is not owner/name, and
    /// any part that could climb out of the model root or is outside Hugging Face's character set.
    /// </summary>
    /// <param name="repositoryId">The repository id.</param>
    /// <param name="folder">The folder.</param>
    /// <param name="version">The version.</param>
    [Theory(DisplayName = "REQ-RAG-108 InvalidNameRefused")]
    [InlineData("gemma", null, null)]
    [InlineData("a/b/c", null, null)]
    [InlineData("../evil", null, null)]
    [InlineData("owner/..", null, null)]
    [InlineData("owner/na me", null, null)]
    [InlineData("owner/a--b", null, null)]
    [InlineData("owner/name", "../../etc", null)]
    [InlineData("owner/name", "a/../b", null)]
    [InlineData("owner/name", null, "../main")]
    [InlineData("owner/name", null, "refs/pr/1")]
    public void InvalidNameRefused(string repositoryId, string? folder, string? version) =>
        Assert.Throws<ArgumentException>(() => LocalModel.FromHuggingFace(repositoryId, folder, version));

    /// <summary>
    /// An unpinned name is resolved once and recorded: a later FromHuggingFace with the same name, after
    /// the page moved to a new commit and with Hugging Face unreachable, loads the pinned files from disk
    /// without a single request and without asking for the terms again.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 UnpinnedNamePinnedAndReusedOffline")]
    public async Task UnpinnedNamePinnedAndReusedOffline()
    {
        repository.Publish();
        var first = NewModel();
        await EnsureAsync(first, Accepted());
        var pinnedCommit = first.Resolution!.Snapshot.Commit;
        repository.Commit = "0123456789abcdef0123456789abcdef01234567";
        repository.Publish();
        server.Dispose();
        var requestsBefore = server.Requests.Count;

        var second = NewModel();
        var offline = new LocalModelStore(ModelDownloadService.Instance, new HuggingFaceHub(new Uri(server.BaseUrl), new HttpClientHandler()));
        await offline.PrepareAsync(second, CancellationToken.None);
        await offline.EnsureAsync(second, second.Resolution!.Variant, second.Resolution.Directory, new LocalLlmOptions(), null, CancellationToken.None);

        Assert.Equal((pinnedCommit, requestsBefore), (second.Resolution.Snapshot.Commit, server.Requests.Count));
        Assert.Equal(("gemma", 4_096), (second.LicenceName, second.ContextLength));
    }

    /// <summary>A version given as a commit id is used exactly: its revision is asked for and its folder named after it.</summary>
    [Fact(DisplayName = "REQ-RAG-108 ExactVersionUsedAsGiven")]
    public async Task ExactVersionUsedAsGiven()
    {
        repository.Publish();
        var model = LocalModel.FromHuggingFace(Repository, null, repository.Commit, root);

        await Store().PrepareAsync(model, CancellationToken.None);

        Assert.Contains(server.Requests, r => r.Path == $"api/models/{Repository}/revision/{repository.Commit}");
        Assert.EndsWith("hf--arm--gemma-test-onnx--" + repository.Commit[..12], model.Resolution!.Directory, StringComparison.Ordinal);
        Assert.False(File.Exists(model.HuggingFace!.PinPath));
    }

    /// <summary>
    /// UseLocalLlm accepts a Hugging Face model without touching the network: the configuration records
    /// the repository as the local model.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 UseLocalLlmTakesHuggingFaceModel")]
    public void UseLocalLlmTakesHuggingFaceModel()
    {
        var builder = new TechieRagBuilder().UseLocalLlm(LocalModel.FromHuggingFace(Repository, null, null, root));

        Assert.Equal((LlmSource.Local, Repository), (builder.GetConfig().Llm.Source, builder.GetConfig().Llm.Model));
        Assert.Empty(server.Requests);
    }

    private LocalModel NewModel() => LocalModel.FromHuggingFace(Repository, null, null, root);

    private LocalModelStore Store() =>
        new(ModelDownloadService.Instance, new HuggingFaceHub(new Uri(server.BaseUrl), new HttpClientHandler()));

    private async Task EnsureAsync(LocalModel model, LocalLlmOptions options)
    {
        var store = Store();
        await store.PrepareAsync(model, CancellationToken.None);
        var resolution = model.Resolution!;
        await store.EnsureAsync(model, resolution.Variant, resolution.Directory, options, null, CancellationToken.None);
    }

    private static LocalLlmOptions Accepted() => new() { TermsAccepted = true };
}
