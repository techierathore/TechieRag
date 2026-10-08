using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests.Reranking;

/// <summary>
/// <c>RerankConfig.Enabled</c> is the default for searches, not the switch that builds a reranker, and its
/// documentation says so (REQ-RAG-098, Sevak TR-RAG-012).
/// </summary>
public sealed class RerankEnabledMeaningTests
{
    /// <summary>
    /// With <c>Enabled = false</c> and a usable source, the reranker is still built; with <c>Source = None</c>
    /// none is built. This is exactly what the XML documentation tells a consumer.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-098 EnabledFalseStillBuildsAUsableReranker")]
    public void EnabledFalseStillBuildsAUsableReranker()
    {
        var withSource = new TechieRagBuilder().WithReranker(RerankSource.Cohere, "test-key").WithRerankEnabledByDefault(false);
        Assert.False(withSource.GetConfig().Rerank.Enabled);
        Assert.NotNull(withSource.CreateReranker());

        var noSource = new TechieRagBuilder();
        noSource.GetConfig().Rerank.Enabled = false;
        Assert.Equal(RerankSource.None, noSource.GetConfig().Rerank.Source);
        Assert.Null(noSource.CreateReranker());
    }

    /// <summary>
    /// A source with no key builds nothing and raises nothing while <c>Enabled</c> is false, and throws when it
    /// is true, as the documentation states.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-098 MissingKeyFailsOnlyWhenEnabled")]
    public void MissingKeyFailsOnlyWhenEnabled()
    {
        var builder = new TechieRagBuilder();
        builder.GetConfig().Rerank.Source = RerankSource.Jina;

        builder.GetConfig().Rerank.Enabled = false;
        Assert.Null(builder.CreateReranker());

        builder.GetConfig().Rerank.Enabled = true;
        Assert.Throws<InvalidOperationException>(() => builder.CreateReranker());
    }

    /// <summary>
    /// The XML documentation shipped with the package states the changed meaning and how to build no reranker,
    /// so a consumer's IDE shows it on <c>RerankConfig.Enabled</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-098 EnabledDocumentationStatesChangedMeaning")]
    public void EnabledDocumentationStatesChangedMeaning()
    {
        var xmlPath = Path.ChangeExtension(typeof(TechieRagBuilder).Assembly.Location, ".xml");
        var member = XDocument.Load(xmlPath).Descendants("member")
            .Single(m => (string?)m.Attribute("name") == "P:TechieRag.RerankConfig.Enabled");
        var text = string.Join(" ", member.DescendantNodes().OfType<XText>().Select(t => t.Value));

        Assert.Contains("by default", text, StringComparison.Ordinal);
        Assert.Contains("Changed meaning", text, StringComparison.Ordinal);
        Assert.Contains("does not prevent it", text, StringComparison.Ordinal);
    }
}
