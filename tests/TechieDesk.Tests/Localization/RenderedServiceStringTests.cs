using TechieDesk.Services.Localization;
using TechieDesk.Services.Speech;
using TechieDesk.Services.Support;
using TechieDesk.Services.Web;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-055 (BRD-91): the service sentences a SCREEN renders — the "Add from web" run, the support
/// attachment refusals, the chat export toast, the agent editor's refusals and the mic button's
/// permission hint — resolve in both shipped languages, and the data inside them does not move with
/// the reader's culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this covers that the ratchet does not.</b>
/// <see cref="ServiceStringCoverageTests.TheServiceLayerNeverGrowsMoreEnglish"/> counts English
/// literals. Counting cannot tell whether the key that replaced one exists, whether Hindi carries it,
/// or whether a <c>{0}</c> survived translation — and a key that resolves to its own name renders as
/// <c>WebSummaryIngested</c> on the screen with every count still green. These tests take that half.
/// </para>
/// <para>
/// <b>The keys are driven off the real call, not off a list.</b> Every key below is collected by
/// running the actual service method through a recording localizer, so a branch that is later
/// re-worded back to an English literal stops contributing a key and fails the population guard,
/// rather than passing because a hand-written array still names the old key.
/// </para>
/// <para>
/// <b>What this does NOT cover.</b> Whether the Hindi is any GOOD. It is agent-produced, and
/// Devanagari is the cheapest honest proof it was translated rather than copied — not proof it reads
/// well. It also says nothing about the service literals deliberately left in English: model-facing
/// skill descriptions, log templates, the support payload posted to AppManager, the exported
/// transcript, and the persisted settings-change names.
/// </para>
/// </remarks>
public sealed class RenderedServiceStringTests
{
    /// <summary>A file size comfortably past the attachment cap.</summary>
    private const long OversizeBytes = SupportAttachmentPolicy.MaxFileSizeBytes + 1;

    /// <summary>
    /// Runs every rendered path in one culture and reports which resource keys it asked for.
    /// </summary>
    /// <param name="resources">The harness resolving the keys.</param>
    /// <returns>Every key the services asked for, in call order, duplicates included.</returns>
    /// <remarks>
    /// Deliberately exercises the FAILING arm of each rule. The happy path returns null or a bare
    /// count and names no key, so a test that only asked for successes would collect nothing and
    /// assert over an empty set.
    /// </remarks>
    private static IReadOnlyList<string> KeysAskedForBy(ResourceHarness resources)
    {
        var asked = new List<string>();
        LocalizeText recording = (key, arguments) =>
        {
            asked.Add(key);
            return resources.Localize(key, arguments);
        };

        // Add from web: every validation refusal the card can show.
        Request(WebIngestionSource.Page, string.Empty).Validate(recording);
        Request(WebIngestionSource.Page, "https://example.com", workspaceId: string.Empty).Validate(recording);
        Request(WebIngestionSource.Video, "https://example.com/not-youtube").Validate(recording);
        Request(WebIngestionSource.Page, "example.com").Validate(recording);
        Request(WebIngestionSource.Page, "http://192.168.1.10/wiki").Validate(recording);
        Request(WebIngestionSource.Site, "https://example.com", depth: 99).Validate(recording);
        Request(WebIngestionSource.Site, "https://example.com", pages: 0).Validate(recording);

        // Add from web: all four run summaries, including both pluralizations of each count.
        Outcome(0, 0).SummaryText(recording);
        Outcome(0, 1).SummaryText(recording);
        Outcome(0, 4).SummaryText(recording);
        Outcome(1, 0).SummaryText(recording);
        Outcome(4, 0).SummaryText(recording);
        Outcome(4, 2).SummaryText(recording);

        // Support: every attachment refusal the drop zone can raise.
        SupportAttachmentPolicy.GetRejectionReason(" ", 2048, recording);
        SupportAttachmentPolicy.GetRejectionReason("payload.exe", 2048, recording);
        SupportAttachmentPolicy.GetRejectionReason("empty.png", 0, recording);
        SupportAttachmentPolicy.GetRejectionReason("huge.png", OversizeBytes, recording);

        // Keys the screens name directly rather than reaching through a service call.
        asked.Add(SupportAttachmentPolicy.LimitsSummaryKey);
        asked.Add(DictationSession.DeniedHintKey);
        asked.Add(UnsupportedDictationService.ReasonKey);

        return asked;
    }

    /// <summary>
    /// Every key these services ask for is present in the culture's OWN resource file.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The culture's own key set rather than "it resolved", for the reason
    /// <see cref="ResourceHarness.OwnKeys"/> records: a key present in English and missing from
    /// Hindi resolves to the ENGLISH value with <c>ResourceNotFound</c> false, which is an English
    /// sentence on a Hindi screen and is the whole defect.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryRenderedServiceKeyResolvesInBothLanguages(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var keys = KeysAskedForBy(resources).Distinct(StringComparer.Ordinal).ToArray();

        // A guard against the guard: a service re-worded back to English literals would stop naming
        // keys, and every assertion below would then pass over an empty set.
        Assert.True(keys.Length >= 20, $"Only {keys.Length} rendered-service keys were collected.");

        var own = resources.OwnKeys;

        foreach (var key in keys)
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is asked for by a service that feeds a screen, but is missing from the " +
                $"{culture} resources — so that screen shows English (or the key name) in a " +
                $"{culture} window.");

            Assert.NotEqual(key, resources.Localizer[key].Value);
        }
    }

    /// <summary>
    /// The Hindi values are Hindi, not the English string pasted across.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryRenderedServiceKeyResolvesInBothLanguages"/> passes for an entry copied
    /// verbatim out of the English file — the key is present, it resolves, and it is not the key
    /// name. That is what a half-finished translation looks like, and Devanagari is the cheapest
    /// honest proof it did not happen here.
    /// </remarks>
    [Fact]
    public void TheHindiValuesCarryDevanagari()
    {
        using var resources = new ResourceHarness("hi");

        foreach (var key in KeysAskedForBy(resources).Distinct(StringComparer.Ordinal))
        {
            var value = resources.Localizer[key].Value;

            Assert.True(
                value.Any(character => character is >= 'ऀ' and <= 'ॿ'),
                $"The Hindi value for '{key}' is \"{value}\", which carries no Devanagari at all — " +
                "it looks like the English string was copied into AppStrings.hi.resx rather than " +
                "translated.");
        }
    }

    /// <summary>
    /// Every placeholder an English value carries survives into the Hindi one.
    /// </summary>
    /// <remarks>
    /// A dropped <c>{0}</c> does not throw. It renders a sentence with the count, the file name or
    /// the URL silently missing — "अटैच नहीं की जा सकती" with no file named — which reads as a
    /// rendering fault and is impossible to diagnose from the screen. The SET is compared rather
    /// than the order, because reordering is exactly what a translator is supposed to be able to do.
    /// </remarks>
    [Fact]
    public void EveryPlaceholderSurvivesTranslation()
    {
        string[] keys;
        Dictionary<string, string> english = new(StringComparer.Ordinal);

        using (var englishResources = new ResourceHarness("en"))
        {
            keys = KeysAskedForBy(englishResources).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var key in keys)
            {
                english[key] = englishResources.Localizer[key].Value;
            }
        }

        using var hindi = new ResourceHarness("hi");

        foreach (var key in keys)
        {
            var expected = Placeholders(english[key]);
            var actual = Placeholders(hindi.Localizer[key].Value);

            Assert.True(
                expected.SetEquals(actual),
                $"'{key}' carries [{string.Join(", ", expected.Order())}] in English but " +
                $"[{string.Join(", ", actual.Order())}] in Hindi, so a Hindi window renders the " +
                "sentence with data missing from it.");
        }
    }

    /// <summary>
    /// A partly-successful web run is never summarised as a clean success, in either language.
    /// </summary>
    /// <remarks>
    /// The honesty rule <c>WebIngestionOutcome.SummaryText</c> exists for, asserted through the
    /// localizer rather than against an English literal. "Ingested 20 documents" when 5 were dropped
    /// is technically true and practically a lie: the operator's next act is to go looking for
    /// content that was never added. Moving the sentence into resources must not lose that.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void APartialWebRunIsNeverSummarisedAsACleanRun(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var clean = Outcome(4, 0).SummaryText(resources.Localize);
        var partial = Outcome(4, 2).SummaryText(resources.Localize);
        var nothing = Outcome(0, 3).SummaryText(resources.Localize);

        Assert.NotEqual(clean, partial);
        Assert.NotEqual(clean, nothing);

        // The skipped count is IN the sentence, not merely implied by a different wording.
        Assert.Contains("2", partial, StringComparison.Ordinal);
        Assert.Contains("3", nothing, StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts and user data inside a translated sentence stay in Latin script and keep their bytes.
    /// </summary>
    /// <remarks>
    /// .NET's <c>hi</c> culture does not substitute Devanagari digits, and the rest of the app
    /// formats every count and clock time with the invariant culture for that reason. A file name
    /// and a URL are worse than cosmetic: the user matches them against what they dropped in, and a
    /// Hindi window that renamed them would be reporting on a file nobody has.
    /// </remarks>
    [Fact]
    public void CountsAndUserDataAreIdenticalInEveryCulture()
    {
        using (new ResourceHarness("en"))
        {
        }

        using var hindi = new ResourceHarness("hi");

        var refusal = SupportAttachmentPolicy.GetRejectionReason("secret-plans.exe", 10, hindi.Localize);
        Assert.NotNull(refusal);
        Assert.Contains("secret-plans.exe", refusal, StringComparison.Ordinal);

        var privateHost = Request(WebIngestionSource.Page, "http://192.168.1.10/wiki").Validate(hindi.Localize);
        Assert.NotNull(privateHost);
        Assert.Contains("192.168.1.10", privateHost, StringComparison.Ordinal);

        Assert.Contains("4", Outcome(4, 0).SummaryText(hindi.Localize), StringComparison.Ordinal);
    }

    /// <summary>Gets the indexed placeholders a resource value carries.</summary>
    /// <param name="value">The resource value.</param>
    /// <returns>The distinct placeholder tokens, such as <c>{0}</c>.</returns>
    private static HashSet<string> Placeholders(string value)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '{')
            {
                continue;
            }

            var close = value.IndexOf('}', index);
            if (close > index + 1 && value[(index + 1)..close].All(char.IsAsciiDigit))
            {
                found.Add(value[index..(close + 1)]);
            }
        }

        return found;
    }

    /// <summary>Builds an "Add from web" request with one field pushed out of bounds.</summary>
    /// <param name="source">The source kind.</param>
    /// <param name="url">The address as typed.</param>
    /// <param name="workspaceId">The target workspace.</param>
    /// <param name="depth">The crawl depth.</param>
    /// <param name="pages">The page budget.</param>
    /// <returns>The request.</returns>
    private static WebIngestionRequest Request(
        WebIngestionSource source,
        string url,
        string workspaceId = "ws-1",
        int depth = 1,
        int pages = 25) =>
        new()
        {
            Source = source,
            WorkspaceId = workspaceId,
            Url = url,
            MaxDepth = depth,
            MaxPages = pages,
        };

    /// <summary>Builds a web ingestion outcome with the given tallies.</summary>
    /// <param name="ingested">How many documents landed in the workspace.</param>
    /// <param name="skipped">How many source items did not.</param>
    /// <returns>The outcome.</returns>
    private static WebIngestionOutcome Outcome(int ingested, int skipped) =>
        new(
            [.. Enumerable.Range(0, ingested).Select(index =>
                new WebIngestedDocument($"doc-{index}", $"Page {index}", $"https://example.com/{index}"))],
            [.. Enumerable.Range(0, skipped).Select(index =>
                new TechieRag.Web.CrawlFailure($"https://example.com/skipped-{index}", "unreadable"))]);
}
