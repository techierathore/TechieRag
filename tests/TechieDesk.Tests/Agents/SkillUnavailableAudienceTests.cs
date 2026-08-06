using System.Globalization;
using System.Resources;
using TechieDesk.Resources;
using TechieDesk.Services.Agents;
using TechieDesk.Services.Flows;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// A skill that cannot run tells the MODEL and the PERSON different things, on purpose
/// (REQ-UI-059 clause 3 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>The premise that was wrong, and how it was disproven.</b> Two clusters classified the
/// <c>unavailable: …</c> sentences as machine-facing and out of localization scope. The reasoning was
/// sound — do not translate what the model reads, because that changes what it is told. The premise
/// was not: the execution trace renders tool-result content <b>verbatim to the user</b>, so
/// <c>"Tool said: unavailable: 'web-scrape' sends a request off this machine…"</c> appeared in English
/// on an otherwise fully-Hindi screen. Carrying BOTH is the fix; translating the model's copy would
/// have been a correctness bug wearing a localization bug's clothes.</para>
/// </remarks>
public sealed class SkillUnavailableAudienceTests
{
    private static readonly ResourceManager Resources =
        new("TechieDesk.Resources.AppStrings", typeof(AppStrings).Assembly);

    /// <summary>The model still receives invariant English, marker and all.</summary>
    /// <remarks>
    /// The half that must NOT change. If this ever renders in the reader's language, the model is
    /// being told something different depending on who is looking at the screen.
    /// </remarks>
    [Fact]
    public void TheModelStillReceivesInvariantEnglish()
    {
        var outcome = SkillUnavailable.Coded("SkillUnavailableWebFetcher");

        Assert.StartsWith(SkillUnavailable.Marker, outcome.Text, StringComparison.Ordinal);
        Assert.Contains("no web fetcher is configured", outcome.Text, StringComparison.Ordinal);
        Assert.DoesNotContain('ऀ', outcome.Text);
    }

    /// <summary>The same refusal carries a code a trace can render in the reader's language.</summary>
    [Fact]
    public void ThePersonGetsACodeThatRendersInTheirLanguage()
    {
        var outcome = SkillUnavailable.Coded("SkillUnavailableWebFetcher");

        Assert.NotNull(outcome.Message);
        Assert.Equal("SkillUnavailableWebFetcher", outcome.Message!.Code);

        var hindi = Resources.GetString("SkillUnavailableWebFetcher", CultureInfo.GetCultureInfo("hi"));
        Assert.False(string.IsNullOrWhiteSpace(hindi));
        Assert.NotEqual(Resources.GetString("SkillUnavailableWebFetcher", CultureInfo.InvariantCulture), hindi);
    }

    /// <summary>Arguments survive into the code, so a path or tool name still reaches the sentence.</summary>
    [Fact]
    public void ArgumentsTravelWithTheCode()
    {
        var outcome = SkillUnavailable.Coded("SkillUnavailableFilesMissingArea", "/srv/workspace-files");

        Assert.Contains("/srv/workspace-files", outcome.Text, StringComparison.Ordinal);
        Assert.Equal(["/srv/workspace-files"], outcome.Message!.Arguments);
    }

    /// <summary>Every coded reason exists in BOTH shipped languages.</summary>
    /// <remarks>
    /// A key present only in English resolves to English silently — the exact outcome this clause
    /// exists to remove, reintroduced by omission rather than by design.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryCodedReasonExistsInBothLanguages(string language)
    {
        string[] keys =
        [
            "SkillUnavailableWebFetcher", "SkillUnavailableSqlNoTarget", "SkillUnavailableSqlUnreachable",
            "SkillUnavailableWebSearchNoProvider", "SkillUnavailableWebSearchBroken",
            "SkillUnavailableFilesNoArea", "SkillUnavailableFilesMissingArea",
            "SkillUnavailableEgressNoPrompt", "SkillUnavailableEgressDeclined", "SkillUnavailableMcpEgress"
        ];

        var culture = CultureInfo.GetCultureInfo(language);
        var missing = keys
            .Where(key => string.IsNullOrWhiteSpace(Resources.GetString(key, culture)))
            .ToList();

        Assert.True(missing.Count == 0, $"Absent from AppStrings.{language}: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The trace renders the coded refusal in the reader's language rather than the model's English.
    /// </summary>
    /// <remarks>
    /// The end-to-end assertion for this clause: a skill reports itself unavailable, the loop records
    /// it, and the row a person reads resolves through the localizer. Asserting on
    /// <c>SkillUnavailable</c> alone would prove the code exists, not that anything renders it.
    /// </remarks>
    [Fact]
    public void TheTraceRendersTheRefusalInTheReadersLanguage()
    {
        var outcome = SkillUnavailable.Coded("SkillUnavailableEgressDeclined", "web-search");

        var trace = new AgentTrace();
        trace.Add(new AgentStep
        {
            Iteration = 1,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = "web-search",
            Content = outcome.Text,
            IsSuccess = true,
            FailureMessage = outcome.Message
        });

        var entry = Assert.Single(trace.Entries);
        var detail = entry.DetailText((key, args) =>
            args.Length == 0 ? $"[{key}]" : $"[{key}|{string.Join("|", args)}]");

        Assert.Equal("[SkillUnavailableEgressDeclined|web-search]", detail);
    }

    /// <summary>A skill result that is DATA is untouched — there is nothing there to translate.</summary>
    /// <remarks>
    /// The boundary that keeps this honest. A fetched page or a query result belongs to the world,
    /// not to the product, and running it through a localizer would be nonsense.
    /// </remarks>
    [Fact]
    public void AnOrdinaryDataResultCarriesNoCode()
    {
        SkillOutcome outcome = "the page said: markets rose 2% today";

        Assert.Null(outcome.Message);
        Assert.Equal("the page said: markets rose 2% today", outcome.Text);
    }

    /// <summary>A provider's own reason is passed through, not replaced by ours.</summary>
    /// <remarks>
    /// <c>SqlQuerySkill</c> and <c>WebSearchSkill</c> prefer the target's <c>UnavailableReason</c>
    /// when it supplied one. Those words came from an operator's configuration and are not ours to
    /// code or translate.
    /// </remarks>
    [Fact]
    public void AnOperatorSuppliedReasonIsNotCoded()
    {
        var outcome = SkillUnavailable.Because("the reporting replica is in maintenance until 09:00");

        Assert.Null(outcome.Message);
        Assert.Contains("maintenance until 09:00", outcome.Text, StringComparison.Ordinal);
    }

    /// <summary>The resolver treats an app-authored code as its own resource key.</summary>
    [Fact]
    public void TheResolverMapsAnAppCodeToItself()
    {
        Assert.Equal(
            "SkillUnavailableMcpEgress", FlowMessageText.ResourceKey("SkillUnavailableMcpEgress"));
        Assert.Null(FlowMessageText.ResourceKey("SomethingNobodyDefined"));
    }
}
