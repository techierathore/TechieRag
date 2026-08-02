using System.IO.Compression;
using System.Text;

using TechieDesk.Services.Backup;

using Xunit;

namespace TechieDesk.Tests.Backup;

/// <summary>
/// The archive never carries credentials (REQ-FN-046, BRD-144 clause 3).
/// </summary>
/// <remarks>
/// <para>
/// This is the test the requirement asks for by name, and it is written as a scan rather than as an
/// assertion about which tables the packer reads. A structural assertion would pass as long as the
/// code looked right; scanning every byte of a produced archive fails the moment a secret actually
/// reaches it, whatever route it took.
/// </para>
/// <para>
/// The scan covers the compressed file AND every decompressed entry. Checking only the raw file
/// would be worthless — deflate would hide a plaintext token from a substring search — and checking
/// only the entries would miss anything smuggled into the ZIP structure itself, such as an entry
/// name or a comment.
/// </para>
/// </remarks>
public sealed class CredentialExclusionTests : IDisposable
{
    /// <summary>Marker strings planted in every credential store a real install uses.</summary>
    private const string TokenSentinel = "SENTINEL-APPMANAGER-TOKEN-8f3a91c7";

    private readonly BackupTestHost host = new();
    private readonly string archivePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-credscan-{Guid.NewGuid():N}.tdbak");

    /// <summary>No secret material planted in the install appears anywhere in a produced archive.</summary>
    [Fact]
    public void AProducedArchiveCarriesNoCredentialMaterial()
    {
        host.CreateStores();
        host.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        host.SeedCredentials(TokenSentinel);

        // Prove the sentinel really was planted, or this test would pass against an install that
        // simply had no secrets in it.
        Assert.Contains(
            TokenSentinel,
            File.ReadAllText(Path.Combine(host.Directory, "connector-secrets.json")));

        host.Service.Export(archivePath, BackupScope.Instance);

        var raw = File.ReadAllBytes(archivePath);
        Assert.DoesNotContain(TokenSentinel, Encoding.UTF8.GetString(raw));

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain(TokenSentinel, entry.FullName);

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();

            Assert.False(
                content.Contains(TokenSentinel, StringComparison.Ordinal),
                $"Secret material reached the archive entry '{entry.FullName}'.");
        }
    }

    /// <summary>None of the credential-bearing artefacts is packed as an entry.</summary>
    /// <remarks>
    /// The complement of the byte scan. That scan proves no secret VALUE got in; this proves no
    /// secret-bearing FILE got in, which would still be a leak if its contents happened not to match
    /// the sentinel — an empty key ring, say, or a differently-encrypted token.
    /// </remarks>
    [Fact]
    public void NoCredentialBearingArtefactIsPackedAsAnEntry()
    {
        host.CreateStores();
        host.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        host.SeedCredentials(TokenSentinel);

        host.Service.Export(archivePath, BackupScope.Instance);

        string[] forbidden =
        [
            "connector-secrets.json", "techierag-config.json", "techiedesk.db", "keys", "key-test.xml"
        ];

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            foreach (var name in forbidden)
            {
                Assert.False(
                    entry.FullName.Contains(name, StringComparison.OrdinalIgnoreCase),
                    $"A credential-bearing artefact was packed: {entry.FullName}");
            }
        }
    }

    /// <summary>The manifest records the embedding model but never the provider's API key.</summary>
    /// <remarks>
    /// The manifest is the one place a credential could plausibly be argued into — it already
    /// describes the embedding provider, and a key is "part of" that provider's configuration. It is
    /// not, and this pins the distinction.
    /// </remarks>
    [Fact]
    public void TheManifestNamesTheModelButCarriesNoApiKey()
    {
        host.CreateStores();
        host.SeedCredentials(TokenSentinel);
        host.WriteConfig("bge-m3", 1024, apiKey: $"enc:v1:{TokenSentinel}");

        var outcome = host.Service.Export(archivePath, BackupScope.Instance);

        Assert.Equal("bge-m3", outcome.Manifest.Embedding.Model);

        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry(BackupArchive.ManifestEntryName);
        Assert.NotNull(manifestEntry);

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var manifestJson = reader.ReadToEnd();

        Assert.DoesNotContain(TokenSentinel, manifestJson);
        Assert.DoesNotContain("apiKey", manifestJson);
        Assert.DoesNotContain("enc:v1:", manifestJson);
    }

    /// <summary>Exporting never modifies the install it reads from.</summary>
    /// <remarks>
    /// Export opens both content databases in SQLite's read-only mode, so this holds at the driver
    /// level rather than by discipline. Asserted because an "export" that mutated live data would be
    /// the worst possible bug in a feature users run when they are worried about losing it.
    /// </remarks>
    [Fact]
    public void ExportingDoesNotModifyTheInstall()
    {
        host.CreateStores();
        host.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");

        var before = File.ReadAllBytes(host.RagStorePath);
        var vectorsBefore = File.ReadAllBytes(host.VectorDbPath);

        host.Service.Export(archivePath, BackupScope.Instance);

        Assert.Equal(before, File.ReadAllBytes(host.RagStorePath));
        Assert.Equal(vectorsBefore, File.ReadAllBytes(host.VectorDbPath));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        host.Dispose();
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch (IOException)
        {
            // A leftover temp archive must never fail a run.
        }
    }
}
