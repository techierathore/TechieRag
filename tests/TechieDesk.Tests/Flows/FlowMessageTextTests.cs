using System.Globalization;
using System.Reflection;
using System.Resources;
using TechieDesk.Resources;
using TechieDesk.Services.Flows;
using TechieRag.Orchestration;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// The app-side reader for the library's flow message codes (REQ-UI-058 / REQ-RAG-050 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>What went wrong without this.</b> <c>REQ-RAG-050</c> taught the library to emit a stable
/// code plus arguments instead of a finished English sentence, and then nothing read it — so a Hindi
/// user still met English at the exact moment a flow refused something. The library half was done,
/// tested, and completely invisible. These tests cover the half that reaches a person.</para>
/// </remarks>
public sealed class FlowMessageTextTests
{
    private static readonly ResourceManager Resources =
        new("TechieDesk.Resources.AppStrings", typeof(AppStrings).Assembly);

    /// <summary>
    /// EVERY code the library defines has a resource key — checked by reflection, so a library
    /// upgrade that adds one cannot silently ship as English.
    /// </summary>
    /// <remarks>
    /// This is the test that keeps the feature honest over time. The mapping was hand-written once;
    /// without a reflective guard the twenty-fifth code would be added upstream, fall through to the
    /// English fallback, and nobody would notice until a screenshot in another language showed it —
    /// which is precisely how this requirement came to exist.
    /// </remarks>
    [Fact]
    public void EveryLibraryCodeHasAResourceKey()
    {
        var missing = LibraryCodes()
            .Where(code => FlowMessageText.ResourceKey(code) is null)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"FlowMessageCodes with no app-side mapping: {string.Join(", ", missing)}");
    }

    /// <summary>Every mapped key actually exists in BOTH shipped languages.</summary>
    /// <remarks>
    /// A key that maps to nothing resolves to the key name itself, which renders as a raw token like
    /// <c>FlowMsgSubFlowBlocked</c> on screen — worse than the English it replaced.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryMappedKeyExistsInBothLanguages(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        var missing = LibraryCodes()
            .Select(FlowMessageText.ResourceKey)
            .OfType<string>()
            .Where(key => string.IsNullOrWhiteSpace(Resources.GetString(key, culture)))
            .ToList();

        Assert.True(missing.Count == 0, $"Keys absent from AppStrings.{language}: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Every mapped translation has a placeholder for each argument the library supplies.
    /// </summary>
    /// <remarks>
    /// A Hindi sentence that dropped <c>{1}</c> would render "Blocked by guardrail 'host-egress':"
    /// with the reason silently missing — grammatical, plausible, and useless. Counting holes is the
    /// only way to catch that without reading every string.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryTranslationKeepsItsPlaceholders(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var complaints = new List<string>();

        foreach (var (code, expected) in ExpectedPlaceholderCounts())
        {
            var key = FlowMessageText.ResourceKey(code)!;
            var text = Resources.GetString(key, culture) ?? string.Empty;

            for (var hole = 0; hole < expected; hole++)
            {
                if (!text.Contains($"{{{hole}}}", StringComparison.Ordinal))
                {
                    complaints.Add($"{key} ({language}) is missing {{{hole}}}");
                }
            }
        }

        Assert.True(complaints.Count == 0, string.Join("; ", complaints));
    }

    /// <summary>A coded message renders through the localizer, with its arguments substituted.</summary>
    [Fact]
    public void ACodedMessageRendersThroughTheLocalizer()
    {
        var message = FlowMessage.Create(
            FlowMessageCodes.AgentUnavailable, "Agent '{0}' is not available on this host.", "researcher");

        var rendered = FlowMessageText.Resolve(message, "fallback", Localize);

        Assert.Equal("[FlowMsgAgentUnavailable|researcher]", rendered);
    }

    /// <summary>
    /// A guardrail whose reason this app owns has that NESTED reason localized too, not just the
    /// framing around it.
    /// </summary>
    /// <remarks>
    /// The acceptance clause that is easy to half-do. A refusal is "this framing, around that
    /// guardrail's reason": translating only the framing leaves the clause a user actually cares
    /// about — <i>why</i> — in English, inside an otherwise translated sentence.
    /// </remarks>
    [Fact]
    public void ANestedGuardrailReasonIsLocalizedAsWell()
    {
        var message = FlowMessage.Create(
            FlowMessageCodes.ToolCallRefusedByGuardrail,
            "Blocked by guardrail '{0}': {1}",
            FlowGuardrailCatalog.NoCredentialsInOutputId,
            "the English reason the library composed");

        var rendered = FlowMessageText.Resolve(message, null, Localize);

        // Argument 1 became the catalogue's OWN key, not the library's English.
        Assert.Contains(FlowGuardrailCatalog.NoCredentialsBlockReasonKey, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("the English reason", rendered, StringComparison.Ordinal);
    }

    /// <summary>A guardrail this app does not own keeps the reason it was given.</summary>
    /// <remarks>
    /// A host guardrail supplies plain prose under an id the catalogue never heard of. Those words
    /// are the host's own choice and must not be replaced by a guess.
    /// </remarks>
    [Fact]
    public void AnUnknownGuardrailKeepsItsSuppliedReason()
    {
        var message = FlowMessage.Create(
            FlowMessageCodes.ToolCallRefusedByGuardrail,
            "Blocked by guardrail '{0}': {1}",
            "host-egress",
            "approval was declined");

        var rendered = FlowMessageText.Resolve(message, null, Localize);

        Assert.Contains("approval was declined", rendered, StringComparison.Ordinal);
    }

    /// <summary>An unknown code falls back to the library's own English, not to a blank or a token.</summary>
    /// <remarks>
    /// The degradation path for a library newer than this build. Blank would erase the row and a raw
    /// code would be gibberish; English is merely untranslated, which is recoverable.
    /// </remarks>
    [Fact]
    public void AnUnknownCodeFallsBackToTheLibrarysEnglish()
    {
        var message = FlowMessage.Create("SomeCodeFromANewerLibrary", "A sentence from the future.");

        Assert.Equal("A sentence from the future.", FlowMessageText.Resolve(message, "fallback", Localize));
    }

    /// <summary>No message at all falls through to the stored English beside it.</summary>
    [Fact]
    public void NoMessageFallsThroughToTheStoredText()
    {
        Assert.Equal("the old english column", FlowMessageText.Resolve(null, "the old english column", Localize));
    }

    /// <summary>Reflects over the library's public code constants.</summary>
    /// <returns>Every declared code value.</returns>
    private static IEnumerable<string> LibraryCodes() =>
        typeof(FlowMessageCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: var type } && type == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    /// <summary>How many holes each code's sentence takes, per the library's own format strings.</summary>
    /// <returns>Code and expected placeholder count.</returns>
    private static IEnumerable<(string Code, int Holes)> ExpectedPlaceholderCounts() =>
    [
        (FlowMessageCodes.GuardrailFaulted, 2),
        (FlowMessageCodes.GuardrailResolverMissing, 1),
        (FlowMessageCodes.GuardrailUnresolvable, 1),
        (FlowMessageCodes.NodeBlockedByGuardrail, 2),
        (FlowMessageCodes.ToolCallBlockedByGuardrail, 2),
        (FlowMessageCodes.ToolCallRefusedByGuardrail, 2),
        (FlowMessageCodes.FlowNotValidated, 1),
        (FlowMessageCodes.StepBudgetExhausted, 2),
        (FlowMessageCodes.StepBudgetReached, 1),
        (FlowMessageCodes.AgentUnavailable, 1),
        (FlowMessageCodes.AgentUnresolvable, 1),
        (FlowMessageCodes.AgentStepFailed, 1),
        (FlowMessageCodes.ToolHandlerMissing, 1),
        (FlowMessageCodes.RouteToNode, 2),
        (FlowMessageCodes.HandoffNoVariables, 2),
        (FlowMessageCodes.HandoffCarryingVariables, 3),
        (FlowMessageCodes.SubFlowBlocked, 3),
        (FlowMessageCodes.SubFlowStepBudgetExhausted, 2),
        (FlowMessageCodes.SubFlowFailed, 2),
        (FlowMessageCodes.SubFlowInvocationLimitReached, 2)
    ];

    /// <summary>A localizer that reveals which key and arguments were used.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">The values its holes take.</param>
    /// <returns>A marker naming both, so a test can assert on the resolution rather than the wording.</returns>
    private static string Localize(string key, params object?[] arguments) =>
        arguments.Length == 0 ? $"[{key}]" : $"[{key}|{string.Join("|", arguments)}]";
}
