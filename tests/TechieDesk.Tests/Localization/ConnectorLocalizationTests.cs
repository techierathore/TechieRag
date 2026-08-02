using TechieDesk.Services.Connectors;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-055 (BRD-91): the CONNECTORS slice of the service layer hands screens resource keys, not
/// English, and the connector vocabulary that goes on the wire does not move with the culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes.</b> <c>IConnectorSecretStore.StorageDescription</c> returned an English
/// sentence and the connector hub and the connector editor interpolated it RAW into four otherwise
/// fully localized alerts — a Devanagari alert title above an English body, at
/// <c>ConnectorsHub.razor:180,189</c> and <c>ConnectorEdit.razor:495,505</c>. Beside it,
/// <c>ConnectorTypes</c> named every source in English, <c>ConnectorSettings.Describe</c> built the
/// line under every saved connector in English, and <c>ConnectorRunReport.Summary</c> built the whole
/// run-result sentence in English. None of it was visible to <see cref="RazorStringCoverage"/> or to
/// <see cref="CodeBlockStringCoverage"/>, because all four are composed in services.
/// </para>
/// <para>
/// <b>Everything here resolves through the real <see cref="ResourceHarness"/> in both shipped
/// languages.</b> Asserting on the key string alone would prove nothing: the whole class of defect
/// being replaced is a value that looks right in code and renders English on a translated install.
/// The Hindi assertions check the culture's OWN key set, because a key present in English and absent
/// from Hindi resolves to the ENGLISH value with <c>ResourceNotFound</c> false — which is the defect
/// wearing a green test as a disguise.
/// </para>
/// <para>
/// <b>What this does not cover.</b> Whether the Hindi is any GOOD; the connector refusal messages
/// that travel into <c>ConnectorException</c> and the run row's <c>FailureReason</c> (they cross into
/// the scheduling cluster's string contracts and are still English); and the per-item reasons
/// <c>RagConnectorDocumentSink</c> records, for the same reason.
/// </para>
/// </remarks>
public sealed class ConnectorLocalizationTests
{
    /// <summary>The two shipped languages, so every case below runs in both.</summary>
    public static TheoryData<string> Cultures => new() { "en", "hi" };

    /// <summary>
    /// Every key the connector service layer returns is present in BOTH cultures' own resources.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// Driven off the live tables and off the store's own constants rather than a hand-written list,
    /// so a fourth connector type is covered the day it is added.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryConnectorServiceKeyResolvesInBothLanguages(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var keys = new List<string>();
        keys.AddRange(ConnectorTypes.All.SelectMany(type => new[] { type.DisplayNameKey, type.DescriptionKey }));
        keys.Add(ConnectorSecretStore.OsStoreDescriptionKey);
        keys.Add(ConnectorSecretStore.EncryptedAtRestDescriptionKey);
        keys.Add(ConnectorSecretStore.InMemoryDescriptionKey);

        // A guard against the guard: an emptied table would make this pass over nothing.
        Assert.True(keys.Count >= 9, $"Only {keys.Count} connector-owned keys were collected.");

        var own = resources.OwnKeys;

        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by a connector service but missing from the {culture} " +
                $"resources, so whatever renders it shows English in a {culture} window.");

            var value = resources.Require(key);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// The connector type table carries KEYS, never the label itself.
    /// </summary>
    /// <remarks>
    /// The precise shape of the regression that made this requirement: a display column that happens
    /// to read as English. A key is a single token; a label is not.
    /// </remarks>
    [Fact]
    public void TheConnectorTypeTableCarriesKeysAndNotLabels()
    {
        Assert.NotEmpty(ConnectorTypes.All);

        foreach (var type in ConnectorTypes.All)
        {
            Assert.DoesNotContain(' ', type.DisplayNameKey);
            Assert.DoesNotContain(' ', type.DescriptionKey);
            Assert.NotEqual(type.DisplayNameKey, type.DescriptionKey);
        }
    }

    /// <summary>
    /// The connector WIRE vocabulary is byte-identical whatever culture the app runs in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The central risk of this change, asserted directly. A connector type code is the library
    /// connector's own <c>SourceType</c>; it is written into the <c>Connector</c> table, into
    /// <c>ConnectorJobPayload.ConnectorType</c> and onto every ingested document's metadata, and
    /// <c>DatabaseConnectorResolver.Build</c> switches on it to decide which connector to construct.
    /// If it moved with the culture, a Hindi install would save rows this build cannot open and would
    /// stamp metadata no citation could match.
    /// </para>
    /// <para>
    /// <c>INBOX</c> is here for the same reason at one level down: it is the folder name sent to an
    /// IMAP server in a <c>SELECT</c>, and the trap is exactly the one <c>QdrantAdmin</c>'s daemon
    /// endpoint kind fell into — a string that WAS its own English label and was parsed back.
    /// </para>
    /// </remarks>
    [Fact]
    public void ConnectorWireVocabularyIsTheSameInEveryCulture()
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
        Assert.Contains("repository", english);
        Assert.Contains("confluence", english);
        Assert.Contains("email", english);
        Assert.Contains("INBOX", english);
        Assert.Contains("techiedesk.connector.", english);
    }

    /// <summary>
    /// The credential-storage description is a KEY, and each of the three store states names its own.
    /// </summary>
    /// <remarks>
    /// This is the member the requirement singles out. The assertion is that the three states are
    /// distinguishable AND that none of them is a sentence: an implementation that "helpfully"
    /// returned English again would fail on the space, not on a string comparison somebody could
    /// update to match.
    /// </remarks>
    [Fact]
    public void TheCredentialStorageDescriptionIsAKeyForEveryStoreState()
    {
        string[] states =
        [
            ConnectorSecretStore.OsStoreDescriptionKey,
            ConnectorSecretStore.EncryptedAtRestDescriptionKey,
            ConnectorSecretStore.InMemoryDescriptionKey
        ];

        Assert.Equal(3, states.Distinct(StringComparer.Ordinal).Count());
        Assert.All(states, key => Assert.DoesNotContain(' ', key));
    }

    /// <summary>
    /// A saved connector's one-line summary renders in the reader's language, and the invariant parts
    /// of it survive untouched.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// The project path, the branch and the site URL are what the operator matches against the source
    /// itself. Translating any of them would produce a summary that names a repository nobody has.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void ASavedConnectorSummaryTranslatesTheWordsAndNotTheAddress(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var repository = new ConnectorSettings { ProjectPath = "acme/handbook", Branch = "release-7" }
            .Describe(ConnectorTypes.Repository, resources.Localize);

        Assert.Contains("acme/handbook", repository, StringComparison.Ordinal);
        Assert.Contains("release-7", repository, StringComparison.Ordinal);
        Assert.Contains("GitHub", repository, StringComparison.Ordinal);

        var confluence = new ConnectorSettings
        {
            BaseUrl = "https://acme.atlassian.net/wiki",
            SpaceKey = "ENG",
        }.Describe(ConnectorTypes.Confluence, resources.Localize);

        Assert.Contains("https://acme.atlassian.net/wiki", confluence, StringComparison.Ordinal);
        Assert.Contains("ENG", confluence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The summary of a connector whose branch was left to the host says so in the reader's language.
    /// </summary>
    /// <remarks>
    /// The one fragment of the repository summary that is a WORD rather than an address, so it is the
    /// one that must move between languages — and the one a naive "translate the whole line" fix
    /// would have got wrong in the other direction by translating the branch name beside it.
    /// </remarks>
    [Fact]
    public void TheDefaultBranchPhraseIsTranslatedAndTheRestIsNot()
    {
        var settings = new ConnectorSettings { ProjectPath = "acme/handbook" };

        string english;
        using (var resources = new ResourceHarness("en"))
        {
            english = settings.Describe(ConnectorTypes.Repository, resources.Localize);
        }

        using (var resources = new ResourceHarness("hi"))
        {
            var hindi = settings.Describe(ConnectorTypes.Repository, resources.Localize);

            Assert.NotEqual(english, hindi);
            Assert.Contains("default branch", english, StringComparison.Ordinal);
            Assert.DoesNotContain("default branch", hindi, StringComparison.Ordinal);
            Assert.Contains("acme/handbook", hindi, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A run report's summary renders in the reader's language and still refuses to overstate itself.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    /// <remarks>
    /// The honesty rule is the point of <c>ConnectorRunReport</c> and it must survive translation: a
    /// run that ingested one document and dropped three items may not read as a clean success in any
    /// language. Asserted structurally — the summary of a partial run is not the summary of a clean
    /// one — rather than by matching Hindi words, which would only test the translation.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void APartialRunNeverSummarizesAsACleanOne(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var clean = Report(ingested: 2, failed: 0, skipped: 0).SummaryText(resources.Localize);
        var partial = Report(ingested: 2, failed: 1, skipped: 2).SummaryText(resources.Localize);
        var nothing = Report(ingested: 0, failed: 3, skipped: 0).SummaryText(resources.Localize);

        Assert.NotEqual(clean, partial);
        Assert.NotEqual(clean, nothing);
        Assert.All(
            new[] { clean, partial, nothing },
            line => Assert.False(string.IsNullOrWhiteSpace(line)));

        // The counts are the operator's evidence and are digits in both scripts.
        Assert.Contains("2", clean, StringComparison.Ordinal);
        Assert.Contains("1", partial, StringComparison.Ordinal);
        Assert.Contains("3", nothing, StringComparison.Ordinal);
    }

    /// <summary>
    /// The run summary is genuinely different in Hindi — the keys resolve, they do not fall through.
    /// </summary>
    /// <remarks>
    /// The check that catches the failure mode <see cref="ResourceHarness.OwnKeys"/> documents: a key
    /// added to <c>AppStrings.resx</c> and forgotten in <c>AppStrings.hi.resx</c> resolves to the
    /// English text with no error at all. Comparing the two renderings is what sees it.
    /// </remarks>
    [Fact]
    public void TheRunSummaryActuallyChangesLanguage()
    {
        var report = Report(ingested: 4, failed: 1, skipped: 1);

        string english;
        using (var resources = new ResourceHarness("en"))
        {
            english = report.SummaryText(resources.Localize);
        }

        using (var resources = new ResourceHarness("hi"))
        {
            var hindi = report.SummaryText(resources.Localize);

            Assert.NotEqual(english, hindi);
            Assert.Contains("Ingested", english, StringComparison.Ordinal);
            Assert.DoesNotContain("Ingested", hindi, StringComparison.Ordinal);
            Assert.DoesNotContain("could not be read", hindi, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pluralization goes through the resources, so a Hindi count is never an English plural.
    /// </summary>
    /// <remarks>
    /// The old code appended an <c>s</c>. "4 दस्तावेज़s" is the tell-tale of a counter that was
    /// wrapped rather than translated, and it is invisible to a test that only checks the key exists.
    /// </remarks>
    [Fact]
    public void CountsAreNeverAnEnglishPluralInHindi()
    {
        using var resources = new ResourceHarness("hi");

        var many = Report(ingested: 4, failed: 0, skipped: 0).SummaryText(resources.Localize);
        var one = Report(ingested: 1, failed: 0, skipped: 0).SummaryText(resources.Localize);

        Assert.DoesNotContain("documents", many, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document", one, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4", many, StringComparison.Ordinal);
        Assert.Contains("1", one, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connector type this build does not know has no name key, so the caller shows its stored code.
    /// </summary>
    /// <remarks>
    /// Null rather than a substitute key. A row written by a newer build names a source this one
    /// cannot open, and labelling it with another type's translated name would tell the operator they
    /// are looking at something they are not.
    /// </remarks>
    [Fact]
    public void AnUnknownConnectorTypeHasNoNameKey()
    {
        Assert.Null(ConnectorTypes.DisplayNameKey("sharepoint"));
        Assert.Null(ConnectorTypes.DisplayNameKey(null));
        Assert.Equal("ConnectorTypeRepositoryName", ConnectorTypes.DisplayNameKey(ConnectorTypes.Repository));
    }

    /// <summary>Builds a report with the requested mix of outcomes.</summary>
    /// <param name="ingested">How many items reached the library.</param>
    /// <param name="failed">How many could not be read.</param>
    /// <param name="skipped">How many were deliberately not read.</param>
    /// <returns>The report.</returns>
    private static ConnectorRunReport Report(int ingested, int failed, int skipped)
    {
        var items = new List<ConnectorRunItem>();
        items.AddRange(Enumerable.Range(0, ingested).Select(index => Item($"i{index}", RunItemStatus.Processed)));
        items.AddRange(Enumerable.Range(0, failed).Select(index => Item($"f{index}", RunItemStatus.Failed)));
        items.AddRange(Enumerable.Range(0, skipped).Select(index => Item($"s{index}", RunItemStatus.Skipped)));

        return new ConnectorRunReport(
            1,
            null,
            "Handbook",
            RunTrigger.Manual,
            RunOutcome.Succeeded,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow,
            null,
            null,
            items);
    }

    /// <summary>Builds one recorded item.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="status">What happened to it.</param>
    /// <returns>The item.</returns>
    private static ConnectorRunItem Item(string id, RunItemStatus status) =>
        new(id, $"{id}.md", status, null, DateTime.UtcNow);

    /// <summary>Collects every connector value that is persisted, sent, or switched on.</summary>
    /// <returns>The wire vocabulary, in a stable order.</returns>
    private static string[] WireVocabulary() =>
    [
        .. ConnectorTypes.All.Select(type => type.ConnectorType),
        ConnectorTypes.Repository,
        ConnectorTypes.Confluence,
        ConnectorTypes.Email,
        ConnectorSettings.DefaultMailFolder,
        ConnectorSecretStore.SecretKeyPrefix,
        ConnectorSecretStore.EncryptedPrefix,
        ConnectorSecretStore.SecretFileName,
        ConnectorJobHandler.Kind
    ];
}
