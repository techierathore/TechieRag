using System.Globalization;
using System.Net;
using System.Text;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The <c>chart-generate</c> catalogue skill as a library tool (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>Entirely local, which is why it can be trusted with data.</b> The chart is rendered as
/// SVG in process — no rendering service, no image host, nothing leaves the machine. That is what
/// lets a workspace enable charting without weakening REQ-NFR-008, and it is why the catalogue
/// marks this skill <see cref="SkillExposure.Local"/>.</para>
/// <para><b>It plots what it was given and nothing else.</b> The tool never invents, interpolates
/// or reorders values: a chart is a factual claim about data, and a chart tool that quietly filled
/// a gap would make the model's answer wrong in a way the reader cannot see. Mismatched labels and
/// values are refused rather than padded.</para>
/// <para><b>Every label is escaped</b> before it reaches the markup. Labels come from documents,
/// so treating them as trusted SVG would let a document write script into the chat transcript.</para>
/// </remarks>
public static class ChartGenerateSkill
{
    /// <summary>The JSON Schema for the chart-generate tool's parameters.</summary>
    public const string Schema =
        """{"type":"object","properties":{"type":{"type":"string","enum":["bar","line","pie"],"description":"Chart form","default":"bar"},"title":{"type":"string","description":"Chart title"},"labels":{"type":"array","items":{"type":"string"},"description":"One label per value"},"values":{"type":"array","items":{"type":"number"},"description":"The values to plot"}},"required":["labels","values"]}""";

    /// <summary>The description the model is shown.</summary>
    public const string Description =
        "Renders a bar, line or pie chart as inline SVG from labels and values you supply. Runs "
        + "locally and plots exactly the values given.";

    /// <summary>The most data points a single chart will plot.</summary>
    public const int MaxPoints = 40;

    private const int Width = 640;
    private const int Height = 360;
    private const int Padding = 48;

    private static readonly string[] Palette =
        ["#2563eb", "#059669", "#d97706", "#dc2626", "#7c3aed", "#0891b2"];

    /// <summary>
    /// Builds the chart-generate skill. It has no external dependency, so it is always available.
    /// </summary>
    /// <returns>The skill implementation.</returns>
    public static SkillImplementation Create() =>
        new(SkillCatalog.ChartGenerate, Description, Schema,
            (argumentsJson, _) => Task.FromResult<SkillOutcome>(Run(argumentsJson)));

    /// <summary>Runs one chart call.</summary>
    /// <param name="argumentsJson">The tool-call arguments.</param>
    /// <returns>The SVG markup, or a refusal explaining what was wrong with the data.</returns>
    private static string Run(string argumentsJson)
    {
        var labels = SkillArguments.ReadStrings(argumentsJson, "labels");
        var values = SkillArguments.ReadNumbers(argumentsJson, "values");

        if (labels.Count == 0 || values.Count == 0)
        {
            return "No chart was drawn: supply both a labels array and a values array.";
        }

        if (labels.Count != values.Count)
        {
            return $"No chart was drawn: {labels.Count} label(s) but {values.Count} value(s). "
                + "The tool plots only what it is given and will not pad the difference.";
        }

        if (labels.Count > MaxPoints)
        {
            return $"No chart was drawn: {labels.Count} points exceeds the {MaxPoints}-point limit. "
                + "Aggregate the data first.";
        }

        var title = SkillArguments.ReadString(argumentsJson, "title");
        var kind = SkillArguments.ReadString(argumentsJson, "type").Trim().ToLowerInvariant();

        return kind switch
        {
            "line" => Wrap(title, RenderLine(labels, values)),
            "pie" => Wrap(title, RenderPie(labels, values)),
            _ => Wrap(title, RenderBars(labels, values))
        };
    }

    /// <summary>Wraps rendered marks in the SVG document and its title.</summary>
    /// <param name="title">The chart title, which may be empty.</param>
    /// <param name="marks">The rendered chart body.</param>
    /// <returns>The complete SVG document.</returns>
    private static string Wrap(string title, string marks)
    {
        var heading = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"""<text x="{Width / 2}" y="26" text-anchor="middle" font-family="sans-serif" font-size="16" font-weight="600">{Escape(title)}</text>""";

        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {Width} {Height}" width="{Width}" height="{Height}" role="img">{heading}{marks}</svg>""";
    }

    /// <summary>Renders a bar chart body.</summary>
    /// <param name="labels">One label per bar.</param>
    /// <param name="values">The bar heights.</param>
    /// <returns>The SVG fragment.</returns>
    private static string RenderBars(IReadOnlyList<string> labels, IReadOnlyList<double> values)
    {
        var marks = new StringBuilder(Axis());
        var slot = (double)(Width - (Padding * 2)) / values.Count;
        var scale = Scale(values);

        for (var index = 0; index < values.Count; index++)
        {
            var height = Math.Abs(values[index]) * scale;
            var x = Padding + (slot * index) + (slot * 0.15);
            var y = Height - Padding - height;
            marks.Append(CultureInfo.InvariantCulture,
                $"""<rect x="{Round(x)}" y="{Round(y)}" width="{Round(slot * 0.7)}" height="{Round(height)}" fill="{Palette[index % Palette.Length]}"><title>{Escape(labels[index])}: {Number(values[index])}</title></rect>""");
            marks.Append(Label(Padding + (slot * index) + (slot / 2), labels[index]));
        }

        return marks.ToString();
    }

    /// <summary>Renders a line chart body.</summary>
    /// <param name="labels">One label per point.</param>
    /// <param name="values">The point values.</param>
    /// <returns>The SVG fragment.</returns>
    private static string RenderLine(IReadOnlyList<string> labels, IReadOnlyList<double> values)
    {
        var marks = new StringBuilder(Axis());
        var step = values.Count == 1 ? 0 : (double)(Width - (Padding * 2)) / (values.Count - 1);
        var scale = Scale(values);
        var points = new StringBuilder();

        for (var index = 0; index < values.Count; index++)
        {
            var x = Padding + (step * index);
            var y = Height - Padding - (Math.Abs(values[index]) * scale);
            points.Append(CultureInfo.InvariantCulture, $"{Round(x)},{Round(y)} ");
            marks.Append(CultureInfo.InvariantCulture,
                $"""<circle cx="{Round(x)}" cy="{Round(y)}" r="3.5" fill="{Palette[0]}"><title>{Escape(labels[index])}: {Number(values[index])}</title></circle>""");
            marks.Append(Label(x, labels[index]));
        }

        return $"""<polyline points="{points.ToString().Trim()}" fill="none" stroke="{Palette[0]}" stroke-width="2" />"""
            + marks;
    }

    /// <summary>Renders a pie chart body.</summary>
    /// <param name="labels">One label per slice.</param>
    /// <param name="values">The slice weights.</param>
    /// <returns>The SVG fragment.</returns>
    private static string RenderPie(IReadOnlyList<string> labels, IReadOnlyList<double> values)
    {
        var total = values.Sum(Math.Abs);
        if (total <= 0)
        {
            return """<text x="320" y="180" text-anchor="middle" font-family="sans-serif" font-size="13">No positive values to chart</text>""";
        }

        var marks = new StringBuilder();
        const double centreX = 220;
        const double centreY = 190;
        const double radius = 120;
        var angle = -Math.PI / 2;

        for (var index = 0; index < values.Count; index++)
        {
            var sweep = Math.Abs(values[index]) / total * Math.PI * 2;
            var next = angle + sweep;
            marks.Append(CultureInfo.InvariantCulture,
                $"""<path d="M {centreX} {centreY} L {Round(centreX + (radius * Math.Cos(angle)))} {Round(centreY + (radius * Math.Sin(angle)))} A {radius} {radius} 0 {(sweep > Math.PI ? 1 : 0)} 1 {Round(centreX + (radius * Math.Cos(next)))} {Round(centreY + (radius * Math.Sin(next)))} Z" fill="{Palette[index % Palette.Length]}"><title>{Escape(labels[index])}: {Number(values[index])}</title></path>""");
            marks.Append(CultureInfo.InvariantCulture,
                $"""<text x="400" y="{80 + (index * 18)}" font-family="sans-serif" font-size="12" fill="{Palette[index % Palette.Length]}">■ {Escape(labels[index])} ({Number(values[index])})</text>""");
            angle = next;
        }

        return marks.ToString();
    }

    /// <summary>Renders the baseline and left axis shared by the bar and line forms.</summary>
    /// <returns>The SVG fragment.</returns>
    private static string Axis() =>
        $"""<line x1="{Padding}" y1="{Height - Padding}" x2="{Width - Padding}" y2="{Height - Padding}" stroke="#94a3b8" stroke-width="1" /><line x1="{Padding}" y1="{Padding}" x2="{Padding}" y2="{Height - Padding}" stroke="#94a3b8" stroke-width="1" />""";

    /// <summary>Renders one x-axis label.</summary>
    /// <param name="x">Where to centre the label.</param>
    /// <param name="text">The label text.</param>
    /// <returns>The SVG fragment.</returns>
    private static string Label(double x, string text) =>
        $"""<text x="{Round(x)}" y="{Height - Padding + 16}" text-anchor="middle" font-family="sans-serif" font-size="11" fill="#475569">{Escape(text)}</text>""";

    /// <summary>Computes pixels per unit so the tallest value fills the plot area.</summary>
    /// <param name="values">The values being plotted.</param>
    /// <returns>The scale factor, or zero when every value is zero.</returns>
    private static double Scale(IReadOnlyList<double> values)
    {
        var peak = values.Max(Math.Abs);
        return peak <= 0 ? 0 : (Height - (Padding * 2)) / peak;
    }

    /// <summary>Formats a coordinate for the markup.</summary>
    /// <param name="value">The coordinate.</param>
    /// <returns>The invariant, two-decimal text.</returns>
    private static string Round(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Formats a data value for a tooltip or legend.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The invariant text.</returns>
    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Escapes text that came from a document before it reaches the markup.</summary>
    /// <param name="text">The untrusted text.</param>
    /// <returns>The XML-safe text.</returns>
    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
