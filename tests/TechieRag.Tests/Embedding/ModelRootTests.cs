using TechieRag.Embedded;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// The model and cache root (REQ-RAG-053 / BRD-89): per-user application data by default,
/// host-overridable, shared by the embedding model and the reranker.
/// </summary>
[Collection(ModelRootCollection.Name)]
public class ModelRootTests
{
    /// <summary>
    /// With no override the root is <c>&lt;LocalApplicationData&gt;/TechieRag/models</c>, never the
    /// assembly folder.
    /// </summary>
    [Fact]
    public void DefaultRootIsUnderLocalApplicationData()
    {
        ModelRoot.Set(null);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, ModelRoot.DefaultPath, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(localAppData, "TechieRag", "models"), ModelRoot.DefaultPath);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), ModelRoot.DefaultPath, StringComparison.Ordinal);
    }

    /// <summary>
    /// A developer calling <c>UseEmbedded()</c> with no override gets model folders under the
    /// per-user application data folder, for the embedding model and the reranker alike.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-053 UseEmbeddedModelsLandUnderApplicationData")]
    public void UseEmbeddedModelsLandUnderApplicationData()
    {
        ModelRoot.Set(null);
        var root = ModelRoot.DefaultPath;

        Assert.Equal(Path.Combine(root, "bge-m3"), EmbeddedModel.BgeM3.GetModelDirectory());
        Assert.Equal(Path.Combine(root, "all-minilm-l6-v2"), EmbeddedModel.MiniLM.GetModelDirectory());
        Assert.Equal(Path.Combine(root, "bge-reranker-v2-m3"), OnnxCrossEncoderReranker.GetModelDirectory());
        Assert.StartsWith(root, EmbeddedEmbeddingProvider.GetModelDirectory(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A host override moves every model at once, and clearing it restores the default.
    /// </summary>
    [Fact]
    public void HostOverrideMovesEveryModel()
    {
        var custom = Path.Combine(Path.GetTempPath(), "techierag-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            new TechieRagBuilder().UseModelRoot(custom);

            Assert.True(ModelRoot.IsOverridden);
            Assert.Equal(Path.Combine(custom, "bge-m3"), EmbeddedModel.BgeM3.GetModelDirectory());
            Assert.Equal(Path.Combine(custom, "bge-reranker-v2-m3"), OnnxCrossEncoderReranker.GetModelDirectory());
        }
        finally
        {
            ModelRoot.Set(null);
        }

        Assert.False(ModelRoot.IsOverridden);
        Assert.Equal(ModelRoot.DefaultPath, ModelRoot.Current);
    }

    /// <summary>A model name is one folder, never a path that could escape the root.</summary>
    /// <param name="name">The rejected name.</param>
    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    public void ModelNameCannotEscapeTheRoot(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => ModelRoot.GetModelDirectory(name));
    }

    /// <summary>
    /// A host with no application data folder falls back to the profile, then the temp folder —
    /// never an empty path.
    /// </summary>
    [Fact]
    public void RootFallsBackWhenApplicationDataIsMissing()
    {
        var temp = Path.GetTempPath();

        Assert.Equal(Path.Combine("/home/u", ".local", "share", "TechieRag", "models"), ModelRoot.ComputeDefault("", "/home/u", temp));
        Assert.Equal(Path.Combine(temp, "TechieRag", "models"), ModelRoot.ComputeDefault(null, null, temp));
    }

    /// <summary>
    /// REQ-RAG-053: on iOS and Mac Catalyst, where .NET reports <c>Documents</c> as the local
    /// application data folder, the root is <c>&lt;home&gt;/Library/Application Support/TechieRag/models</c>,
    /// Apple's per-user application data folder; elsewhere the reported folder is kept.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-053 AppleRootIsApplicationSupportNotDocuments")]
    public void AppleRootIsApplicationSupportNotDocuments()
    {
        var temp = Path.GetTempPath();

        Assert.Equal(
            Path.Combine("/Users/u", "Library", "Application Support", "TechieRag", "models"),
            ModelRoot.ComputeDefault("/Users/u/Documents", "/Users/u", temp, isAppleUiKit: true));
        Assert.Equal(
            Path.Combine("/Users/u/Documents", "TechieRag", "models"),
            ModelRoot.ComputeDefault("/Users/u/Documents", "/Users/u", temp, isAppleUiKit: false));
        Assert.Equal(
            Path.Combine("/Users/u/Documents", "TechieRag", "models"),
            ModelRoot.ComputeDefault("/Users/u/Documents", "", temp, isAppleUiKit: true));
    }
}
