using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Licensing;
using TechieDesk.Tests.Localization;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Licensing;

/// <summary>
/// REQ-UI-055 (BRD-91): the licensing services hand a resource KEY to the screen, never English —
/// and nothing that decides ENTITLEMENTS moves with the culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why licensing was the one that mattered.</b> <see cref="LicenseStatus"/>'s message renders in
/// the always-visible <c>MainLayout</c> banner. It was English on every screen of every Hindi
/// install the moment AppManager went unreachable, which made it the highest-visibility unlocalized
/// string left in the product after REQ-UI-051.
/// </para>
/// <para>
/// <b>The two halves, and they pull against each other.</b> Licence prose has to be translated;
/// licence VOCABULARY must not be. Plan names, AppManager status strings and feature codes are
/// matched against the licence server — <c>InstanceModeResolver</c> looks a tier up in the
/// configured maps, <c>Pricing.IsCurrent</c> prefix-matches the plan you hold — so a translated tier
/// name is a billing bug, not a cosmetic one. Half these tests exist to prove the translation
/// happened; the other half exist to prove it stopped exactly where it had to.
/// </para>
/// <para>
/// <b>Three kinds of string, three treatments</b>, each asserted below:
/// our own copy about a known state is localized with the varying parts as arguments
/// (<see cref="EveryLicensingKeyResolvesInBothShippedLanguages"/>); a wire error code is localized
/// BY CODE (<see cref="ValidationMessagesAreKeyedOffTheWireCodeNotTheEnglish"/>); and a sentence
/// AppManager composed at run time is shown verbatim inside a localized frame
/// (<see cref="ServerSuppliedReasonIsFramedRatherThanTranslated"/>).
/// </para>
/// </remarks>
public sealed class LicensingLocalizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Regex PlaceholderHole = new(@"\{(\d+)\}");

    /// <summary>
    /// The multi-word literals the licensing services still hold, and why each one is not a defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Free (offline)</c> is <b>wire vocabulary that happens to look like a label</b>. It is
    /// <see cref="LicenseStatus.Offline"/>'s <c>LicenseName</c>, and <c>Pricing.IsCurrent</c> asks
    /// whether the held licence name STARTS WITH a published tier name in order to highlight the
    /// right plan card. Translating it would stop an offline install highlighting Free, which is the
    /// precedent <c>Pricing.Tier.Name</c> already records: the matched name is invariant, the drawn
    /// text is separate. It renders as itself in the licence-card badge for the same reason every
    /// OTHER value in that badge does — they all come from the licence server, and that badge is
    /// "the plan as the server names it".
    /// </para>
    /// <para>
    /// <c>No access token after refresh</c> is a developer-facing exception message on a path that
    /// cannot reach a screen: it is thrown as an <see cref="AppManagerException"/> with status 401,
    /// caught two lines later, and answered with a message keyed off the CODE. Nobody ever reads it
    /// but whoever is reading a stack trace.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlySet<string> MachineFacingLicensingText =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Free (offline)",
            "No access token after refresh"
        };

    // -------------------------------------------------------------------------------------------
    // The keys exist, in both languages, and say something different in each.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Every key the licensing services can return resolves in the culture's OWN resource set.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// It drives <see cref="LicenseMessageKeys.All"/> rather than a hand-written list, so a key
    /// added to the licensing layer next month is covered the day it is added. A key present in
    /// English and absent from Hindi resolves to the ENGLISH value with <c>ResourceNotFound</c>
    /// false — an English sentence on a Hindi screen, and precisely the defect this REQ is about —
    /// so the culture's own key set is what is checked, not merely "it resolved".
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryLicensingKeyResolvesInBothShippedLanguages(string culture)
    {
        using var resources = new ResourceHarness(culture);
        var own = resources.OwnKeys;

        Assert.True(LicenseMessageKeys.All.Count >= 40,
            $"Only {LicenseMessageKeys.All.Count} licensing keys were collected, so this proves little.");

        foreach (var key in LicenseMessageKeys.All.Distinct(StringComparer.Ordinal))
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by a licensing service but missing from the {culture} " +
                "resources, so the shell banner shows English (or the key name) in a " +
                $"{culture} window.");

            var value = resources.Require(key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// The Hindi is actually Hindi, and it kept every placeholder the English has.
    /// </summary>
    /// <remarks>
    /// Two failures this catches that the resolve-check above cannot. A key copied into
    /// <c>AppStrings.hi.resx</c> with its English value still in it passes every existence check and
    /// renders English. And a translation that dropped <c>{1}</c> would silently lose the plan name
    /// from "your seat on the {1} licence" — or, worse, an EXTRA hole would throw
    /// <see cref="FormatException"/> inside the always-visible banner.
    /// </remarks>
    [Fact]
    public void HindiLicensingTextIsTranslatedAndKeepsEveryPlaceholder()
    {
        Dictionary<string, string> english;
        using (var enResources = new ResourceHarness("en"))
        {
            english = LicenseMessageKeys.All
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(key => key, key => enResources.Require(key), StringComparer.Ordinal);
        }

        using var hiResources = new ResourceHarness("hi");

        foreach (var (key, englishValue) in english)
        {
            var hindiValue = hiResources.Require(key);

            Assert.True(
                hindiValue.Any(character => character >= 'ऀ' && character <= 'ॿ'),
                $"'{key}' carries no Devanagari in AppStrings.hi.resx, so it was never translated.");

            Assert.Equal(
                PlaceholderHole.Matches(englishValue).Select(match => match.Groups[1].Value).Order(),
                PlaceholderHole.Matches(hindiValue).Select(match => match.Groups[1].Value).Order());
        }
    }

    // -------------------------------------------------------------------------------------------
    // The always-visible banner — the site this requirement was raised for.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The shell banner sentence, built by the REAL <see cref="LicenseService"/> during a real
    /// outage, renders in Hindi on a Hindi install.
    /// </summary>
    /// <remarks>
    /// End-to-end on purpose: fake AppManager, real grace arithmetic, real cache round-trip, real
    /// <see cref="ResourceHarness"/>. Asserting on <c>MessageKey</c> alone would prove only that a
    /// literal moved, which is the class of test REQ-UI-051 explicitly rejected.
    /// </remarks>
    [Fact]
    public async Task TheCachedLicenceBannerRendersInTheReadersLanguage()
    {
        var time = new FixedTimeProvider(Now);
        var client = new FakeAppManagerClient();
        var service = BuildLicenseService(client, time);

        client.OnValidateLicense = (_, _) => Task.FromResult(TeamLicense("Active"));
        await service.ValidateAsync();

        // AppManager goes away, inside the 72h window: this is the banner state.
        time.Advance(TimeSpan.FromHours(24));
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");
        var cached = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Cached, cached.Availability);

        string englishBanner;
        using (var enResources = new ResourceHarness("en"))
        {
            englishBanner = cached.Describe(enResources.Localize);
        }

        using var hiResources = new ResourceHarness("hi");
        var hindiBanner = cached.Describe(hiResources.Localize);

        Assert.NotEqual(englishBanner, hindiBanner);
        Assert.Contains("cached licence", englishBanner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("लाइसेंस", hindiBanner, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable", hindiBanner, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The grace-expired banner puts the CONFIGURED window into a translated sentence rather than
    /// gluing a number to an English "h".
    /// </summary>
    [Fact]
    public async Task TheGraceExpiredBannerCarriesTheWindowAsAPlaceholder()
    {
        var time = new FixedTimeProvider(Now);
        var client = new FakeAppManagerClient();
        var service = BuildLicenseService(client, time, graceHours: 36);

        client.OnValidateLicense = (_, _) => Task.FromResult(TeamLicense("Active"));
        await service.ValidateAsync();

        time.Advance(TimeSpan.FromHours(40));
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");
        var expired = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.GraceExpired, expired.Availability);
        Assert.Equal(LicenseMessageKeys.StateGraceExpired, expired.MessageKey);
        Assert.Equal(36, Assert.Single(expired.MessageArguments));

        using var hiResources = new ResourceHarness("hi");
        var hindi = expired.Describe(hiResources.Localize);

        Assert.Contains("36", hindi, StringComparison.Ordinal);
        Assert.Contains("ग्रेस", hindi, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    // Localize by CODE — the trap this requirement named explicitly.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A licence rejection is explained from the WIRE CODE, never from the server's English.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The proof is the pair of controls, not the happy path. Two rejections carrying the SAME
    /// English text but different codes must produce different messages; two carrying the same code
    /// but wildly different English — including English in another language entirely — must produce
    /// the same one. A mapping keyed off <c>Exception.Message</c> passes neither.
    /// </para>
    /// <para>
    /// This matters because that text is written by a server this app does not own. It is not part
    /// of any documented contract and can be reworded at any time; a localization keyed off it would
    /// fall back to the generic message the first time somebody fixed a typo in AppManager.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ValidationMessagesAreKeyedOffTheWireCodeNotTheEnglish()
    {
        // Same English, different codes -> different explanations.
        const string SharedEnglish = "License validation failed.";
        var expired = await RejectedWith("LICENSE_EXPIRED", SharedEnglish);
        var noAccess = await RejectedWith("NO_APP_ACCESS", SharedEnglish);

        Assert.Equal(LicenseMessageKeys.LicenseErrorExpired, expired.MessageKey);
        Assert.Equal(LicenseMessageKeys.LicenseErrorNoAppAccess, noAccess.MessageKey);
        Assert.NotEqual(expired.MessageKey, noAccess.MessageKey);

        // Same code, unrecognisable English -> the same explanation.
        var reworded = await RejectedWith("LICENSE_EXPIRED", "Sorry! that licence ran out ages ago");
        var translated = await RejectedWith("LICENSE_EXPIRED", "लाइसेंस समाप्त");

        Assert.Equal(expired.MessageKey, reworded.MessageKey);
        Assert.Equal(expired.MessageKey, translated.MessageKey);

        // And a code this build has never heard of says something true rather than guessing.
        var unknown = await RejectedWith("SOMETHING_APPMANAGER_ADDED_LATER", "who knows");
        Assert.Equal(LicenseMessageKeys.LicenseErrorGeneric, unknown.MessageKey);

        // Nothing the server wrote survives into what the user reads.
        using var resources = new ResourceHarness("en");
        foreach (var status in new[] { expired, noAccess, reworded, translated, unknown })
        {
            Assert.DoesNotContain("Sorry!", status.Describe(resources.Localize), StringComparison.Ordinal);
            Assert.DoesNotContain("who knows", status.Describe(resources.Localize), StringComparison.Ordinal);
        }
    }

    /// <summary>Feature denials are keyed off the wire code on the same terms.</summary>
    [Fact]
    public async Task FeatureDenialsAreKeyedOffTheWireCodeNotTheEnglish()
    {
        var notAvailable = await FeatureRejectedWith("FEATURE_NOT_AVAILABLE", "Nope.");
        var notFound = await FeatureRejectedWith("FEATURE_NOT_FOUND", "Nope.");
        var reworded = await FeatureRejectedWith("FEATURE_NOT_AVAILABLE", "not in your plan, sorry");

        Assert.Equal(LicenseMessageKeys.FeatureDeniedNotInPlan, notAvailable.ReasonKey);
        Assert.Equal(LicenseMessageKeys.FeatureDeniedUnknownFeature, notFound.ReasonKey);
        Assert.Equal(notAvailable.ReasonKey, reworded.ReasonKey);
        Assert.Null(notAvailable.ServerReason);
    }

    /// <summary>
    /// The whole documented AppManager error vocabulary maps to a key that resolves, in both
    /// languages, for both the licence check and the feature check.
    /// </summary>
    /// <remarks>
    /// Driven off <see cref="AppManagerError"/> itself, so a code added to the typed contract later
    /// cannot quietly acquire a message nobody translated.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryDocumentedErrorCodeMapsToATranslatedMessage(string culture)
    {
        using var resources = new ResourceHarness(culture);

        foreach (var error in Enum.GetValues<AppManagerError>())
        {
            foreach (var key in new[]
                     {
                         LicenseMessageKeys.ForValidationFailure(error),
                         LicenseMessageKeys.ForFeatureFailure(error)
                     })
            {
                Assert.Contains(key, LicenseMessageKeys.All);
                Assert.True(resources.OwnKeys.Contains(key), $"{error} maps to '{key}', missing from {culture}.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Server-supplied text — shown, framed, never translated.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// FeatureSvc's own sentence reaches the user WORD FOR WORD, inside a frame that is translated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policy, asserted rather than merely documented. AppManager may return a <c>reason</c>
    /// written by whoever configured the feature; it arrives at run time, in whatever language they
    /// wrote it in, and no key can exist for a sentence this build has never seen. Dropping it would
    /// throw away the only text saying why THIS deployment refused THIS feature; matching it against
    /// known English would be the exact defect REQ-UI-055 is about.
    /// </para>
    /// <para>
    /// So it is quoted, and the quoting is what gets translated: a Hindi reader gets a Hindi
    /// sentence telling them the words that follow are the server's, instead of a bare English
    /// fragment that reads as a missed translation.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ServerSuppliedReasonIsFramedRatherThanTranslated()
    {
        const string ServerWords = "Your organisation has used all 25 seats.";

        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, code, _) => Task.FromResult(new FeatureAccessData
            {
                FeatureCode = code, HasAccess = false, RequiredLicense = "Enterprise", Reason = ServerWords
            })
        };

        var decision = await BuildFeatureGate(client).EvaluateAsync("TEAM_SEATS");

        Assert.Equal(ServerWords, decision.ServerReason);
        Assert.Null(decision.ReasonKey);

        using var hiResources = new ResourceHarness("hi");
        var framed = decision.DescribeReason(hiResources.Localize);

        // The server's words survive intact...
        Assert.Contains(ServerWords, framed, StringComparison.Ordinal);

        // ...inside a frame that is itself Hindi, so the English is attributed rather than orphaned.
        Assert.NotEqual(ServerWords, framed);
        Assert.Contains("लाइसेंस सर्वर", framed, StringComparison.Ordinal);
    }

    /// <summary>
    /// When AppManager offers no reason of its own, the denial is one of OUR keys and is translated.
    /// </summary>
    [Fact]
    public async Task DenialWithNoServerReasonUsesOurOwnTranslatedCopy()
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, code, _) => Task.FromResult(new FeatureAccessData
            {
                FeatureCode = code, HasAccess = false, RequiredLicense = "Enterprise"
            })
        };

        var decision = await BuildFeatureGate(client).EvaluateAsync("WHITE_LABEL");

        Assert.Null(decision.ServerReason);
        Assert.Equal(LicenseMessageKeys.FeatureDeniedUpgradeRequired, decision.ReasonKey);

        using var hiResources = new ResourceHarness("hi");
        var reason = decision.DescribeReason(hiResources.Localize);

        // The feature CODE stays in Latin script — it is AppManager's vocabulary, re-cased. The
        // sentence around it is Hindi.
        Assert.Contains("White Label", reason, StringComparison.Ordinal);
        Assert.Contains("प्लान", reason, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------
    // The half that must NOT move: entitlement matching.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Entitlements resolve identically in every culture.</b> This is the money test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure it guards against is not cosmetic. <c>InstanceModeResolver</c> decides Team and
    /// Enterprise entitlements by looking <see cref="LicenseStatus.LicenseName"/> up in
    /// <see cref="LicensingOptions.TeamLicenseTiers"/>, and <c>Pricing.IsCurrent</c> prefix-matches
    /// the same value against the published plan names. If translating the licence card had reached
    /// either value, a Hindi install would resolve a paying Team seat to Individual and stop
    /// highlighting the plan the customer actually holds — a mistranslation that reads as a
    /// downgrade.
    /// </para>
    /// <para>
    /// So the whole resolution is run twice, in two cultures, and compared field by field: the mode,
    /// the seat, the tier name, and the upgrade tier on a denial. Only the prose is allowed to
    /// differ, and the last assertion proves it did.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EntitlementMatchingIsIdenticalInEveryCulture()
    {
        var options = new LicensingOptions();
        string?[] tiers = [null, "Free", "Professional", "Team", "Business", "Enterprise", "Weird"];
        string?[] states = [null, "Active", "Expired", "Revoked", "Pending"];

        (InstanceMode Mode, SeatState Seat, string? Tier, bool Team)[] ResolveAll()
            =>
            [
                .. from availability in Enum.GetValues<LicenseAvailability>()
                   from tier in tiers
                   from state in states
                   let resolved = InstanceModeResolver.Resolve(
                       new LicenseStatus { Availability = availability, LicenseName = tier, Status = state },
                       options)
                   select (resolved.Mode, resolved.Seat, resolved.TierName, resolved.IsTeamOrEnterprise)
            ];

        (InstanceMode Mode, SeatState Seat, string? Tier, bool Team)[] english;
        string offlineLicenseName;
        string offlineStatus;
        string? deniedUpgradeTier;

        using (new ResourceHarness("en"))
        {
            english = ResolveAll();
            offlineLicenseName = LicenseStatus.Offline.LicenseName!;
            offlineStatus = LicenseStatus.Offline.Status!;
            deniedUpgradeTier = (await OfflineDenial()).RequiredLicense;
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, ResolveAll());

            // The wire values the licence server matches on are byte-identical.
            Assert.Equal(offlineLicenseName, LicenseStatus.Offline.LicenseName);
            Assert.Equal(offlineStatus, LicenseStatus.Offline.Status);
            Assert.Equal(deniedUpgradeTier, (await OfflineDenial()).RequiredLicense);

            // ...and they are still the exact values the rest of the system expects. Pricing
            // highlights the Free card by PREFIX; IsActive compares against "Active".
            Assert.StartsWith("Free", LicenseStatus.Offline.LicenseName!, StringComparison.Ordinal);
            Assert.Equal("Active", LicenseStatus.Offline.Status);
            Assert.True(LicenseStatus.Offline.FeaturesPermitted);
            Assert.Equal("Professional", deniedUpgradeTier);

            // A Team tier still entitles a Team seat while the reader is in Hindi.
            var team = InstanceModeResolver.Resolve(
                new LicenseStatus
                {
                    Availability = LicenseAvailability.Live, LicenseName = "Team", Status = "Active"
                },
                options);

            Assert.Equal(InstanceMode.Team, team.Mode);
            Assert.Equal(SeatState.Assigned, team.Seat);
            Assert.Equal("Team", team.TierName);
        }

        // The guard against a vacuous pass: the PROSE did move, even though nothing above did.
        var status = new LicenseStatus
        {
            Availability = LicenseAvailability.Live, LicenseName = "Team", Status = "Active"
        };
        var mode = InstanceModeResolver.Resolve(status, options);

        string englishSentence;
        using (var enResources = new ResourceHarness("en"))
        {
            englishSentence = mode.Describe(enResources.Localize);
        }

        using var hiResources = new ResourceHarness("hi");
        Assert.NotEqual(englishSentence, mode.Describe(hiResources.Localize));

        // The invariant tier name is quoted into BOTH sentences, untranslated.
        Assert.Contains("Team", englishSentence, StringComparison.Ordinal);
        Assert.Contains("Team", mode.Describe(hiResources.Localize), StringComparison.Ordinal);
    }

    /// <summary>
    /// The licence tier maps are ORDINAL sets of invariant names, and the ambient culture cannot
    /// change what they match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two assertions, and the first is the one that bites. The comparer is checked DIRECTLY,
    /// because a behavioural check could not see the mistake it guards: swapping
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> for
    /// <see cref="StringComparer.CurrentCultureIgnoreCase"/> was tried as a mutation and every
    /// behavioural assertion below stayed green — ICU's case-insensitive collation still matches
    /// <c>BUSINESS</c> to <c>Business</c> under <c>tr-TR</c>. The regression is real (a
    /// culture-sensitive lookup on an entitlement map is a latent billing bug) but it does not
    /// reproduce through behaviour on the strings TechieDesk actually ships, so the structure is
    /// what has to be asserted. That is worth saying out loud rather than leaving a test that looks
    /// stronger than it is.
    /// </para>
    /// <para>
    /// The behavioural half is kept for what it DOES prove: tier matching is case-insensitive, and
    /// it survives an exotic ambient culture end-to-end rather than only in theory.
    /// </para>
    /// </remarks>
    [Fact]
    public void TierLookupsAreOrdinalAndCultureBlind()
    {
        var options = new LicensingOptions();

        Assert.Same(StringComparer.OrdinalIgnoreCase, options.TeamLicenseTiers.Comparer);
        Assert.Same(StringComparer.OrdinalIgnoreCase, options.EnterpriseLicenseTiers.Comparer);
        Assert.Same(StringComparer.OrdinalIgnoreCase, options.OfflinePremiumFeatures.Comparer);

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            foreach (var tier in new[] { "Team", "TEAM", "team", "Business", "BUSINESS", "business", "Enterprise" })
            {
                var mode = InstanceModeResolver.Resolve(
                    new LicenseStatus
                    {
                        Availability = LicenseAvailability.Live, LicenseName = tier, Status = "Active"
                    },
                    options);

                Assert.True(mode.IsTeamOrEnterprise, $"'{tier}' stopped entitling a seat under tr-TR.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = original;
        }
    }

    // -------------------------------------------------------------------------------------------
    // The zero gate for this slice.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// No file under <c>Services/Licensing/</c> builds an English sentence any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <c>ServiceStringCoverageTests.LocalizedServiceFilesCarryNoEnglish</c>,
    /// kept HERE rather than added to that registry so this slice's gate does not share a file with
    /// five other concurrent tranches. It is strictly the stronger of the two: it covers every file
    /// in the folder, including any added later, rather than a hand-maintained list.
    /// </para>
    /// <para>
    /// The two survivors are named in <see cref="MachineFacingLicensingText"/> with a reason each,
    /// and the test below keeps those claims honest.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoLicensingServiceBuildsAnEnglishSentence()
    {
        var folder = Path.Combine(ServiceStringCoverage.FindServicesRoot(), "Licensing");
        Assert.True(Directory.Exists(folder), $"The licensing services are not at '{folder}'.");

        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 12, $"Only {files.Length} licensing files were scanned.");

        var offenders = new List<string>();

        foreach (var file in files)
        {
            var row = ServiceStringCoverage.Measure(File.ReadAllText(file), Path.GetFileName(file));
            var unexplained = row.Literals.Where(value => !MachineFacingLicensingText.Contains(value)).ToArray();

            if (unexplained.Length > 0)
            {
                offenders.Add($"{row.RelativePath}: \"{string.Join("\", \"", unexplained)}\"");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A licensing service has grown an English sentence again (REQ-UI-055). It renders in " +
            "the always-visible shell banner, so this is English on every screen of a translated " +
            "install. Return a key from LicenseMessageKeys instead:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", offenders));
    }

    /// <summary>
    /// Every machine-facing exemption is still real, and the list has not started growing.
    /// </summary>
    /// <remarks>
    /// An exemption list is where a coverage gate goes to die. Two entries, both argued in the
    /// remarks above; a stale one would silently cover whatever string matched it next.
    /// </remarks>
    [Fact]
    public void TheMachineFacingExemptionsStaySmallAndInUse()
    {
        Assert.True(MachineFacingLicensingText.Count <= 3,
            $"The licensing machine-text list has grown to {MachineFacingLicensingText.Count} entries.");

        var folder = Path.Combine(ServiceStringCoverage.FindServicesRoot(), "Licensing");
        var sources = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var entry in MachineFacingLicensingText)
        {
            Assert.True(
                sources.Any(source => source.Contains(entry, StringComparison.Ordinal)),
                $"'{entry}' is exempted as machine text but no licensing service still contains it.");
        }
    }

    /// <summary>
    /// The status types no longer expose anything a component could render English from.
    /// </summary>
    /// <remarks>
    /// The structural half. <c>MainLayout</c> rendered <c>licenseStatus.Message</c> directly; had
    /// that property survived as a string beside the new key, the banner would have kept compiling
    /// and kept showing English. Removing it is what made every render site fail to build until it
    /// was converted, and this test is what stops it coming back.
    /// </remarks>
    [Fact]
    public void TheStatusTypesExposeNoRenderableEnglish()
    {
        foreach (var type in new[] { typeof(LicenseStatus), typeof(InstanceModeStatus) })
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name == "Message" && property.PropertyType == typeof(string));

            Assert.Contains(type.GetProperties(), property => property.Name == "MessageKey");
        }

        // FeatureDecision keeps a Reason-shaped member ONLY for the server's own words, and its
        // name says so — nothing can mistake it for app copy.
        Assert.DoesNotContain(
            typeof(FeatureDecision).GetProperties(),
            property => property.Name == "Reason");
        Assert.Contains(typeof(FeatureDecision).GetProperties(), property => property.Name == "ServerReason");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private static LicenseValidationData TeamLicense(string status) => new()
    {
        IsValid = true,
        License = new ActiveLicenseData
        {
            LicenseId = 42,
            LicenseName = "Team",
            Status = status,
            ExpiryDate = Now.AddDays(300),
            DaysRemaining = 300
        }
    };

    private static LicenseService BuildLicenseService(
        FakeAppManagerClient client, FixedTimeProvider time, int graceHours = 72)
    {
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", time.GetUtcNow().AddYears(1));

        return new LicenseService(
            client,
            new InMemoryLicenseCacheRepository(),
            TestFactory.Mode(appManagerEnabled: true),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            Options.Create(new LicensingOptions { LicenseGraceHours = graceHours }),
            time,
            NullLogger<LicenseService>.Instance);
    }

    private static FeatureGateService BuildFeatureGate(
        FakeAppManagerClient client, bool appManagerEnabled = true)
    {
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1));

        return new FeatureGateService(
            client,
            TestFactory.Mode(appManagerEnabled),
            store,
            new StubTokenRefresher(),
            new FakeLicenseService(new LicenseStatus
            {
                Availability = LicenseAvailability.Live, LicenseName = "Professional", Status = "Active"
            }),
            Options.Create(new LicensingOptions()),
            NullLogger<FeatureGateService>.Instance);
    }

    /// <summary>Drives a licence validation that AppManager rejects with a chosen code and text.</summary>
    private static async Task<LicenseStatus> RejectedWith(string errorCode, string serverEnglish)
    {
        var client = new FakeAppManagerClient
        {
            OnValidateLicense = (_, _) => throw new AppManagerException(errorCode, serverEnglish, 403)
        };

        return await BuildLicenseService(client, new FixedTimeProvider(Now)).ValidateAsync();
    }

    /// <summary>Drives a feature check that AppManager rejects with a chosen code and text.</summary>
    private static async Task<FeatureDecision> FeatureRejectedWith(string errorCode, string serverEnglish)
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, _, _) => throw new AppManagerException(errorCode, serverEnglish, 403)
        };

        return await BuildFeatureGate(client).EvaluateAsync("CONNECTORS");
    }

    /// <summary>The offline Free-tier denial, whose upgrade tier is matched by the licence server.</summary>
    private static async Task<FeatureDecision> OfflineDenial()
        => await BuildFeatureGate(new FakeAppManagerClient(), appManagerEnabled: false)
            .EvaluateAsync("WHITE_LABEL");
}
