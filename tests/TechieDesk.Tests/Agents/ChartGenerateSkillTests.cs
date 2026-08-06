using System.Xml.Linq;
using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the <c>chart-generate</c> skill. A chart is a factual claim about data, so the
/// tests that matter are the ones proving it plots exactly what it was given and refuses rather
/// than invents when the data does not line up.
/// </summary>
public class ChartGenerateSkillTests
{
    /// <summary>The skill binds to the catalogue name the toggles and the resolver use.</summary>
    [Fact]
    public void BindsToTheCatalogueName()
    {
        Assert.Equal(SkillCatalog.ChartGenerate, ChartGenerateSkill.Create().SkillName);
    }

    /// <summary>A bar chart comes back as well-formed SVG with one rectangle per value.</summary>
    [Fact]
    public async Task ABarChartHasOneRectanglePerValue()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"type":"bar","title":"Revenue","labels":["Q1","Q2","Q3"],"values":[10,20,15]}""",
            CancellationToken.None);

        var document = XDocument.Parse(svg);
        Assert.Equal(3, document.Descendants().Count(node => node.Name.LocalName == "rect"));
        Assert.Contains("Revenue", svg, StringComparison.Ordinal);
    }

    /// <summary>A line chart draws a polyline through every point.</summary>
    [Fact]
    public async Task ALineChartDrawsAPolyline()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"type":"line","labels":["Jan","Feb"],"values":[5,9]}""", CancellationToken.None);

        var document = XDocument.Parse(svg);
        Assert.Single(document.Descendants().Where(node => node.Name.LocalName == "polyline"));
        Assert.Equal(2, document.Descendants().Count(node => node.Name.LocalName == "circle"));
    }

    /// <summary>A pie chart draws one wedge per slice.</summary>
    [Fact]
    public async Task APieChartDrawsOneWedgePerSlice()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"type":"pie","labels":["A","B","C"],"values":[1,2,3]}""", CancellationToken.None);

        var document = XDocument.Parse(svg);
        Assert.Equal(3, document.Descendants().Count(node => node.Name.LocalName == "path"));
    }

    /// <summary>An unrecognised chart type falls back to bars rather than failing the turn.</summary>
    [Fact]
    public async Task AnUnknownTypeFallsBackToBars()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"type":"sunburst","labels":["A"],"values":[1]}""", CancellationToken.None);

        Assert.Contains("<rect", svg, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mismatched labels and values are refused, never padded. A chart that quietly filled a gap
    /// would be wrong in a way the reader cannot see.
    /// </summary>
    [Fact]
    public async Task MismatchedLabelsAndValuesAreRefused()
    {
        var result = await ChartGenerateSkill.Create().Invoke(
            """{"labels":["A","B","C"],"values":[1,2]}""", CancellationToken.None);

        Assert.DoesNotContain("<svg", result, StringComparison.Ordinal);
        Assert.Contains("will not pad", result, StringComparison.Ordinal);
    }

    /// <summary>Empty or missing data is reported rather than drawn as an empty chart.</summary>
    [Theory]
    [InlineData("""{"labels":[],"values":[]}""")]
    [InlineData("{}")]
    [InlineData("not json at all")]
    public async Task MissingDataIsReportedNotDrawn(string arguments)
    {
        var result = await ChartGenerateSkill.Create().Invoke(arguments, CancellationToken.None);

        Assert.Contains("No chart was drawn", result, StringComparison.Ordinal);
    }

    /// <summary>Too many points is refused with the limit named, so the model can aggregate.</summary>
    [Fact]
    public async Task TooManyPointsIsRefused()
    {
        var labels = string.Join(",", Enumerable.Range(0, 60).Select(index => $"\"L{index}\""));
        var values = string.Join(",", Enumerable.Range(0, 60));

        var result = await ChartGenerateSkill.Create().Invoke(
            $$"""{"labels":[{{labels}}],"values":[{{values}}]}""", CancellationToken.None);

        Assert.Contains($"{ChartGenerateSkill.MaxPoints}-point limit", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Labels come from documents, so a label carrying markup is escaped rather than rendered.
    /// Treating them as trusted SVG would let a document write script into the transcript.
    /// </summary>
    [Fact]
    public async Task ALabelCarryingMarkupIsEscaped()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"labels":["<script>alert(1)</script>"],"values":[1]}""", CancellationToken.None);

        Assert.DoesNotContain("<script>", svg, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", svg, StringComparison.Ordinal);
        XDocument.Parse(svg);
    }

    /// <summary>
    /// A non-finite value is dropped rather than plotted, which then trips the label-count check —
    /// an axis scaled to infinity draws a chart that is silently wrong.
    /// </summary>
    [Fact]
    public async Task ANonNumericValueIsNotPlotted()
    {
        var result = await ChartGenerateSkill.Create().Invoke(
            """{"labels":["A","B"],"values":[1,"not a number"]}""", CancellationToken.None);

        Assert.Contains("No chart was drawn", result, StringComparison.Ordinal);
    }

    /// <summary>All-zero values still produce valid markup rather than dividing by the peak.</summary>
    [Fact]
    public async Task AllZeroValuesStillRenderValidMarkup()
    {
        var svg = await ChartGenerateSkill.Create().Invoke(
            """{"labels":["A","B"],"values":[0,0]}""", CancellationToken.None);

        XDocument.Parse(svg);
    }
}
