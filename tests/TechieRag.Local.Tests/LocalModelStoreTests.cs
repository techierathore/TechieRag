using System.Security.Cryptography;
using TechieRag.Embedded;
using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// The one-time model download: terms first, size before the first byte, resumable, SHA-256
/// verified, mirror honoured (REQ-RAG-061 / BRD-100, REQ-RAG-062 / BRD-101). Runs real HTTP against a
/// loopback server through the shared <see cref="ModelDownloadService"/>.
/// </summary>
[Collection(ModelDownloadCollection.Name)]
public sealed class LocalModelStoreTests : IDisposable
{
    private static readonly Uri TermsUrl = new("https://example.org/model-terms");

    private readonly LocalFileServer server = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "techierag-local-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] first = RandomNumberGenerator.GetBytes(200_000);
    private readonly byte[] second = RandomNumberGenerator.GetBytes(50_000);

    /// <inheritdoc/>
    public void Dispose()
    {
        server.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Starting a download without terms acceptance sends no request, and the exception hands the host
    /// the terms URL and licence to show.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-062 DownloadWithoutTermsRequestsNothing")]
    public async Task DownloadWithoutTermsRequestsNothing()
    {
        var (model, variant) = TestModel();

        var error = await Assert.ThrowsAsync<LocalModelTermsNotAcceptedException>(
            () => LocalModelStore.Shared.EnsureAsync(model, variant, directory, new LocalLlmOptions(), null, CancellationToken.None));

        Assert.Empty(server.Requests);
        Assert.Equal(TermsUrl, error.Terms.TermsUrl);
        Assert.Equal("MIT", error.Terms.LicenceName);
        Assert.False(File.Exists(Path.Combine(directory, "first.bin")));
    }

    /// <summary>The host's terms dialog receives the terms and the download size before anything is fetched.</summary>
    [Fact]
    public async Task ConfirmTermsSeesTermsAndSize()
    {
        var (model, variant) = TestModel();
        LocalModelTerms? shown = null;
        var requestsWhenShown = -1;
        var options = new LocalLlmOptions
        {
            ConfirmTermsAsync = (terms, _) =>
            {
                shown = terms;
                requestsWhenShown = server.Requests.Count;
                return Task.FromResult(true);
            }
        };

        await LocalModelStore.Shared.EnsureAsync(model, variant, directory, options, null, CancellationToken.None);

        Assert.Equal(first.Length + second.Length, shown!.DownloadBytes);
        Assert.Equal(0, requestsWhenShown);
    }

    /// <summary>An accepted download lands every file with its pinned SHA-256 and marks the folder verified.</summary>
    [Fact]
    public async Task AcceptedDownloadIsVerifiedAndMarked()
    {
        var (model, variant) = TestModel();

        await LocalModelStore.Shared.EnsureAsync(model, variant, directory, Accepted(), null, CancellationToken.None);

        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(directory, "first.bin")));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(directory, "second.bin")));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(directory, LocalModelStore.VerifiedMarkerFileName)).Length);
    }

    /// <summary>A file whose SHA-256 does not match is deleted and the load fails naming it.</summary>
    [Fact(DisplayName = "REQ-RAG-061 ChecksumMismatchDeletesFile")]
    public async Task ChecksumMismatchDeletesFile()
    {
        var (model, variant) = TestModel(secondSha: new string('0', 64));

        var error = await Assert.ThrowsAsync<LocalModelIntegrityException>(
            () => LocalModelStore.Shared.EnsureAsync(model, variant, directory, Accepted(), null, CancellationToken.None));

        Assert.Equal("second.bin", error.FileName);
        Assert.False(File.Exists(Path.Combine(directory, "second.bin")));
    }

    /// <summary>An interrupted download resumes from its partial file with a range request and verifies.</summary>
    [Fact(DisplayName = "REQ-RAG-061 InterruptedDownloadResumes")]
    public async Task InterruptedDownloadResumes()
    {
        var (model, variant) = TestModel();
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "first.bin.part"), first[..120_000]);

        var error = await Record.ExceptionAsync(
            () => LocalModelStore.Shared.EnsureAsync(model, variant, directory, Accepted(), null, CancellationToken.None));

        Assert.True(error is null, $"{error?.Message} Server: {string.Join("; ", server.Errors)}");
        Assert.Contains(server.Requests, r => r.Path.EndsWith("first.bin", StringComparison.Ordinal) && r.Range == "bytes=120000-");
        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(directory, "first.bin")));
    }

    /// <summary>The size is reported through ModelDownloadService before the first byte is requested.</summary>
    [Fact(DisplayName = "REQ-RAG-061 SizeReportedBeforeFirstByte")]
    public async Task SizeReportedBeforeFirstByte()
    {
        var (model, variant) = TestModel();
        long reported = -1;
        var requestsAtReport = -1;
        void OnSize(object? sender, ModelDownloadSizeEventArgs e)
        {
            if (e.ModelName == model.Id)
            {
                reported = e.TotalBytes;
                requestsAtReport = server.Requests.Count;
            }
        }

        ModelDownloadService.Instance.DownloadSizeKnown += OnSize;
        try
        {
            await LocalModelStore.Shared.EnsureAsync(model, variant, directory, Accepted(), null, CancellationToken.None);
        }
        finally
        {
            ModelDownloadService.Instance.DownloadSizeKnown -= OnSize;
        }

        Assert.Equal(first.Length + second.Length, reported);
        Assert.Equal(0, requestsAtReport);
    }

    /// <summary>A complete, verified folder downloads nothing and needs no terms again.</summary>
    [Fact]
    public async Task CompleteFolderDownloadsNothingAgain()
    {
        var (model, variant) = TestModel();
        await LocalModelStore.Shared.EnsureAsync(model, variant, directory, Accepted(), null, CancellationToken.None);
        var requests = server.Requests.Count;

        await LocalModelStore.Shared.EnsureAsync(model, variant, directory, new LocalLlmOptions(), null, CancellationToken.None);

        Assert.Equal(requests, server.Requests.Count);
    }

    /// <summary>TECHIERAG_MODEL_BASE_URL redirects every file to &lt;mirror&gt;/&lt;folder&gt;/&lt;file&gt;.</summary>
    [Fact]
    public void MirrorRedirectsEveryFile()
    {
        var variant = LocalModel.Qwen25Instruct05B.Variants.Single(v => v.Format == LocalModelFormat.Gguf);

        var files = variant.GetDownloadFiles("https://mirror.example/models/");

        Assert.Equal(
            "https://mirror.example/models/qwen2.5-0.5b-instruct-gguf/qwen2.5-0.5b-instruct-q4_k_m.gguf",
            files.Single().Url.ToString());
    }

    private static LocalLlmOptions Accepted() => new() { TermsAccepted = true };

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private (LocalModel Model, LocalModelVariant Variant) TestModel(string? secondSha = null)
    {
        server.Add("m/first.bin", first);
        server.Add("m/second.bin", second);
        var variant = new LocalModelVariant(
            LocalModelFormat.Test,
            "test-model",
            server.BaseUrl + "m",
            [
                new LocalModelFileSpec("first.bin", "first.bin", first.Length, Sha(first)),
                new LocalModelFileSpec("second.bin", "second.bin", second.Length, secondSha ?? Sha(second))
            ]);
        var model = new LocalModel(
            "test-model-" + Guid.NewGuid().ToString("N")[..8], "Test model", "MIT", TermsUrl, LocalChatTemplate.ChatMl,
            contextLength: 4_096, kvBytesPerToken: 1_024, runsOnPhones: true, variants: [variant]);
        return (model, variant);
    }
}
