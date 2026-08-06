using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// REQ-UI-055 (BRD-91): nothing the scheduling cluster shows a person is English on a Hindi install,
/// and nothing it stores, sends or re-parses moves with the language.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a separate file from the describer's.</b> <see cref="CronDescriberTests"/> asserts
/// the composed SENTENCE, which is one call site with a hundred shapes. This asserts the other four
/// surfaces the cluster hands to a screen — the parse errors, the run-condition skip reasons, the
/// interpreter's refusals, and the background helper's state and toasts — each of which is one shape
/// with one string, and each of which was English until this requirement.
/// </para>
/// <para>
/// <b>Everything resolves through the real localizer.</b> Asserting on the key alone would prove
/// nothing: the defect class REQ-UI-051 replaced is a value that looks right in code and renders
/// English on a translated install. <see cref="ResourceHarness"/> goes through the container, so a key
/// missing from <c>AppStrings.hi.resx</c> resolves to the English text with <c>ResourceNotFound</c>
/// false — which is what <c>ResourceHarness.OwnKeys</c> is compared against below.
/// </para>
/// </remarks>
public sealed class SchedulingLocalizationTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "techiedesk-scheduling-l10n", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary directory the helper tests write into.</summary>
    public SchedulingLocalizationTests() => Directory.CreateDirectory(root);

    /// <summary>Removes the temporary directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Every resource key the scheduling cluster can return is present in BOTH shipped languages.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The culture's OWN key set, not merely "it resolved". A key present in English and missing from
    /// Hindi resolves to the ENGLISH value with <c>ResourceNotFound</c> false, which is an English row
    /// on a Hindi screen and is exactly what this requirement is about.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EverySchedulingKeyIsInBothResourceSets(string culture)
    {
        using var resources = new ResourceHarness(culture);
        var own = resources.OwnKeys;

        foreach (var key in SchedulingKeys)
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by the scheduling cluster but missing from the {culture} " +
                $"resources, so Automations shows English (or the key name) in a {culture} window.");

            // ResourceManagerStringLocalizer returns the KEY NAME when the lookup misses entirely,
            // so a value equal to its own key is a miss the localizer will not throw over.
            var value = resources.Require(key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// A cron parse failure names the offending DATA verbatim and the wording from the resources.
    /// </summary>
    /// <param name="expression">An expression that cannot be parsed.</param>
    /// <param name="key">The key the parser must return.</param>
    /// <param name="quoted">The offending token, which must survive into both languages unchanged.</param>
    [Theory]
    [InlineData("", "CronErrorExpressionRequired", null)]
    [InlineData("0 7 * *", "CronErrorFieldCount", null)]
    [InlineData("0 25 * * *", "CronErrorOutOfRange", "25")]
    [InlineData("0 7 * * XYZ", "CronErrorNotANumber", "XYZ")]
    [InlineData("*/0 * * * *", "CronErrorInvalidStep", "*/0")]
    public void ParseErrorsCarryAKeyAndUntranslatedData(string expression, string key, string? quoted)
    {
        Assert.False(CronExpression.TryParse(expression, out _, out var error));
        Assert.Equal(key, error!.Key);

        using var english = new ResourceHarness("en");
        var inEnglish = error.Resolve(english.Localize);

        using var hindi = new ResourceHarness("hi");
        var inHindi = error.Resolve(hindi.Localize);

        Assert.NotEqual(inEnglish, inHindi);
        Assert.Contains(inHindi, character => character is >= 'ऀ' and <= 'ॿ');

        if (quoted is not null)
        {
            // The cron field is DATA. It is quoted back at the user in both languages, byte for byte,
            // because it is what they typed into the Advanced box.
            Assert.Contains(quoted, inEnglish, StringComparison.Ordinal);
            Assert.Contains(quoted, inHindi, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A skipped run explains itself in the reader's language, keeping the network's own name.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void SkipReasonsAreLocalizedAndKeepTheNetworkName(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var onBattery = new RunConditionEvaluator(new FakeRunEnvironmentProbe(PowerState.Battery, "Home"))
            .Evaluate(new RunConditions(RequireMainsPower: true));

        var offNetwork = new RunConditionEvaluator(
                new FakeRunEnvironmentProbe(PowerState.Mains, "Airport WiFi"))
            .Evaluate(new RunConditions(RestrictToNamedNetworks: true, AllowedNetworks: ["Home"]));

        Assert.False(onBattery.IsAllowed);
        Assert.False(offNetwork.IsAllowed);

        // REQ-UI-056: the evaluator returns CODES now, so the culture enters at Resolve rather than
        // at Evaluate. The assertion is unchanged in what it proves — the skip reason a reader sees
        // is the one this culture's resource file holds.
        Assert.Equal(
            resources.Require("SchedulerSkipOnBattery"), onBattery.Reason!.Resolve(resources.Localize));

        // The user named their own WiFi. It is theirs, not ours, and it survives verbatim.
        Assert.Contains(
            "Airport WiFi",
            offNetwork.Reason!.Resolve(resources.Localize),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The background helper names its mechanism and explains its state in the reader's language.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void TheHelperStateIsLocalized(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var unavailable = BuildHelper(null, resources.Localize).GetState();
        var notInstalled = BuildHelper(CreateFakeHelper(), resources.Localize).GetState();

        Assert.Equal(resources.Require("SchedulerMechanismLaunchAgent"), unavailable.MechanismName);
        Assert.Equal(resources.Require("SchedulerHelperNotInstalledReason"), notInstalled.Reason);
        Assert.Contains("TechieDeskScheduler", unavailable.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The install and uninstall toasts are localized, and still quote the real path.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public async Task TheHelperToastsAreLocalized(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var refused = await BuildHelper(null, resources.Localize).InstallAsync(SchedulerPreferences.Default);
        var uninstalled = await BuildHelper(CreateFakeHelper(), resources.Localize).UninstallAsync();

        Assert.False(refused.Succeeded);
        Assert.Equal(
            resources.Require("SchedulerHelperMissingOnInstallMac", "TechieDeskScheduler"), refused.Message);

        Assert.True(uninstalled.Succeeded);
        Assert.Equal(
            resources.Require(
                "SchedulerHelperUninstalledMac", resources.Require("SchedulerHelperNoAgentInstalled")),
            uninstalled.Message);
    }

    /// <summary>
    /// The identifiers the operating system is addressed by never move with the language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQ-UI-055's central risk, asserted directly for this cluster. The launchd LABEL is what
    /// <c>launchctl bootout</c> is given and what names the plist on disk; the Task Scheduler task name
    /// is what <c>schtasks /Delete /TN</c> is given; the environment variable is what the helper
    /// process reads to find the same data directory the app uses. A translated one of those uninstalls
    /// nothing, or points a second process at a second database.
    /// </para>
    /// <para>
    /// The trap is not hypothetical: <c>QdrantAdmin</c>'s daemon endpoint kind was once a string that
    /// WAS its English label and was parsed back to build the endpoint.
    /// </para>
    /// </remarks>
    [Fact]
    public void HelperWireIdentifiersAreTheSameInEveryCulture()
    {
        string[] english;
        using (var resources = new ResourceHarness("en"))
        {
            english = HelperWireVocabulary(resources.Localize);
        }

        using (var resources = new ResourceHarness("hi"))
        {
            Assert.Equal(english, HelperWireVocabulary(resources.Localize));
        }

        Assert.Contains("com.techiedesk.scheduler", english);
        Assert.Contains("TechieDeskScheduler", english);
        Assert.Contains("TechieDeskDataDirectory", english);
    }

    /// <summary>
    /// The plist written to disk is byte-identical whatever language the app is running in.
    /// </summary>
    /// <remarks>
    /// The plist is read by launchd, not by a person. If any part of it moved with the culture, an
    /// agent installed in Hindi would register under a different label from the one uninstall boots
    /// out, and the helper would survive being switched off.
    /// </remarks>
    [Fact]
    public void ThePlistIsIdenticalInEveryCulture()
    {
        string english;
        using (new ResourceHarness("en"))
        {
            english = LaunchAgentSchedulerHelper.BuildPlist(
                "/tmp/TechieDeskScheduler", root, SchedulerPreferences.Default, "/usr/local/share/dotnet");
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(
                english,
                LaunchAgentSchedulerHelper.BuildPlist(
                    "/tmp/TechieDeskScheduler", root, SchedulerPreferences.Default, "/usr/local/share/dotnet"));
        }
    }

    /// <summary>Every key the scheduling cluster can hand to a screen.</summary>
    /// <remarks>
    /// Hand-written rather than scraped, and that is the point: adding a key to a service without
    /// adding it here is invisible, so the list is reviewed the way the resources are. It covers the
    /// describer's whole vocabulary, both parse-error families, both helpers and the confirmation gate.
    /// </remarks>
    private static readonly string[] SchedulingKeys =
    [
        "CronDaySunday", "CronDayMonday", "CronDayTuesday", "CronDayWednesday",
        "CronDayThursday", "CronDayFriday", "CronDaySaturday",
        "CronDayShortSunday", "CronDayShortMonday", "CronDayShortTuesday", "CronDayShortWednesday",
        "CronDayShortThursday", "CronDayShortFriday", "CronDayShortSaturday",
        "CronMonthJanuary", "CronMonthFebruary", "CronMonthMarch", "CronMonthApril",
        "CronMonthMay", "CronMonthJune", "CronMonthJuly", "CronMonthAugust",
        "CronMonthSeptember", "CronMonthOctober", "CronMonthNovember", "CronMonthDecember",
        "CronOrdinalFirst", "CronOrdinalSecond", "CronOrdinalThird", "CronOrdinalOther",
        "CronListAnd",
        "CronEveryMinute", "CronEveryMinuteOnDays", "CronEveryNMinutes", "CronEveryNMinutesOnDays",
        "CronEveryHourAtMinute", "CronEveryHourAtMinuteOnDays",
        "CronEveryNHoursAtMinute", "CronEveryNHoursAtMinuteOnDays",
        "CronDaysAtTime", "CronDaysAtTimeCount",
        "CronDaysEveryDay", "CronDaysEveryWeekday", "CronDaysEveryWeekendDay", "CronDaysNamed",
        "CronDaysOfMonth", "CronDaysOfMonthCount", "CronDaysInMonths", "CronDaysOrJoin",
        "CronMonthsCount",
        "CronErrorExpressionRequired", "CronErrorFieldCount", "CronErrorSelectsNothing",
        "CronErrorInvalidStep", "CronErrorNotANumber", "CronErrorOutOfRange",
        "ScheduleInterpretNeedsInstruction", "ScheduleInterpretNoLocalModel",
        "ScheduleInterpretNoActions", "ScheduleInterpretModelFailed", "ScheduleInterpretNoSchedule",
        "ScheduleInterpretUnusableCron", "ScheduleInterpretUnknownAction",
        "ScheduleInterpretSummaryMismatch",
        "ScheduleDraftStepRuns", "ScheduleDraftStepNumbered", "ScheduleDraftStepThen",
        "ScheduleDraftDeliveryDefault",
        "ScheduleNotConfirmedTextChanged", "ScheduleNotConfirmedActionChanged",
        "ScheduleNotConfirmedNotValid",
        "SchedulerSkipOnBattery", "SchedulerSkipNotOnAllowedNetwork", "SchedulerSkipMissedNoCatchUp",
        "SchedulerMechanismLaunchAgent", "SchedulerMechanismWindowsLogonTask",
        "SchedulerHelperInstalledReasonMac", "SchedulerHelperInstalledReasonWindows",
        "SchedulerHelperNotInstalledReason",
        "SchedulerHelperUnavailableReasonMac", "SchedulerHelperUnavailableReasonWindows",
        "SchedulerHelperMissingOnInstallMac", "SchedulerHelperMissingOnInstallWindows",
        "SchedulerHelperWriteFailed", "SchedulerHelperLoadRefused", "SchedulerHelperInstalledMac",
        "SchedulerHelperInstalledWindows", "SchedulerHelperRemovedFile",
        "SchedulerHelperNoAgentInstalled", "SchedulerHelperDeleteFailed",
        "SchedulerHelperUninstalledMac", "SchedulerHelperUninstalledWindows",
        "SchedulerHelperTaskCreateRefused", "SchedulerHelperTaskDeleteRefused",
        "SchedulerToolMissing", "SchedulerToolNotStarted", "SchedulerToolTimedOut",
        "SchedulerToolTimedOutSoon"
    ];

    /// <summary>Collects every scheduling value that the operating system is addressed by.</summary>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <returns>The wire vocabulary, in a stable order.</returns>
    private string[] HelperWireVocabulary(TechieDesk.Services.Localization.LocalizeText localize)
    {
        var helper = BuildHelper(CreateFakeHelper(), localize);

        return
        [
            LaunchAgentSchedulerHelper.AgentLabel,
            LaunchAgentSchedulerHelper.DataDirectoryEnvironmentVariable,
            WindowsSchedulerHelper.TaskName,
            helper.PlistPath,
            new SchedulerHelperLocator(null, root).ExecutableName,
            helper.GetState().MechanismLocation,
            helper.GetState().HelperExecutablePath!
        ];
    }

    private string CreateFakeHelper()
    {
        var path = Path.Combine(root, "TechieDeskScheduler");
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        return path;
    }

    private LaunchAgentSchedulerHelper BuildHelper(
        string? helperPath, TechieDesk.Services.Localization.LocalizeText localize) => new(
        new FakeHelperLocator(helperPath),
        NullLogger<LaunchAgentSchedulerHelper>.Instance,
        localize,
        Path.Combine(root, "LaunchAgents"),
        Path.Combine(root, "data"));
}
