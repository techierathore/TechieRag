using System.Globalization;
using TechieDesk.Services.Agents;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.Appearance;
using TechieDesk.Services.Branding;
using TechieDesk.Services.Data;
using TechieDesk.Services.Localization;
using TechieDesk.Services.Speech;
using TechieDesk.Services.Storage;
using TechieDesk.Services.Support;
using TechieDesk.Services.Threads;
using TechieDesk.Services.Workspaces;
using TechieDesk.Tests.Support;
using TechieDeskDb;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-055 (BRD-91): the LEAF services — the small, single-purpose ones nothing else localizes —
/// hand a resource KEY to the screen, and the values they persist or put on the wire do not move
/// with the reader's language.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this covers that <see cref="ServiceStringCoverageTests"/> does not.</b> That class counts
/// English literals and holds a ratchet; counting cannot tell a user-visible label from a MIME type,
/// and it says nothing about whether a key actually resolves. These tests take the other half: every
/// key this slice returns is resolved through the REAL localizer in BOTH shipped languages, and every
/// value that is stored, matched or exported is asserted byte-identical across those same two
/// cultures.
/// </para>
/// <para>
/// <b>The three judgement calls are asserted, not just written down.</b> The export stays English
/// (<see cref="TheExportedTranscriptIsByteIdenticalInEveryCulture"/>), the language picker keeps its
/// endonyms (<see cref="EveryLanguageIsStillNamedInItself"/>), and the spoken interjections are only
/// translated when a caller says the synthesiser can pronounce them
/// (<see cref="SpokenTextFallsBackToEnglishWhenNoVoiceCanSayIt"/>). A future edit that quietly
/// reverses one of those decisions fails here rather than being discovered on a user's machine.
/// </para>
/// </remarks>
public sealed class LeafServiceStringTests
{
    /// <summary>Every key the leaf services return, driven off the services themselves.</summary>
    /// <returns>The keys, with duplicates left in for the caller to distinct.</returns>
    /// <remarks>
    /// Driven off <c>AccentPalette.All</c> and the enum rather than a hand-written list, so a sixth
    /// accent or a new promo-code outcome is covered the day it is added rather than the day somebody
    /// remembers to extend this array.
    /// </remarks>
    private static IReadOnlyList<string> LeafServiceKeys()
    {
        var keys = new List<string>
        {
            UploadTypePolicy.NoFileNameKey,
            UploadTypePolicy.NoExtensionKey,
            UploadTypePolicy.UnsupportedTypeKey,
            BrandingLogo.WrongTypeKey,
            BrandingLogo.EmptyKey,
            BrandingLogo.TooLargeKey,
            BrandingSettings.DefaultWelcomeMessageKey,
            FileManagerReveal.NoPathKey,
            FileManagerReveal.NothingThereKey,
            FileManagerReveal.NotStartedKey,
            FileManagerReveal.RevealedKey,
            FileManagerReveal.LauncherFailedKey,
            SpeechText.CodeBlockPlaceholderKey,
            SpeechText.TruncationNoticeKey,
            AgentDefinition.BuiltInDisplayNameKey,
            AgentDefinition.BuiltInDescriptionKey
        };

        keys.AddRange(AccentPalette.All.Select(accent => accent.DisplayNameKey));
        keys.AddRange(Enum.GetValues<PromoCodeFormat>()
            .Select(PromoCodeValidator.DescribeFailure)
            .Where(failure => failure is not null)
            .Select(failure => failure!.MessageKey));

        return keys;
    }

    /// <summary>
    /// Every key the leaf services return is present in the culture's OWN resource file and resolves
    /// to something other than its own name.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The culture's own key set rather than "it resolved", for the reason
    /// <see cref="ResourceHarness.OwnKeys"/> records: a key present in English and missing from Hindi
    /// resolves to the ENGLISH value with <c>ResourceNotFound</c> false, which is an English string on
    /// a Hindi screen and is the whole defect.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryLeafServiceKeyResolvesInBothLanguages(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var keys = LeafServiceKeys().Distinct(StringComparer.Ordinal).ToArray();

        // A guard against the guard: an emptied table would make every assertion below vacuous.
        Assert.True(keys.Length >= 24, $"Only {keys.Length} leaf-service keys were collected.");

        var own = resources.OwnKeys;

        foreach (var key in keys)
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by a leaf service but missing from the {culture} resources, so " +
                $"whatever renders it shows English (or the key name) in a {culture} window.");

            var value = resources.Require(key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// The Hindi resources actually carry Hindi, not a copy of the English.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryLeafServiceKeyResolvesInBothLanguages"/> passes for an <c>.hi.resx</c> entry
    /// whose value was pasted straight from the English file — the key is present, it resolves, and
    /// it is not the key name. That is exactly what a half-finished translation looks like, and it is
    /// the state this requirement exists to leave behind. Devanagari is the cheapest honest proof the
    /// string was translated rather than copied.
    /// </remarks>
    [Fact]
    public void TheHindiResourcesCarryDevanagariForEveryLeafKey()
    {
        using var resources = new ResourceHarness("hi");

        foreach (var key in LeafServiceKeys().Distinct(StringComparer.Ordinal))
        {
            var value = resources.Require(key);

            Assert.True(
                value.Any(character => character is >= 'ऀ' and <= 'ॿ'),
                $"The Hindi value for '{key}' is \"{value}\", which carries no Devanagari at all — " +
                "it looks like the English string was copied into AppStrings.hi.resx rather than " +
                "translated.");
        }
    }

    /// <summary>
    /// The leaf services' WIRE vocabulary is byte-identical whatever culture the app runs in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The central risk of this requirement, asserted directly. Accent keys are persisted by
    /// REQ-UI-038 and read back by <see cref="AccentPalette.Resolve"/>; the upload accept filter is
    /// handed to the OS file picker; the logo MIME types and the <c>data:</c> prefix end up in an
    /// <c>src</c> attribute and are matched on the way out of the database; the built-in agent handle
    /// is what <c>@agent</c> parses to; the branding defaults are compared byte-for-byte by
    /// <c>BrandingSettings.IsCustomised</c>.
    /// </para>
    /// <para>
    /// The trap is not hypothetical: <c>QdrantAdmin</c>'s daemon endpoint kind was once a string that
    /// WAS its English label and was parsed back to build the endpoint.
    /// </para>
    /// </remarks>
    [Fact]
    public void WireVocabularyIsTheSameInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = LeafWireVocabulary();
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, LeafWireVocabulary());
        }

        // And it is still the vocabulary the rest of the system expects, not merely a stable one.
        Assert.Contains("indigo", english);
        Assert.Contains("image/svg+xml", english);
        Assert.Contains("agent", english);
        Assert.Contains("Welcome! Ask anything about your documents.", english);
    }

    /// <summary>
    /// A logo encoded on a Hindi install is byte-identical to one encoded on an English install.
    /// </summary>
    /// <remarks>
    /// The stored value is a <c>data:</c> URI that goes into an <c>&lt;img src&gt;</c> and is
    /// re-validated by <see cref="BrandingLogo.IsAcceptable"/> every time it is read back. A culture
    /// that moved so much as the case of the scheme would make yesterday's logo fail today's check
    /// and blank the brand mark.
    /// </remarks>
    [Fact]
    public void AnEncodedLogoIsTheSameInEveryCulture()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        string? english;
        using (new ResourceHarness("en"))
        {
            Assert.True(BrandingLogo.TryEncode("mark.png", "image/png", content, out english, out _));
        }

        using (new ResourceHarness("hi"))
        {
            Assert.True(BrandingLogo.TryEncode("mark.png", "image/png", content, out var hindi, out _));
            Assert.Equal(english, hindi);
            Assert.True(BrandingLogo.IsAcceptable(english));
        }
    }

    /// <summary>
    /// A promo code normalizes to the same string in every culture, Turkish included.
    /// </summary>
    /// <remarks>
    /// Turkish is in here rather than Hindi because it is the culture that BREAKS a careless
    /// upper-case: <c>"i".ToUpper()</c> under <c>tr-TR</c> is <c>"İ"</c>, so a validator that used
    /// the ambient culture would post a code the payment service has never heard of and the user
    /// would be told their correctly typed code does not exist. TechieDesk does not ship Turkish, but
    /// the OS supplies the culture and <c>CurrentCulture</c> is not something this app fully controls.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("tr-TR")]
    public void PromoCodeNormalizationIsCultureInvariant(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal(PromoCodeFormat.Valid, PromoCodeValidator.Normalize("india-2026", out var code));
            Assert.Equal("INDIA-2026", code);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// An exported transcript is byte-identical whatever language the app is running in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This asserts a DECISION, not an accident.</b> The export is a file the user saves and may
    /// send on; it outlives the language setting that produced it and its reader is not necessarily
    /// its author. The Markdown scaffolding and the JSON property names therefore stay invariant
    /// English, while the user's own titles, questions, answers and cited snippets are carried
    /// through verbatim — so a Hindi thread still exports as a Hindi thread.
    /// </para>
    /// <para>
    /// If a later requirement decides a Hindi user should get a Hindi export, this test is the thing
    /// that has to be deliberately rewritten, which is the point of writing it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExportedTranscriptIsByteIdenticalInEveryCulture()
    {
        var thread = new ConversationThread
        {
            ThreadId = "thread-55",
            UserId = "user-1",
            Title = "वित्तीय रिपोर्ट",
            WorkspaceId = "ws-1",
            CreatedAt = new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 2, 9, 30, 0, DateTimeKind.Utc)
        };

        var messages = new List<StoredChatMessage>
        {
            new()
            {
                ThreadId = "thread-55",
                Role = "user",
                Content = "पिछली तिमाही का राजस्व क्या था?",
                CreatedAt = new DateTime(2026, 8, 2, 9, 1, 0, DateTimeKind.Utc)
            },
            new()
            {
                ThreadId = "thread-55",
                Role = "assistant",
                Content = null,
                CreatedAt = new DateTime(2026, 8, 2, 9, 2, 0, DateTimeKind.Utc)
            }
        };

        var exporter = new ThreadExporter();

        string englishMarkdown;
        string englishJson;
        string englishFileName;
        using (new ResourceHarness("en"))
        {
            englishMarkdown = exporter.ToMarkdown(thread, messages);
            englishJson = exporter.ToJson(thread, messages);
            englishFileName = exporter.BuildFileName(thread, "md");
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(englishMarkdown, exporter.ToMarkdown(thread, messages));
            Assert.Equal(englishJson, exporter.ToJson(thread, messages));
            Assert.Equal(englishFileName, exporter.BuildFileName(thread, "md"));
        }

        // The scaffolding is English and the user's own words are not, which is the whole claim.
        Assert.Contains("- **Thread ID:** thread-55", englishMarkdown, StringComparison.Ordinal);
        Assert.Contains("वित्तीय रिपोर्ट", englishMarkdown, StringComparison.Ordinal);
        Assert.Contains("पिछली तिमाही का राजस्व क्या था?", englishMarkdown, StringComparison.Ordinal);
        Assert.Contains("\"threadId\"", englishJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each offered language is still named in ITSELF, and the English names never reach a screen.
    /// </summary>
    /// <remarks>
    /// REQ-UI-039's convention, asserted so a well-meaning "finish the localization" pass cannot undo
    /// it. Somebody stranded in a UI they cannot read is hunting the picker for "हिन्दी"; translating
    /// that endonym into the language they are already lost in removes the only string on the screen
    /// they were looking for.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryLanguageIsStillNamedInItself(string culture)
    {
        using (new ResourceHarness(culture))
        {
            Assert.Equal("English", SupportedLanguages.Resolve("en").NativeName);
            Assert.Equal("हिन्दी", SupportedLanguages.Resolve("hi").NativeName);

            // The regional culture the OS actually reports resolves to the same neutral entry.
            Assert.Equal("हिन्दी", SupportedLanguages.Resolve("hi-IN").NativeName);
        }
    }

    /// <summary>
    /// The spoken interjections are English unless a caller says the synthesiser can pronounce them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The judgement call, asserted.</b> <c>MauiReadAloudService</c> speaks through
    /// <c>AVSpeechSynthesizer</c>, and an utterance with no locale uses
    /// <c>AVSpeechSynthesisVoice.CurrentLanguageCode</c> — the language of macOS, not the language of
    /// TechieDesk, which is a database row. A Hindi TechieDesk on an <c>en-US</c> Mac with no
    /// Devanagari voice installed would hand Hindi to an English voice, which skips the characters
    /// instead of speaking them. Silence where a sentence belongs is worse than English.
    /// </para>
    /// <para>
    /// So the default overload — the one used when
    /// <see cref="IReadAloudService.CanSpeakAsync"/> says no — speaks the invariant English EVEN IN A
    /// HINDI PROCESS. That is the assertion below, and it is the opposite of what every other test in
    /// this file asserts, on purpose.
    /// </para>
    /// </remarks>
    [Fact]
    public void SpokenTextFallsBackToEnglishWhenNoVoiceCanSayIt()
    {
        using var resources = new ResourceHarness("hi");

        var spoken = SpeechText.ForSpeech("Here:\n```\nvar x = 1;\n```\n");

        Assert.Contains(SpeechText.CodeBlockPlaceholder, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain(resources.Require(SpeechText.CodeBlockPlaceholderKey), spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// The spoken interjections ARE translated once a caller has confirmed a voice exists.
    /// </summary>
    /// <remarks>
    /// The other half of the gate. Both interjections are covered — the code-block stand-in and the
    /// truncation notice — because they are produced by two different code paths and a substitution
    /// that reached only one of them would leave a listener hearing half a language.
    /// </remarks>
    [Fact]
    public void SpokenTextUsesTheReadersLanguageWhenAVoiceCanSayIt()
    {
        using var resources = new ResourceHarness("hi");

        var placeholder = resources.Require(SpeechText.CodeBlockPlaceholderKey);
        var notice = resources.Require(SpeechText.TruncationNoticeKey);

        var withCode = SpeechText.ForSpeech("Here:\n```\nvar x = 1;\n```\n", resources.Localize);
        Assert.Contains(placeholder, withCode, StringComparison.Ordinal);
        Assert.DoesNotContain(SpeechText.CodeBlockPlaceholder, withCode, StringComparison.Ordinal);

        var longAnswer = new string('क', SpeechText.MaxSpokenCharacters + 400);
        var truncated = SpeechText.ForSpeech(longAnswer, resources.Localize);
        Assert.EndsWith(notice, truncated, StringComparison.Ordinal);
        Assert.DoesNotContain(SpeechText.TruncationNotice, truncated, StringComparison.Ordinal);
    }

    /// <summary>
    /// The spoken Hindi is a SENTENCE, not a transliterated fragment or a bare noun.
    /// </summary>
    /// <remarks>
    /// A synthesiser reads what it is given with no visual context to lean on, so a spoken string has
    /// to stand alone as speech. Devanagari plus a terminating danda is the cheapest structural proof
    /// available in a unit test that somebody wrote a spoken sentence rather than pasting a UI label;
    /// whether it SOUNDS right is a live-smoke question and is recorded as one.
    /// </remarks>
    [Fact]
    public void TheSpokenHindiReadsAsASpokenSentence()
    {
        using var resources = new ResourceHarness("hi");

        foreach (var key in new[] { SpeechText.CodeBlockPlaceholderKey, SpeechText.TruncationNoticeKey })
        {
            var value = resources.Require(key).Trim();

            Assert.EndsWith("।", value, StringComparison.Ordinal);
            Assert.DoesNotContain('{', value);
            Assert.DoesNotContain('*', value);
            Assert.DoesNotContain('`', value);
        }
    }

    /// <summary>
    /// The truncation notice keeps the leading space that joins it to the sentence before it.
    /// </summary>
    /// <remarks>
    /// <c>SpeechText.Truncate</c> appends the notice to a <c>TrimEnd</c>ed body, so the space has to
    /// come from the resource. Without it a synthesiser is handed "…on screen.बाकी उत्तर" as one
    /// token and runs the two sentences together.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void TheTruncationNoticeKeepsItsLeadingSpace(string culture)
    {
        using var resources = new ResourceHarness(culture);

        Assert.StartsWith(" ", resources.Require(SpeechText.TruncationNoticeKey), StringComparison.Ordinal);
    }

    /// <summary>
    /// The built-in agent's shipped wording is translated; a name the operator typed is not.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentDefinition.DisplayName"/> is a persisted, user-entered column. Translating the
    /// field wholesale would rewrite somebody's own agent name for them, so only the untouched
    /// shipped wording is TechieDesk's to translate — which is the distinction
    /// <see cref="AgentDefinition.HasShippedBuiltInWording"/> draws and this asserts.
    /// </remarks>
    [Fact]
    public void OnlyTheShippedBuiltInWordingIsTranslated()
    {
        var builtIn = AgentDefinition.BuiltIn("ws-1", new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(builtIn.HasShippedBuiltInWording);
        Assert.Equal(AgentDefinition.BuiltInDisplayName, builtIn.DisplayName);

        builtIn.DisplayName = "मेरा सहायक";
        Assert.False(builtIn.HasShippedBuiltInWording);

        var typed = AgentDefinition.BuiltIn("ws-1", DateTime.UtcNow);
        typed.IsBuiltIn = false;
        Assert.False(typed.HasShippedBuiltInWording);
    }

    /// <summary>
    /// An accent's resource key travels with it instead of being rebuilt from the persisted key.
    /// </summary>
    /// <remarks>
    /// <c>AppearancePanel</c> used to derive the resource key as <c>"Accent" + Capitalise(Key)</c>,
    /// which made the PERSISTED identifier half of a resource contract: an accent whose key did not
    /// capitalise cleanly, or a resource somebody renamed, would have put the raw key on screen with
    /// nothing failing. The two now travel together and are asserted to disagree in shape, so a
    /// future re-derivation is visibly wrong.
    /// </remarks>
    [Fact]
    public void AccentKeysAndResourceKeysAreSeparateThings()
    {
        foreach (var accent in AccentPalette.All)
        {
            Assert.True(accent.Key.All(char.IsAsciiLetterLower), $"'{accent.Key}' is persisted; keep it lower-case ASCII.");
            Assert.NotEqual(accent.Key, accent.DisplayNameKey);
            Assert.StartsWith("Accent", accent.DisplayNameKey, StringComparison.Ordinal);
        }

        // Resolve still answers on the persisted key, not on the resource key.
        Assert.Equal(AccentPalette.DefaultKey, AccentPalette.Resolve("indigo").Key);
        Assert.Equal(AccentPalette.DefaultKey, AccentPalette.Resolve("AccentIndigo").Key);
    }

    /// <summary>
    /// A file-manager reveal reports a KEY and its values, never an assembled sentence.
    /// </summary>
    /// <remarks>
    /// Six surfaces render this outcome. Asserting the arguments rather than the prose is what lets
    /// each of them format it in its own language while the path inside stays the literal path.
    /// </remarks>
    [Fact]
    public void RevealOutcomesCarryKeysAndValues()
    {
        var missing = Path.Combine(Path.GetTempPath(), "techiedesk-req-ui-055-absent");

        var outcome = FileManagerReveal.Reveal(DataDirectoryPlatform.MacOS, missing);

        Assert.False(outcome.Launched);
        Assert.Equal(FileManagerReveal.NothingThereKey, outcome.MessageKey);

        using var resources = new ResourceHarness("hi");
        var rendered = resources.Require(outcome.MessageKey, outcome.Arguments);

        Assert.Contains(missing, rendered, StringComparison.Ordinal);
        Assert.NotEqual(outcome.MessageKey, rendered);
    }

    /// <summary>Collects every leaf-service value that is persisted, sent, matched or exported.</summary>
    /// <returns>The leaf wire vocabulary, in a stable order.</returns>
    private static string[] LeafWireVocabulary() =>
    [
        .. AccentPalette.All.Select(accent => accent.Key),
        AccentPalette.DefaultKey,
        .. BrandingLogo.AllowedContentTypes,
        .. BrandingLogo.AllowedExtensions,
        UploadTypePolicy.AcceptTypes,
        BrandingSettings.DefaultProductName,
        BrandingSettings.DefaultWelcomeMessage,
        BrandingSettings.DefaultFooterLinks,
        AgentDefinition.BuiltInHandle,
        AgentDefinition.BuiltInDisplayName,
        AgentDefinition.BuiltInDescription,
        SupportDiagnostics.Heading,
        DocumentSizeDisplay.Unknown,
        DocumentSizeDisplay.Format(1536),
        .. SupportedLanguages.All.Select(language => language.Culture),
        .. SupportedLanguages.All.Select(language => language.NativeName),
        .. SupportedLanguages.All.Select(language => language.EnglishName)
    ];
}
