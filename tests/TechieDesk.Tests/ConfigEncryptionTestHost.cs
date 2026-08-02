using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;

namespace TechieDesk.Tests;

/// <summary>
/// Disposable, isolated on-disk sandbox used by the REQ-NFR-004 encryption-at-rest tests.
/// </summary>
/// <remarks>
/// Provides a throwaway content root (where <c>techierag-config.json</c> is written) plus a separate
/// Data Protection key-ring directory, so a test can build a brand new provider over the same key
/// ring and prove that encrypted values survive an application restart.
/// </remarks>
public sealed class ConfigEncryptionTestHost : IDisposable
{
    /// <summary>Creates the sandbox directories under the system temp folder.</summary>
    public ConfigEncryptionTestHost()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "techiedesk-encryption-tests", Guid.NewGuid().ToString("N"));
        ContentRootPath = Path.Combine(RootPath, "app");
        DataDirectoryPath = Path.Combine(RootPath, "data");
        KeyRingPath = Path.Combine(RootPath, "keys");
        Directory.CreateDirectory(ContentRootPath);
        Directory.CreateDirectory(DataDirectoryPath);
        Directory.CreateDirectory(KeyRingPath);
    }

    /// <summary>Gets the sandbox root directory.</summary>
    public string RootPath { get; }

    /// <summary>Gets the simulated application content root.</summary>
    public string ContentRootPath { get; }

    /// <summary>Gets the sandbox data directory the service under test is pointed at.</summary>
    /// <remarks>
    /// REQ-FN-037: the data directory is now the per-user OS location and no longer derives from the
    /// content root, so a test must steer it with the <c>AppDb:DataDirectory</c> override — the one
    /// remaining input that changes the resolved root. Without this the encryption tests would write
    /// into the real <c>~/Library/Application Support/TechieDesk</c>.
    /// </remarks>
    public string DataDirectoryPath { get; }

    /// <summary>Gets the directory holding the persisted Data Protection key ring.</summary>
    public string KeyRingPath { get; }

    /// <summary>Gets the path of the saved TechieRag configuration file.</summary>
    /// <remarks>
    /// Lives in the one data directory every persistent artefact shares (REQ-FN-034/REQ-FN-037).
    /// </remarks>
    public string ConfigFilePath =>
        Path.Combine(TechieDeskDb.DataDirectory.Resolve(DataDirectoryPath),
            TechieDeskDb.DataDirectory.ConfigFileName);

    /// <summary>
    /// Creates a Data Protection provider whose key ring is persisted to <see cref="KeyRingPath"/>.
    /// </summary>
    /// <returns>A file-system backed provider, equivalent to a freshly started application.</returns>
    public IDataProtectionProvider CreateProvider() =>
        DataProtectionProvider.Create(new DirectoryInfo(KeyRingPath));

    /// <summary>
    /// Creates a configuration service bound to this sandbox, simulating one application lifetime.
    /// </summary>
    /// <param name="provider">Optional provider; a new one over the same key ring by default.</param>
    /// <returns>A configuration service with an empty in-memory cache.</returns>
    public TechieRagConfigService CreateConfigService(IDataProtectionProvider? provider = null) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [TechieDeskDb.DataDirectory.ConfigKey] = DataDirectoryPath
                })
                .Build(),
            // REQ-FN-035: was a six-member IWebHostEnvironment stub. IAppEnvironment exposes only the
            // content root the service actually uses, so the real type serves the test directly.
            new AppEnvironment(ContentRootPath),
            NullLogger<TechieRagConfigService>.Instance,
            provider ?? CreateProvider(),
            NullLoggerFactory.Instance);

    /// <summary>
    /// Creates a TechieRag manager bound to this sandbox, simulating one application lifetime
    /// (REQ-FN-052).
    /// </summary>
    /// <param name="provider">Optional provider; a new one over the same key ring by default.</param>
    /// <returns>A manager whose read path is pointed at this sandbox's data directory.</returns>
    /// <remarks>
    /// The manager is the READ side of the round trip REQ-FN-052 exists to pin down: LLM Settings
    /// writes through <see cref="TechieRagConfigService"/> and the running RAG instance is built from
    /// whatever this reads. Both are constructed from the same <c>AppDb:DataDirectory</c> override, so
    /// a test can assert they name the same file instead of assuming it.
    /// </remarks>
    public TechieRagManager CreateRagManager(IDataProtectionProvider? provider = null) =>
        new(
            new AppEnvironment(ContentRootPath),
            NullLoggerFactory.Instance,
            NullLogger<TechieRagManager>.Instance,
            provider ?? CreateProvider(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [TechieDeskDb.DataDirectory.ConfigKey] = DataDirectoryPath
                })
                .Build());

    /// <summary>Reads the saved configuration file exactly as it sits on disk.</summary>
    /// <returns>The raw file text, or an empty string when the file is absent.</returns>
    public string ReadRawConfigFile() =>
        File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;

    /// <summary>Removes the sandbox directories.</summary>
    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
