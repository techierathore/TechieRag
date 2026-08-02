using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieRag.Orchestration;
using Xunit;
using AppStrings = TechieDesk.Resources.AppStrings;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// The builder surfaces a validation problem against the step it is about, in the user's language,
/// keyed by the library's stable CODE (REQ-UI-040 / BRD-92).
/// </summary>
public sealed class FlowValidationSurfaceTests : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IStringLocalizer<AppStrings> localizer;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;

    /// <summary>Builds the localizer the screen resolves its strings through.</summary>
    public FlowValidationSurfaceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        provider = services.BuildServiceProvider();
        localizer = provider.GetRequiredService<IStringLocalizer<AppStrings>>();
    }

    /// <summary>
    /// An agent step with no agent chosen is reported as an ERROR carrying the code and the id of
    /// the offending step, not a message about the flow in general.
    /// </summary>
    /// <remarks>
    /// Highlighting is by <c>NodeId</c>, so a flow with four agent steps must say WHICH one is
    /// incomplete. A validator result that named only the flow would leave the user hunting.
    /// </remarks>
    [Fact]
    public void AnIncompleteAgentStepIsReportedAgainstThatStep()
    {
        var complete = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "step-good");
        complete.AgentId = "analyst";

        var incomplete = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "step-bad");
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        var flow = new FlowDefinition
        {
            Id = "flow-validate",
            Name = "Validate me",
            StartNodeId = complete.Id,
            Nodes = [complete, incomplete, end],
            Edges =
            [
                new FlowEdge { Id = "edge-1", FromNodeId = complete.Id, ToNodeId = incomplete.Id },
                new FlowEdge { Id = "edge-2", FromNodeId = incomplete.Id, ToNodeId = end.Id }
            ]
        };

        var issue = Assert.Single(
            FlowValidator.Validate(flow).Issues,
            candidate => candidate.Code == FlowValidationCodes.MissingAgentId);

        Assert.Equal(FlowValidationSeverity.Error, issue.Severity);
        Assert.Equal("step-bad", issue.NodeId);
    }

    /// <summary>
    /// A condition problem is reported against the EDGE that carries it.
    /// </summary>
    [Fact]
    public void ABrokenConditionIsReportedAgainstItsLink()
    {
        var branch = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "step-branch");
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        var flow = new FlowDefinition
        {
            Id = "flow-pattern",
            Name = "Pattern",
            StartNodeId = branch.Id,
            Nodes = [branch, end],
            Edges =
            [
                new FlowEdge
                {
                    Id = "edge-broken",
                    FromNodeId = branch.Id,
                    ToNodeId = end.Id,
                    Condition = new FlowCondition { Kind = FlowConditionKind.Matches, Operand = "([unclosed" }
                }
            ]
        };

        var issue = Assert.Single(
            FlowValidator.Validate(flow).Issues,
            candidate => candidate.Code == FlowValidationCodes.InvalidPattern);

        Assert.Equal("edge-broken", issue.EdgeId);
    }

    /// <summary>
    /// EVERY validation code the library publishes has a translation in both shipped languages.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// <para><b>Why this is reflected rather than listed.</b> <c>FlowValidationIssue.Code</c> is
    /// documented as the stable contract and the English <c>Message</c> as a fallback, so the builder
    /// translates by code. A hand-written list of codes would go stale the first time the library
    /// added one, and the symptom would be a Hindi screen printing an English sentence — invisible to
    /// every other localization test, because no <c>.razor</c> literal is missing. Reflecting over
    /// the constants makes a new library code fail HERE, on the upgrade that introduces it.</para>
    /// <para>An unresolved key renders as the key name, which is what the final assertion catches.</para>
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryValidationCodeHasATranslation(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var codes = typeof(FlowValidationCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.True(codes.Length >= 24, $"Only {codes.Length} validation codes were found by reflection.");

        var missing = codes
            .Select(code => "FlowsIssue" + code)
            .Where(key => localizer[key].ResourceNotFound || localizer[key].Value == key)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} validation code(s) have no {culture} translation, so the builder would "
            + "print the resource key: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every node kind and every guardrail the builder can offer has a translation too.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// Same argument as the codes: the palette is derived from <c>FlowNodeCatalog.Kinds</c>, so a
    /// node kind added by a future library release appears in the palette with no code change — and
    /// would appear untranslated unless this fails first.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryNodeKindHasATranslation(string culture)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var missing = FlowNodeCatalog.Kinds
            .SelectMany(kind => new[] { "FlowsKind" + kind.Kind, "FlowsKind" + kind.Kind + "Hint" })
            .Where(key => localizer[key].ResourceNotFound || localizer[key].Value == key)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} node kind string(s) have no {culture} translation: " + string.Join(", ", missing));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = originalCulture;
        provider.Dispose();
    }
}
