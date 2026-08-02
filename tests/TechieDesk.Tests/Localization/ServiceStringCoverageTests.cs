using TechieDesk.Services;
using TechieDesk.Services.Agents;
using TechieDesk.Services.Storage;
using TechieDesk.Services.Support;
using TechieDesk.Services.Workspaces;
using TechieDesk.Tests.Support;
using TechieRag;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-051 (BRD-91): the service layer does not hand English to a screen, and cannot start
/// doing so again without a test failing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> App-wide MARKUP localization reached 100% (2,280/2,280) on
/// 2026-07-31 and a Hindi install still rendered English the next morning, because
/// <see cref="RazorStringCoverage"/> measures markup and <see cref="CodeBlockStringCoverage"/>
/// measures <c>@code</c> blocks, and both scan the razor tree. Nine artefact names and nine
/// descriptions at <c>/settings/data</c> came from a static class in
/// <c>apps/TechieDesk.Core/Services/Storage/</c>, which neither counter can see. Both counters'
/// authors wrote that blind spot into their own remarks; this is the test that removes it.
/// </para>
/// <para>
/// <b>Three tests, three different jobs.</b>
/// <see cref="LocalizedServiceFilesCarryNoEnglish"/> is a ZERO gate on the files REQ-UI-051
/// converted — precise, and the one that catches a regression in them.
/// <see cref="TheServiceLayerNeverGrowsMoreEnglish"/> is a RATCHET over the whole service tree —
/// broad, and the one that catches the NEXT service to grow a label.
/// <see cref="EveryUserFacingServiceTableResolvesThroughResources"/> is the positive proof: it
/// resolves every key the converted tables return through the real localizer, in both languages.
/// </para>
/// <para>
/// <b>What none of them cover</b> is set out at length on <see cref="ServiceStringCoverage"/> and
/// is not repeated here. The short version: one-word labels, English composed at run time,
/// anything outside <c>Services/</c>, and whether the Hindi is any good.
/// </para>
/// </remarks>
public sealed class ServiceStringCoverageTests
{
    /// <summary>
    /// The service files REQ-UI-051 converted to resource keys. Held at ZERO prose literals.
    /// </summary>
    /// <remarks>
    /// The counterpart of <c>StringCoverageTests</c>'s localized-file registry, and it works the
    /// same way: a file on this list has been dealt with, so a prose literal reappearing in it is a
    /// regression rather than a backlog item. Adding a file here is a claim that every user-visible
    /// string it produces goes through a resource key.
    /// </remarks>
    public static readonly IReadOnlySet<string> LocalizedServiceFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "Agents/AgentTrace.cs",
        "Agents/SkillCatalog.cs",
        "LlmConfigValidator.cs",
        "Scheduling/Authoring/ScheduleInterpreter.cs",
        "Scheduling/CronDescriber.cs",
        "Scheduling/CronExpression.cs",
        "Scheduling/RunConditions.cs",
        "Scheduling/ScheduleService.cs",
        "Storage/DataStorageArtefact.cs",
        "Storage/DataStorageArtefactDefinition.cs",
        "Storage/DataStorageInspector.cs",
        "Support/SupportIssueCatalog.cs",
        "Workspaces/ChatComposer.cs"
    };

    /// <summary>
    /// The number of prose literals the service layer carried when this ratchet was set, on
    /// 2026-08-01, after REQ-UI-051 localized the six known sites.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This number is a CEILING, not a target, and it is not a claim that what remains is fine. It
    /// is a claim that the service layer will not quietly grow MORE English while nobody is
    /// looking, which is exactly how the markup gap re-grew three times.
    /// </para>
    /// <para>
    /// It is EXACTLY the current count, so any new prose literal fails. It is deliberately not
    /// zero and deliberately not a percentage. A percentage over this
    /// population would move when a file is split; a zero gate would fail on the first SQL constant
    /// or model-facing tool description somebody adds and would be suppressed within a week. A
    /// number that may not rise is the strongest honest statement available here.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// Lowered 569 -> 330 on 2026-08-02 (REQ-UI-055), after six clusters converted the connectors,
    /// scheduling, licensing, backup/Docker, leaf-service and agent-skill slices. The ceiling is
    /// re-measured and set by the ORCHESTRATOR once, at the end of a pass, because six agents
    /// lowering one integer concurrently would corrupt it: each was told to leave this line alone.
    /// </para>
    /// <para>
    /// Re-measure it the same way rather than guessing: temporarily change the assertion below to
    /// <c>total &lt;= 0</c>, run this one test, and read the count out of the failure message.
    /// </para>
    /// <para>
    /// What remains at 330 is NOT all defect. A large share is deliberate machine-facing text that
    /// this counter cannot distinguish from user-visible prose — model-facing tool descriptions and
    /// schemas, launchd plist fragments, schtasks arguments, pmset/airport tokens, SQL, developer
    /// exception text. That indistinguishability is precisely why this is a ratchet and not a zero
    /// gate. The remaining USER-VISIBLE subset is tracked by REQ-UI-055, not by driving this number
    /// to zero.
    /// </para>
    /// </remarks>
    private const int ServiceEnglishCeiling = 330;

    /// <summary>
    /// Every file REQ-UI-051 converted stays free of English prose.
    /// </summary>
    [Fact]
    public void LocalizedServiceFilesCarryNoEnglish()
    {
        var offenders = ServiceStringCoverage.Scan()
            .Where(row => LocalizedServiceFiles.Contains(row.RelativePath))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{offenders.Length} file(s) REQ-UI-051 localized have regained an English literal. " +
            "Every user-visible string in these files is meant to be a resource KEY resolved by " +
            "whatever renders it:" + Environment.NewLine + "  " +
            string.Join(
                Environment.NewLine + "  ", offenders.Select(ServiceStringCoverage.Describe)));
    }

    /// <summary>
    /// Every file named on the registry actually exists, so a rename cannot silently retire a gate.
    /// </summary>
    /// <remarks>
    /// The registry is matched by path. A file moved or renamed would drop off the zero gate while
    /// the test kept passing — the same failure mode that lets a suppression outlive the thing it
    /// suppressed.
    /// </remarks>
    [Fact]
    public void EveryRegisteredServiceFileStillExists()
    {
        var root = ServiceStringCoverage.FindServicesRoot();

        foreach (var relativePath in LocalizedServiceFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                $"'{relativePath}' is on the REQ-UI-051 localized-service registry but no longer " +
                "exists, so nothing is holding it at zero. Update the registry with its new path.");
        }
    }

    /// <summary>
    /// The service layer never grows more English than it already had.
    /// </summary>
    /// <remarks>
    /// The guard that catches the NEXT one. It fires on a new prose literal anywhere under
    /// <c>Services/</c>, which is the shape of every defect in this class: somebody adds a label to
    /// a service, no counter sees it, and it renders English on a Hindi install months later.
    /// </remarks>
    [Fact]
    public void TheServiceLayerNeverGrowsMoreEnglish()
    {
        var rows = ServiceStringCoverage.Scan();
        var total = rows.Sum(row => row.Count);

        Assert.True(
            total <= ServiceEnglishCeiling,
            $"The service layer now builds {total} English prose literal(s), up from the " +
            $"{ServiceEnglishCeiling} recorded on 2026-08-01 (REQ-UI-051). A string added to a " +
            "service is invisible to BOTH razor counters, so this is the only thing that sees it. " +
            "Three legitimate responses, in order of preference: (1) return a resource KEY and let " +
            "the surface that renders it resolve it — see DataStorageInspector; (2) if it is " +
            "genuinely machine text (a log template, a prompt sent to the model, SQL, a wire code), " +
            "say so in review and shape it so the scan skips it; (3) if it is neither, raise the " +
            "ceiling here WITH a written reason. The largest files are:" + Environment.NewLine +
            "  " + string.Join(
                Environment.NewLine + "  ",
                rows.OrderByDescending(row => row.Count).Take(10).Select(ServiceStringCoverage.Describe)));
    }

    /// <summary>
    /// The scan is looking at the real service tree, so a green run means something.
    /// </summary>
    /// <remarks>
    /// A path that stopped resolving would make every assertion above vacuously true — the exact
    /// failure mode that lets a coverage gate report success over an empty set.
    /// </remarks>
    [Fact]
    public void ScanFindsTheRealServiceTree()
    {
        var root = ServiceStringCoverage.FindServicesRoot();
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);

        Assert.True(files.Length >= 150, $"Only {files.Length} service files were found under '{root}'.");
        Assert.True(
            File.Exists(Path.Combine(root, "Storage", "DataStorageInspector.cs")),
            "The artefact table REQ-UI-051 was raised for is not where the scan is looking.");
    }

    /// <summary>
    /// Every key the converted service tables return resolves in both shipped languages.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// <para>
    /// The positive half, and the one that replaces something REQ-UI-051 gave up. Keys used to be
    /// written as literals in the razor components, where
    /// <c>ResolvesEveryKeyTheRazorComponentsAskFor</c> scrapes them; moving them into the services
    /// takes them out of that scrape's reach. This test is what puts them back under a guard.
    /// </para>
    /// <para>
    /// It drives the ENUMS and the tables rather than a hand-written key list, so a seventh skill,
    /// a sixth answering mode or a fifth saved prompt is covered the day it is added.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryUserFacingServiceTableResolvesThroughResources(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var keys = new List<string>();

        keys.AddRange(SkillCatalog.Skills.SelectMany(skill => new[] { skill.DisplayNameKey, skill.DescriptionKey }));
        keys.AddRange(Enum.GetValues<SkillExposure>().Select(SkillCatalog.ExposureLabelKey));

        keys.AddRange(ChatComposerState.SavedPrompts.SelectMany(prompt => new[] { prompt.TitleKey, prompt.TextKey }));
        keys.AddRange(Enum.GetValues<ChatAnswerMode>().Select(ChatComposerState.ModeLabelKey));
        keys.AddRange(Enum.GetValues<ChatAnswerMode>().Select(ChatComposerState.ModeDescriptionKey));
        keys.AddRange(Enum.GetValues<WorkspaceRetrievalScope>().Select(ChatComposerState.ScopeLabelKey));

        keys.AddRange(DataStorageInspector.KnownArtefacts.SelectMany(a => new[] { a.NameKey, a.DescriptionKey }));
        keys.Add(DataStorageInspector.OtherArtefactNameKey);
        keys.Add(DataStorageInspector.OtherArtefactDescriptionKey);

        keys.AddRange(SupportIssueCatalog.Types
            .Concat(SupportIssueCatalog.Priorities)
            .Concat(SupportIssueCatalog.Statuses)
            .SelectMany(option => new[] { option.LabelKey, option.QualifierKey }));

        // A guard against the guard: if the tables were ever emptied this would pass over nothing.
        Assert.True(keys.Count >= 60, $"Only {keys.Count} service-owned keys were collected.");

        var own = resources.OwnKeys;

        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            Assert.DoesNotContain(' ', key);

            // The culture's OWN key set, not merely "it resolved". A key present in English and
            // missing from Hindi resolves to the ENGLISH value with ResourceNotFound false, which
            // is an English row on a Hindi screen and is exactly the defect this REQ is about.
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by a service but missing from the {culture} resources, so " +
                $"whatever renders it shows English (or the key name) in a {culture} window.");

            // ResourceManagerStringLocalizer returns the KEY NAME when the lookup misses entirely,
            // so a value equal to its own key is a miss the localizer will not throw over.
            var value = resources.Require(key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// The WIRE vocabulary is byte-identical whatever culture the app is running in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQ-UI-051's central risk, asserted directly. Tool names go to the model and into the
    /// per-workspace toggle tables; AppManager codes go on the wire; artefact paths name real files
    /// on disk; validator field keys are matched by the form. If any of them moved with the
    /// culture, a Hindi install would send a value no server understands, or address a file that
    /// does not exist.
    /// </para>
    /// <para>
    /// The trap is not hypothetical: <c>QdrantAdmin</c>'s daemon endpoint kind was once a string
    /// that WAS its English label and was parsed back to build the endpoint.
    /// </para>
    /// </remarks>
    [Fact]
    public void WireVocabularyIsTheSameInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = WireVocabulary();
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, WireVocabulary());
        }

        // And it is still the vocabulary the rest of the system expects, not merely a stable one.
        Assert.Contains("rag-search", english);
        Assert.Contains("InProgress", english);
        Assert.Contains("Medium", english);
        Assert.Contains("Bug", english);
        Assert.Contains("techiedesk.db", english);
    }

    /// <summary>
    /// The machine-text exemption list stays small, and every entry is still in use.
    /// </summary>
    /// <remarks>
    /// An exemption list is where a coverage gate goes to die: once "add it to the list" is the
    /// cheap fix, the list grows until the gate measures nothing. Capping it forces the argument to
    /// happen in review, and requiring each entry to still match something in the tree stops a
    /// stale exemption from quietly covering a string somebody added later.
    /// </remarks>
    [Fact]
    public void TheMachineTextListStaysSmallAndInUse()
    {
        Assert.True(
            ServiceStringCoverage.MachineText.Count <= 12,
            $"The service machine-text list has grown to {ServiceStringCoverage.MachineText.Count} " +
            "entries. Each one is a claim that a multi-word string is not UI text; past a dozen " +
            "that claim stops being reviewed and the ratchet stops measuring anything.");

        var root = ServiceStringCoverage.FindServicesRoot();
        var sources = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var entry in ServiceStringCoverage.MachineText)
        {
            Assert.True(
                sources.Any(source => source.Contains(entry, StringComparison.Ordinal)),
                $"'{entry}' is exempted as machine text but no service still contains it. Delete " +
                "it — a stale exemption silently covers whatever string matches it next.");
        }
    }

    /// <summary>Collects every service-owned value that is persisted, sent or matched.</summary>
    /// <returns>The wire vocabulary, in a stable order.</returns>
    private static string[] WireVocabulary() =>
    [
        .. SkillCatalog.Skills.Select(skill => skill.Name),
        .. SupportIssueCatalog.Types.Select(option => option.Code),
        .. SupportIssueCatalog.Priorities.Select(option => option.Code),
        .. SupportIssueCatalog.Statuses.Select(option => option.Code),
        SupportIssueCatalog.DefaultType,
        SupportIssueCatalog.DefaultPriority,
        .. DataStorageInspector.KnownArtefacts.Select(artefact => artefact.RelativePath),
        DataStorageInspector.UploadsDirectoryName,
        DataStorageInspector.ModelsDirectoryName,
        LlmConfigValidator.EndpointField,
        LlmConfigValidator.ApiKeyField,
        LlmConfigValidator.ModelField,
        LlmConfigValidator.ApiVersionField,
        .. Enum.GetValues<LlmSource>().Select(LlmConfigValidator.DefaultEndpoint),
        .. Enum.GetValues<LlmSource>().Select(LlmConfigValidator.DescribeSource)
    ];
}
