using System.Net.Sockets;
using System.Security.Authentication;
using TechieDesk.Services;
using TechieDesk.Services.Backup;
using TechieDesk.Services.Install;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Localization;

/// <summary>
/// REQ-UI-055 (BRD-91): the backup, Docker and single-instance services hand the presentation layer
/// resource KEYS, and the values those keys construct connections and archives from stay
/// culture-invariant.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three defects this closes.</b> <c>BackupService.BlockDetail</c> rendered at
/// <c>BackupRestore.razor</c> under a title that WAS localized, so a Hindi user read a Devanagari
/// heading over an English restore refusal. <c>DockerContainerService</c>'s failure reason and
/// <c>DockerDaemonEndpoint</c>'s security warning rendered untranslated on <c>QdrantAdmin</c>.
/// <c>SingleInstanceState</c>'s refusal was English by an explicit decision that has since stopped
/// being true — see <see cref="ALocalizerIsResolvableOnTheSecondInstancePath"/>.
/// </para>
/// <para>
/// <b>The risk being guarded, and it is not the captions.</b> <c>QdrantAdmin</c>'s daemon endpoint
/// kind was once a string that WAS its English label and was parsed back by <c>ParseKind</c> to
/// build the endpoint. Localizing that would have made a Hindi install construct a socket path out
/// of Devanagari. <see cref="LocalizingTheLabelNeverMovesTheEndpointItself"/> and
/// <see cref="ArchiveEntryNamesAreIdenticalInEveryCulture"/> are the two tests that hold the line.
/// </para>
/// </remarks>
public sealed class BackupDockerInstanceStringTests
{
    /// <summary>Every key these three services can return resolves in both shipped languages.</summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The positive half. Driven off the CONSTANTS the services expose rather than a hand-copied
    /// list, so a refusal added tomorrow is covered the day it is written — and off the culture's
    /// OWN key set, because a key present in English and missing from Hindi resolves to the English
    /// value with <c>ResourceNotFound</c> false, which is the defect rather than the fix.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryRefusalKeyResolvesInBothLanguages(string culture)
    {
        using var resources = new ResourceHarness(culture);
        var own = resources.OwnKeys;

        foreach (var key in AllKeys())
        {
            Assert.DoesNotContain(' ', key);
            Assert.True(
                own.Contains(key),
                $"'{key}' is returned by a backup/Docker/single-instance service but is missing " +
                $"from the {culture} resources, so that screen renders English (or the key name) " +
                $"in a {culture} window.");

            // ResourceManagerStringLocalizer returns the KEY NAME on a total miss, so a value equal
            // to its own key is a miss the localizer will not throw over.
            var value = resources.Localizer[key].Value;
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// Localizing the daemon-kind LABEL never moves the URI, the client URI or the persisted
    /// display value.
    /// </summary>
    /// <remarks>
    /// The trap REQ-UI-051 already fell into once, asserted directly rather than assumed. Everything
    /// that constructs a socket path or a URL is compared byte for byte between English and Hindi;
    /// only the label is allowed to differ, and it is asserted to actually DIFFER, because a
    /// "localized" label that came back identical would mean the lookup silently missed.
    /// </remarks>
    [Fact]
    public void LocalizingTheLabelNeverMovesTheEndpointItself()
    {
        string[] english;
        string englishLabel;
        using (var resources = new ResourceHarness("en"))
        {
            english = EndpointWireVocabulary();
            englishLabel = resources.Require(
                DockerDaemonEndpoint.KindLabelKeyFor(DockerDaemonEndpointKind.RemoteTls));
        }

        using (var resources = new ResourceHarness("hi"))
        {
            Assert.Equal(english, EndpointWireVocabulary());

            var hindiLabel = resources.Require(
                DockerDaemonEndpoint.KindLabelKeyFor(DockerDaemonEndpointKind.RemoteTls));
            Assert.NotEqual(englishLabel, hindiLabel);
        }

        // And it is still the vocabulary Docker.DotNet expects, not merely a stable one.
        Assert.Contains("unix:///var/run/docker.sock", english);
        Assert.Contains("tcps://qdrant-host.lan:2376", english);
        Assert.Contains("https", english);
        Assert.Contains("tcp", english);
    }

    /// <summary>
    /// A daemon endpoint built through the UI path is byte-identical in Hindi and in English.
    /// </summary>
    /// <remarks>
    /// <see cref="DockerDaemonEndpoint.FromKind"/> is what the Qdrant admin screen calls when the
    /// operator presses Apply. If any part of it consulted the culture, a Hindi install would drive
    /// a different daemon from the one the same clicks produce in English.
    /// </remarks>
    [Theory]
    [InlineData(DockerDaemonEndpointKind.LocalSocket, "")]
    [InlineData(DockerDaemonEndpointKind.NetworkHost, "qdrant-host.lan")]
    [InlineData(DockerDaemonEndpointKind.RemoteTls, "qdrant-host.lan:2376")]
    public void EndpointConstructionIsIdenticalInEveryCulture(
        DockerDaemonEndpointKind kind, string address)
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = Describe(DockerDaemonEndpoint.FromKind(kind, address));
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, Describe(DockerDaemonEndpoint.FromKind(kind, address)));
        }
    }

    /// <summary>
    /// The <c>.tdbak</c> entry names a restore reads back are untouched by localization.
    /// </summary>
    /// <remarks>
    /// REQ-FN-046/047 and <see cref="BackupArchive.IsKnownEntryName"/> match archive entries by
    /// name. An entry name that moved with the culture would make an archive written on a Hindi
    /// install unreadable on an English one — a data-loss defect, not a caption defect. The entry
    /// names travel through the refusal ARGUMENTS, which is exactly why this is asserted here.
    /// </remarks>
    [Fact]
    public void ArchiveEntryNamesAreIdenticalInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = ArchiveWireVocabulary();
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, ArchiveWireVocabulary());
        }

        Assert.Contains(BackupArchive.ManifestEntryName, english);
        Assert.Contains(BackupArchive.FileExtension, english);

        // And they are still names a restore will actually accept, not merely stable ones.
        Assert.True(BackupArchive.IsKnownEntryName(BackupArchive.ManifestEntryName));
        Assert.All(BackupArchive.ContentEntryNames, name => Assert.True(
            BackupArchive.IsKnownEntryName(name),
            $"'{name}' is no longer an entry name this build will extract."));
    }

    /// <summary>
    /// A localizer IS resolvable on the refused-second-instance path, so the refusal can be
    /// translated without opening the database the live copy is writing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SecondInstancePage</c> used to record that a refused instance had "no localized culture in
    /// place to read from". Half of that was right. <c>MauiProgram</c> calls
    /// <c>RegisterAppServices</c> — and therefore <c>AddTechieDeskAppearance</c>'s
    /// <c>AddLocalization</c> — and calls <c>builder.Build()</c> BEFORE its
    /// <c>IsPrimaryInstance</c> early return, so the localizer exists. What is skipped is
    /// <c>ApplyStoredLanguage</c>, which reads the app database and must stay skipped.
    /// </para>
    /// <para>
    /// So the refusal resolves against whatever <c>CultureInfo.CurrentUICulture</c> the process
    /// starts with — the operating system's language on a desktop head. This test proves the part
    /// that path actually depends on: a container built from nothing but <c>AddLocalization</c>
    /// resolves the refusal in Hindi, with no store, no repository and no file touched.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALocalizerIsResolvableOnTheSecondInstancePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tdrefusal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var first = SingleInstanceGuard.TryAcquire(directory);
        using var held = first.Lock;
        var state = new SingleInstanceState(SingleInstanceGuard.TryAcquire(directory));

        Assert.False(state.IsPrimaryInstance);

        using var resources = new ResourceHarness("hi");

        var title = resources.Require(SingleInstanceState.RefusalTitleKey);
        var detail = resources.Require(state.RefusalDetailKey, [.. state.RefusalDetailArguments]);
        var button = resources.Require(SingleInstanceState.RefusalCloseButtonKey);

        Assert.Contains(directory, detail, StringComparison.Ordinal);
        Assert.Contains(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            detail,
            StringComparison.Ordinal);
        Assert.All(
            new[] { title, detail, button },
            text => Assert.Contains(text, character => character is >= 'ऀ' and <= 'ॿ'));
    }

    /// <summary>
    /// Machine text relayed from Docker or from the runtime is carried as an ARGUMENT and is never
    /// rewritten by translation.
    /// </summary>
    /// <remarks>
    /// A daemon's response body, a <see cref="SocketError"/> name and an exception's own message are
    /// not ours to translate: rewriting them would invent an error the daemon never emitted and
    /// would make the real one unsearchable. They therefore appear verbatim in both languages, and
    /// only the sentence around them moves.
    /// </remarks>
    [Fact]
    public void MachineTextIsRelayedVerbatimInEveryLanguage()
    {
        var endpoint = DockerDaemonEndpoint.Parse("tcps://qdrant-host.lan:2376");
        var problem = DockerContainerService.DescribeFailure(
            endpoint,
            new HttpRequestException(
                "boom", new AuthenticationException("remote certificate is invalid")));

        Assert.Equal(DockerContainerService.TlsHandshakeFailedKey, problem.MessageKey);
        Assert.Contains("remote certificate is invalid", problem.Arguments);

        foreach (var culture in new[] { "en", "hi" })
        {
            using var resources = new ResourceHarness(culture);
            var rendered = resources.Require(problem.MessageKey, [.. problem.Arguments]);

            Assert.Contains("remote certificate is invalid", rendered, StringComparison.Ordinal);
            Assert.Contains("tcps://qdrant-host.lan:2376", rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The refusal a blocked restore shows is Devanagari in Hindi, not English under a Hindi title.
    /// </summary>
    /// <remarks>
    /// The named defect, asserted at the render contract rather than through the file system: every
    /// refusal key the service can return produces text in Devanagari script when the reader's
    /// culture is Hindi. Asserting the KEY alone would pass forever against an English value.
    /// </remarks>
    [Fact]
    public void EveryRestoreRefusalIsWrittenInDevanagariForAHindiReader()
    {
        using var resources = new ResourceHarness("hi");

        foreach (var key in BackupKeys())
        {
            var value = resources.Require(key, SampleArguments(key));
            Assert.Contains(value, character => character is >= 'ऀ' and <= 'ॿ');
        }
    }

    /// <summary>
    /// A sync folder with no brand behind it is named through a KEY, so the warning is Devanagari
    /// end to end rather than a Hindi sentence wrapped round an English noun phrase.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// <para>
    /// macOS mounts every modern OneDrive, Dropbox and Google Drive client under
    /// <c>Library/CloudStorage</c>, and from the path alone there is no way to tell which. That
    /// branch used to return the English words "a cloud-storage provider", which were interpolated
    /// straight into the localized alert TITLE.
    /// </para>
    /// <para>
    /// It is asserted separately from the branded matches because the branded ones legitimately stay
    /// in Latin script — a product noun is written as itself inside a Devanagari sentence — so a
    /// test that swept both together would pass on the branded case and never exercise this one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void AnUnbrandedSyncFolderIsNamedThroughAKey(string culture)
    {
        var match = SyncFolderDetector.Detect("/Users/sam/Library/CloudStorage/SomeVendor/TechieDesk");
        Assert.NotNull(match);
        Assert.Equal(SyncFolderDetector.GenericProviderKey, match!.NameKey);

        using var resources = new ResourceHarness(culture);
        var name = SyncFolderDetector.ProductName(match, resources.Localize);

        Assert.NotEqual(SyncFolderDetector.GenericProviderKey, name);
        Assert.False(string.IsNullOrWhiteSpace(name));

        var warning = resources.Require(SyncFolderDetector.DataDirectoryRiskKey, name);
        Assert.Contains(name, warning, StringComparison.Ordinal);

        if (culture == "hi")
        {
            Assert.Contains(name, character => character is >= 'ऀ' and <= 'ॿ');
        }
    }

    /// <summary>A BRANDED sync folder keeps its brand in Latin script in both languages.</summary>
    /// <remarks>
    /// The mirror of the test above, and the reason the two are not one. <c>Dropbox</c> is a product
    /// noun: the localization standard keeps it in Latin script inside the Hindi sentence, so a
    /// "translated" brand would be the defect here rather than the fix.
    /// </remarks>
    [Fact]
    public void ABrandedSyncFolderKeepsItsBrandInEveryLanguage()
    {
        var match = SyncFolderDetector.Detect("/Users/sam/Dropbox/TechieDesk");
        Assert.NotNull(match);
        Assert.Null(match!.NameKey);

        foreach (var culture in new[] { "en", "hi" })
        {
            using var resources = new ResourceHarness(culture);
            Assert.Equal("Dropbox", SyncFolderDetector.ProductName(match, resources.Localize));
        }
    }

    /// <summary>Collects every resource key this slice's services can return.</summary>
    /// <returns>The keys, without duplicates.</returns>
    private static IEnumerable<string> AllKeys() =>
        BackupKeys()
            .Concat(DockerKeys())
            .Concat(
            [
                SingleInstanceState.RefusalTitleKey,
                SingleInstanceState.RefusalMessageKey,
                SingleInstanceState.RefusalMessageWithOwnerKey,
                SingleInstanceState.RefusalCloseButtonKey
            ])
            .Distinct(StringComparer.Ordinal);

    /// <summary>The restore-refusal and sync-warning keys owned by <c>Services/Backup</c>.</summary>
    /// <returns>The keys, in declaration order.</returns>
    private static string[] BackupKeys() =>
    [
        BackupService.BlockFileMissingKey,
        BackupService.BlockNotReadableKey,
        BackupService.BlockNoManifestKey,
        BackupService.BlockManifestUnreadableKey,
        BackupService.BlockManifestEmptyKey,
        BackupService.BlockNewerFormatKey,
        BackupService.BlockUnsafeEntryKey,
        BackupService.BlockEntryMissingKey,
        BackupService.BlockEntryLengthMismatchKey,
        BackupService.BlockEntryChecksumMismatchKey,
        BackupService.BlockManifestIncompleteKey,
        BackupService.BlockEmbeddingMismatchKey,
        SyncFolderDetector.DataDirectoryRiskKey,
        SyncFolderDetector.GenericProviderKey
    ];

    /// <summary>The daemon keys owned by the Docker service and the endpoint type.</summary>
    /// <returns>The keys, in declaration order.</returns>
    private static string[] DockerKeys() =>
    [
        DockerDaemonEndpoint.PlainTcpWarningKey,
        DockerDaemonEndpoint.TlsVerificationDisabledWarningKey,
        DockerDaemonEndpoint.InvalidEndpointKey,
        DockerDaemonEndpoint.UnsupportedSchemeKey,
        DockerDaemonEndpoint.MissingHostKey,
        DockerDaemonEndpoint.MissingNetworkAddressKey,
        DockerDaemonEndpoint.MissingTlsAddressKey,
        .. Enum.GetValues<DockerDaemonEndpointKind>().Select(DockerDaemonEndpoint.KindLabelKeyFor),
        DockerContainerService.RefusedRequestKey,
        DockerContainerService.TlsHandshakeFailedKey,
        DockerContainerService.ConnectionRefusedKey,
        DockerContainerService.HostNotResolvedKey,
        DockerContainerService.ConnectTimedOutKey,
        DockerContainerService.HostUnreachableKey,
        DockerContainerService.SocketFailureKey,
        DockerContainerService.NoAnswerKey,
        DockerContainerService.NamedPipeMissingKey,
        DockerContainerService.LocalSocketMissingKey,
        DockerContainerService.UnexpectedFailureKey,
        DockerContainerService.NoLogOutputKey
    ];

    /// <summary>Supplies enough placeholder values for a key to format without throwing.</summary>
    /// <param name="key">The key about to be resolved.</param>
    /// <returns>Three sample arguments, or none for a key that carries no placeholder.</returns>
    /// <remarks>
    /// Three is one more than any key in this slice needs and <c>string.Format</c> ignores the
    /// surplus, so this cannot drift as keys gain a placeholder — whereas a per-key argument table
    /// would silently stop matching.
    /// </remarks>
    private static object?[] SampleArguments(string key) =>
        key == SyncFolderDetector.GenericProviderKey ? [] : ["one", "two", "three"];

    /// <summary>Collects every endpoint value that builds a socket path, a URL or a setting.</summary>
    /// <returns>The wire vocabulary, in a stable order.</returns>
    private static string[] EndpointWireVocabulary() =>
    [
        DockerDaemonEndpoint.UnixSocketEndpoint,
        DockerDaemonEndpoint.WindowsPipeEndpoint,
        .. Describe(DockerDaemonEndpoint.Parse("unix:///var/run/docker.sock")),
        .. Describe(DockerDaemonEndpoint.Parse("tcp://qdrant-host.lan")),
        .. Describe(DockerDaemonEndpoint.Parse("tcps://qdrant-host.lan"))
    ];

    /// <summary>Renders the parts of an endpoint that a machine consumes.</summary>
    /// <param name="endpoint">The endpoint to describe.</param>
    /// <returns>Its display value, its URI, its client URI and its scheme.</returns>
    private static string[] Describe(DockerDaemonEndpoint endpoint) =>
    [
        endpoint.Display,
        endpoint.Uri.OriginalString,
        endpoint.ClientUri.OriginalString,
        endpoint.ClientUri.Scheme,
        endpoint.Kind.ToString()
    ];

    /// <summary>Collects every name a <c>.tdbak</c> round-trips.</summary>
    /// <returns>The archive entry names plus the file extension, in a stable order.</returns>
    private static string[] ArchiveWireVocabulary() =>
    [
        BackupArchive.ManifestEntryName,
        .. BackupArchive.ContentEntryNames,
        BackupArchive.FileExtension
    ];
}
