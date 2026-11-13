using TechieRag.Embedded;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// REQ-RAG-163 (AstroLyfe TR-RAG-001): <c>EmbeddedEmbeddingProvider.IsModelDownloaded()</c> and
/// <c>GetModelDirectory()</c> name the folder the model actually loaded from.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> Both static members looked only at <c>&lt;ModelRoot&gt;/bge-m3</c>. A model loaded
/// from a folder the host gave the provider, or from the copy an older version left next to the assembly,
/// worked while the flag said false and the folder named an empty directory, so AstroLyfe's health screen
/// read <c>"modelStatus":"Ready","modelDownloaded":false</c>.</para>
/// <para>The model root is pointed at an empty folder for each test, which is the "fresh copy of the build
/// output" of the report. The live test loads the real bge-m3 from the shared cache this machine stages it in,
/// <c>~/.cache/techierag-models/bge-m3</c>, which holds every file the model needs but no
/// <c>config.json</c>.</para>
/// <para><b>Steady under parallel runs.</b> Each test reads and writes a record of loaded folders of its own
/// (<c>EmbeddedModel.IsolateLoadedFolders</c>), so the live embedder tests that load bge-m3 in another
/// collection at the same moment cannot change what these assertions see.</para>
/// </remarks>
[Collection(ModelRootCollection.Name)]
public sealed class ModelDownloadedFlagTests : IDisposable
{
    private readonly string emptyRoot = Path.Combine(Path.GetTempPath(), "trroot-" + Guid.NewGuid().ToString("N"));

    /// <summary>Initializes a new instance of the <see cref="ModelDownloadedFlagTests"/> class.</summary>
    public ModelDownloadedFlagTests()
    {
        Directory.CreateDirectory(emptyRoot);
        ModelRoot.Set(emptyRoot);
    }

    /// <summary>
    /// A provider created from the shared cache folder embeds, and then the flag says downloaded, the folder
    /// is the cache folder, and the provider names it too.
    /// </summary>
    [BgeM3StagedFact(DisplayName = "REQ-RAG-163 LoadedModelReadsAsDownloaded")]
    public async Task LoadedModelReadsAsDownloaded()
    {
        using var isolated = EmbeddedModel.IsolateLoadedFolders();
        Assert.False(EmbeddedEmbeddingProvider.IsModelDownloaded(), "Precondition: the empty model root holds no model.");

        using var provider = new EmbeddedEmbeddingProvider(BgeM3StagedFactAttribute.Directory);
        var vector = await provider.EmbedAsync("Saturn in the seventh house");

        Assert.Equal(1024, vector.Length);
        Assert.True(EmbeddedEmbeddingProvider.IsModelDownloaded());
        Assert.Equal(BgeM3StagedFactAttribute.Directory, EmbeddedEmbeddingProvider.GetModelDirectory());
        Assert.Equal(BgeM3StagedFactAttribute.Directory, provider.ModelDirectory);
        Assert.True(provider.IsLoaded);
    }

    /// <summary>
    /// With nothing loaded and a complete copy only in the older version's folder, the folder named is that
    /// copy, the one a provider will load, not the empty model root.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-163 FolderNamesOlderCopyWhenOnlyItIsComplete")]
    public void FolderNamesOlderCopyWhenOnlyItIsComplete()
    {
        using var isolated = EmbeddedModel.IsolateLoadedFolders();
        var legacy = Path.Combine(emptyRoot, "legacy", EmbeddedModel.BgeM3.Name);
        StageSparseCopy(legacy);
        EmbeddedModel.LegacyDirectoryOverride = Path.GetDirectoryName(legacy);
        try
        {
            Assert.True(EmbeddedModel.BgeM3.IsDownloaded());
            Assert.Equal(legacy, EmbeddedModel.BgeM3.ResolveDirectory());
            Assert.Equal(legacy, EmbeddedModel.BgeM3.GetLoadDirectory());
        }
        finally
        {
            EmbeddedModel.LegacyDirectoryOverride = null;
        }
    }

    /// <summary>
    /// A folder recorded as loaded that has since lost its model file no longer reads as downloaded.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-163 RemovedFolderStopsReadingAsDownloaded")]
    public void RemovedFolderStopsReadingAsDownloaded()
    {
        using var isolated = EmbeddedModel.IsolateLoadedFolders();
        var folder = Path.Combine(emptyRoot, "given");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "model.onnx"), [1]);

        EmbeddedModel.BgeM3.NoteLoaded(folder);
        Assert.True(EmbeddedModel.BgeM3.IsDownloaded());

        File.Delete(Path.Combine(folder, "model.onnx"));
        Assert.False(EmbeddedModel.BgeM3.IsDownloaded());
        Assert.Equal(EmbeddedModel.BgeM3.GetModelDirectory(), EmbeddedModel.BgeM3.GetLoadDirectory());
    }

    /// <summary>
    /// A load recorded on another thread while this test is isolated (the live embedder tests running in
    /// parallel) is not seen here, and is seen again once the isolation ends.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-163 ParallelLoadDoesNotReachIsolatedTest")]
    public void ParallelLoadDoesNotReachIsolatedTest()
    {
        var folder = Path.Combine(emptyRoot, "elsewhere");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "model.onnx"), [1]);

        using (EmbeddedModel.IsolateLoadedFolders())
        {
            Thread other;
            using (ExecutionContext.SuppressFlow())
            {
                other = new Thread(() => EmbeddedModel.BgeM3.NoteLoaded(folder));
                other.Start();
            }

            other.Join();
            Assert.False(EmbeddedModel.BgeM3.IsDownloaded());
            Assert.Equal(EmbeddedModel.BgeM3.GetModelDirectory(), EmbeddedModel.BgeM3.GetLoadDirectory());
        }

        Assert.Equal(folder, EmbeddedModel.BgeM3.GetLoadDirectory());
        File.Delete(Path.Combine(folder, "model.onnx"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ModelRoot.Set(null);
        try
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file still held is left in the temp folder.
        }
    }

    /// <summary>Writes every bge-m3 file at its expected size without its contents (a sparse 2.2 GB file).</summary>
    /// <param name="folder">The folder.</param>
    private static void StageSparseCopy(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in EmbeddedModel.BgeM3.GetDownloadFiles())
        {
            using var stream = File.Create(Path.Combine(folder, file.FileName));
            stream.SetLength(file.ExpectedBytes);
        }
    }

    /// <summary>Runs a test only where the real bge-m3 weights are staged in the shared cache.</summary>
    private sealed class BgeM3StagedFactAttribute : FactAttribute
    {
        public BgeM3StagedFactAttribute()
        {
            var data = Path.Combine(Directory, "model.onnx_data");
            if (!File.Exists(Path.Combine(Directory, "model.onnx")) || !File.Exists(data) || new FileInfo(data).Length < 2_000_000_000)
            {
                Skip = $"The bge-m3 weights are not staged at {Directory}.";
            }
        }

        public static string Directory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "techierag-models", "bge-m3");
    }
}
