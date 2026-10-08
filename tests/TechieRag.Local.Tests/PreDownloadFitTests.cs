using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TechieRag.Embedded;
using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// A host can tell before the download whether the local model will run and fit here, a load refuses a
/// model that cannot fit before its first byte, and a provider reports its own download's progress alone
/// (REQ-RAG-125, Sevak TR-RAG-047).
/// </summary>
[Collection(ModelDownloadCollection.Name)]
public sealed class PreDownloadFitTests : IDisposable
{
    private static readonly Uri TermsUrl = new("https://example.org/model-terms");

    private readonly LocalFileServer server = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "techierag-local-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] weights = RandomNumberGenerator.GetBytes(120_000);

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
    /// A catalogue model's memory need is public before anything is downloaded: the weights, the context
    /// cache at the asked context size and the runtime's working memory, the same figure the load checks.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-125 CatalogueModelMemoryNeedIsKnownBeforeDownload")]
    public void CatalogueModelMemoryNeedIsKnownBeforeDownload()
    {
        var model = LocalModel.Qwen25Instruct05B;
        var weightBytes = model.FindVariant(LocalModelFormat.OnnxGenAi)!.DownloadBytes;

        var atOneThousand = model.EstimateRequiredMemoryBytes(1_024);
        var atDefault = model.EstimateRequiredMemoryBytes();

        Assert.Equal(weightBytes + (model.KvBytesPerToken * 1_024) + LocalModel.RuntimeOverheadBytes, atOneThousand);
        Assert.Equal(model.EstimateMemoryBytes(weightBytes, model.DefaultContextSize(LocalModel.IsPhonePlatform)), atDefault);
        Assert.True(atDefault > atOneThousand);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.EstimateRequiredMemoryBytes(0));
    }

    /// <summary>A host folder that does not exist has no known weight size, so the estimate is null.</summary>
    [Fact(DisplayName = "REQ-RAG-125 MissingFolderModelHasNoEstimate")]
    public void MissingFolderModelHasNoEstimate()
    {
        var model = LocalModel.FromFolder(Path.Combine(directory, "absent"), LocalChatTemplate.ChatMl);

        Assert.Null(model.EstimateRequiredMemoryBytes());
    }

    /// <summary>
    /// Whether this platform has a runtime, and the free memory the library measures, are both public and
    /// read nothing from the network.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-125 RuntimeAndFreeMemoryArePublic")]
    public void RuntimeAndFreeMemoryArePublic()
    {
        Assert.Equal(LocalRuntimeSelector.IsSupported(RuntimeInformation.ProcessArchitecture), LocalLlmProvider.IsRuntimeAvailable);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        {
            Assert.True(AvailableMemory.Read() > 0);
        }
    }

    /// <summary>
    /// With less free memory than the model needs, the load refuses before any request is sent and before the
    /// terms are asked, naming the shortfall; nothing is downloaded or loaded.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-125 ShortMemoryRefusesBeforeFirstByte")]
    public async Task ShortMemoryRefusesBeforeFirstByte()
    {
        var (model, _) = TestModel("fit");
        var runtime = new FakeLocalLlmRuntime();
        var asked = 0;
        var options = new LocalLlmOptions
        {
            Model = model,
            ConfirmTermsAsync = (_, _) =>
            {
                asked++;
                return Task.FromResult(true);
            }
        };
        using var provider = new LocalLlmProvider(options, null, runtime, new MemoryGate(() => 1_000), LocalModelStore.Shared, isPhone: false);

        var error = await Assert.ThrowsAsync<LocalModelMemoryException>(() => provider.LoadAsync());

        Assert.Equal(1_000, error.AvailableBytes);
        Assert.Empty(server.Requests);
        Assert.Equal(0, asked);
        Assert.Equal(0, runtime.LoadCount);
    }

    /// <summary>
    /// <c>LocalLlmOptions.DownloadProgress</c> receives this provider's download and never another model's
    /// download running through the same process-wide service, and ends at the completed state.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-125 DownloadProgressReportsOnlyThisProvider")]
    public async Task DownloadProgressReportsOnlyThisProvider()
    {
        var (mine, mineVariant) = TestModel("mine");
        var (other, otherVariant) = TestModel("other");
        var reports = new ConcurrentQueue<ModelDownloadProgress>();
        var options = new LocalLlmOptions { TermsAccepted = true, DownloadProgress = new SyncProgress(reports.Enqueue) };

        await Task.WhenAll(
            LocalModelStore.Shared.EnsureAsync(other, otherVariant, Path.Combine(directory, "other"), new LocalLlmOptions { TermsAccepted = true }, null, CancellationToken.None),
            LocalModelStore.Shared.EnsureAsync(mine, mineVariant, Path.Combine(directory, "mine"), options, null, CancellationToken.None));

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(mine.Id, r.ModelName));
        Assert.Equal(ModelDownloadStatus.Completed, reports.Last().Status);
        Assert.Equal(weights.Length, reports.Last().TotalBytes);
        Assert.Equal(reports.Count, reports.Distinct().Count());
    }

    private (LocalModel Model, LocalModelVariant Variant) TestModel(string folder)
    {
        server.Add(folder + "/weights.bin", weights);
        var variant = new LocalModelVariant(
            LocalModelFormat.Test,
            "test-model-" + folder + "-" + Guid.NewGuid().ToString("N")[..8],
            server.BaseUrl + folder,
            [new LocalModelFileSpec("weights.bin", "weights.bin", weights.Length, Convert.ToHexStringLower(SHA256.HashData(weights)))]);
        var model = new LocalModel(
            "test-model-" + folder + "-" + Guid.NewGuid().ToString("N")[..8], "Test model", "MIT", TermsUrl, LocalChatTemplate.ChatMl,
            contextLength: 4_096, kvBytesPerToken: 1_024, runsOnPhones: true, variants: [variant]);
        return (model, variant);
    }

    /// <summary>Reports on the calling thread, so the test sees every report before the download returns.</summary>
    /// <param name="report">Receives each report.</param>
    private sealed class SyncProgress(Action<ModelDownloadProgress> report) : IProgress<ModelDownloadProgress>
    {
        /// <inheritdoc/>
        public void Report(ModelDownloadProgress value) => report(value);
    }
}
