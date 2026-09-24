using TechieRag.Embedded;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Phone defaults for <c>UseEmbedded()</c> (REQ-RAG-054 / BRD-91): a 384-dimension model on Android
/// and iOS, bge-m3 refused there with its 2.3 GB size named; desktop unchanged.
/// </summary>
/// <remarks>
/// The platform is a parameter of the internal overload so both answers are testable on any host;
/// the public overloads pass <see cref="EmbeddedModel.IsPhonePlatform"/>.
/// </remarks>
public class EmbeddedModelTests
{
    /// <summary>On a phone, <c>UseEmbedded()</c> with no model selects the 384-dimension MiniLM.</summary>
    [Fact(DisplayName = "REQ-RAG-054 PhoneDefaultIs384Dimensions")]
    public void PhoneDefaultIs384Dimensions()
    {
        var builder = new TechieRagBuilder();

        TechieRagBuilderExtensions.UseEmbedded(builder, EmbeddedModel.DefaultFor(isPhone: true), isPhone: true);

        Assert.Equal(384, EmbeddedModel.DefaultFor(isPhone: true).Dimensions);
        Assert.Equal("all-minilm-l6-v2", builder.GetConfig().Embedding.Model);
        Assert.Equal(EmbeddingSource.Embedded, builder.GetConfig().Embedding.Source);
    }

    /// <summary>On a phone, asking for bge-m3 throws a message naming its 2.3 GB size.</summary>
    [Fact(DisplayName = "REQ-RAG-054 PhoneRefusesBgeM3NamingItsSize")]
    public void PhoneRefusesBgeM3NamingItsSize()
    {
        var builder = new TechieRagBuilder();

        var refusal = Assert.Throws<NotSupportedException>(
            () => TechieRagBuilderExtensions.UseEmbedded(builder, EmbeddedModel.BgeM3, isPhone: true));

        Assert.Contains("2.3 GB", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("bge-m3", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("MiniLM", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>On a desktop the default stays bge-m3 at 1024 dimensions.</summary>
    [Fact]
    public void DesktopDefaultIsBgeM3()
    {
        var builder = new TechieRagBuilder();

        TechieRagBuilderExtensions.UseEmbedded(builder, EmbeddedModel.DefaultFor(isPhone: false), isPhone: false);

        Assert.Same(EmbeddedModel.BgeM3, EmbeddedModel.DefaultFor(isPhone: false));
        Assert.Equal(1024, EmbeddedModel.BgeM3.Dimensions);
        Assert.Equal("bge-m3", builder.GetConfig().Embedding.Model);
    }

    /// <summary>The test host is a desktop, so the public default is bge-m3 here.</summary>
    [Fact]
    public void ThisHostIsNotAPhone()
    {
        Assert.False(EmbeddedModel.IsPhonePlatform);
        Assert.Same(EmbeddedModel.BgeM3, EmbeddedModel.PlatformDefault);
    }

    /// <summary>The recorded download sizes are the ones the refusal and the size event report.</summary>
    [Fact]
    public void DownloadSizesAreRecorded()
    {
        Assert.Equal("2.3 GB", EmbeddedModel.BgeM3.DownloadSize);
        Assert.Equal("91 MB", EmbeddedModel.MiniLM.DownloadSize);
        Assert.True(EmbeddedModel.MiniLM.RunsOnPhones);
        Assert.False(EmbeddedModel.BgeM3.RunsOnPhones);
    }

    /// <summary>MiniLM's files come from the sentence-transformers repository, ONNX export included.</summary>
    [Fact]
    public void MiniLMFilesComeFromSentenceTransformers()
    {
        var files = EmbeddedModel.MiniLM.GetDownloadFiles();

        Assert.Contains(files, f => f.FileName == "model.onnx" && f.Url.AbsoluteUri.EndsWith("/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx", StringComparison.Ordinal));
        Assert.Contains(files, f => f.FileName == "vocab.txt");
        Assert.True(EmbeddedModel.MiniLM.UsesWordPiece);
        Assert.False(EmbeddedModel.BgeM3.UsesWordPiece);
    }

    /// <summary>A model is found by its name, ignoring case.</summary>
    [Fact]
    public void ModelIsFoundByName()
    {
        Assert.Same(EmbeddedModel.MiniLM, EmbeddedModel.FromName("ALL-MINILM-L6-V2"));
        Assert.Null(EmbeddedModel.FromName("nope"));
    }
}
