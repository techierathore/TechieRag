using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TechieDesk.Services.Auth;
using TechieDeskDb;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// The default <see cref="IConnectorSecretStore"/>: the OS credential store first, an encrypted
/// sidecar second, and never the application database (REQ-FN-039, REQ-NFR-004b).
/// </summary>
/// <remarks>
/// <para><b>Strongest scheme first, exactly as <see cref="TechieRagConfigProtector"/> does it.</b>
/// When <see cref="ISecretStore.IsDurable"/> is true the token goes to Keychain / the Credential
/// Manager under <c>techiedesk.connector.&lt;id&gt;</c> and nothing sensitive touches the disk at
/// all.</para>
/// <para><b>The known Mac Catalyst constraint, handled rather than fought.</b> Keychain access
/// depends on the app's entitlements and code signature, so an unsigned developer build is refused
/// with <c>errSecMissingEntitlement</c> and <see cref="OsCredentialStore"/> degrades to an in-memory
/// store. In-memory alone would mean the operator re-typing a personal access token on every launch,
/// so this class falls back to the REQ-NFR-004b scheme instead: ASP.NET Core Data Protection, written
/// as <c>enc:v1:&lt;payload&gt;</c> into a sidecar file under the data directory, machine- and
/// user-bound by the persisted key ring. That is weaker than the keychain — a machine-bound file is
/// not a keychain — and it is still not cleartext, and it is still not the database.</para>
/// <para><b>The database never participates.</b> There is no code path here that returns a value for
/// the caller to store on a row, and none that reads one. <see cref="ConnectorDefinition.CredentialRef"/>
/// is a name, and this class is the only thing that turns a name into a value.</para>
/// <para><b>Nothing here is logged but counts and outcomes.</b> Not the token, not the ciphertext,
/// not the file's contents.</para>
/// </remarks>
public sealed class ConnectorSecretStore : IConnectorSecretStore
{
    /// <summary>The prefix connector tokens are stored under in the OS credential store.</summary>
    public const string SecretKeyPrefix = "techiedesk.connector.";

    /// <summary>The prefix on every Data-Protection-encrypted value in the sidecar file.</summary>
    public const string EncryptedPrefix = "enc:v1:";

    /// <summary>The sidecar file holding encrypted tokens when no durable OS store is available.</summary>
    public const string SecretFileName = "connector-secrets.json";

    /// <summary>Resource key for the description used when the OS credential store accepted this build.</summary>
    public const string OsStoreDescriptionKey = "ConnectorCredentialsInOsStore";

    /// <summary>Resource key for the description used when tokens fall back to encryption at rest.</summary>
    public const string EncryptedAtRestDescriptionKey = "ConnectorCredentialsEncryptedAtRest";

    /// <summary>Resource key for the description used when nothing durable is available at all.</summary>
    public const string InMemoryDescriptionKey = "ConnectorCredentialsInMemoryOnly";

    private const string ProtectorPurpose = "TechieDesk.Connectors.AccessTokens.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ISecretStore secrets;
    private readonly IDataProtector? protector;
    private readonly ILogger<ConnectorSecretStore> logger;
    private readonly string secretFilePath;
    private readonly object gate = new();

    /// <summary>Initializes a new instance of the <see cref="ConnectorSecretStore"/> class.</summary>
    /// <param name="secrets">The app's OS credential store abstraction (REQ-FN-039).</param>
    /// <param name="configuration">Application configuration, used to locate the data directory.</param>
    /// <param name="logger">Diagnostics. Never receives a token or a ciphertext.</param>
    /// <param name="dataProtectionProvider">
    /// The host's Data Protection provider, used only for the fallback. Optional: a host that does not
    /// register one (the scheduler helper) simply has no fallback, and reports itself not durable
    /// rather than inventing a weaker one.
    /// </param>
    public ConnectorSecretStore(
        ISecretStore secrets,
        IConfiguration configuration,
        ILogger<ConnectorSecretStore> logger,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        protector = dataProtectionProvider?.CreateProtector(ProtectorPurpose);
        secretFilePath = Path.Combine(
            DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]), SecretFileName);
    }

    /// <inheritdoc />
    public bool IsDurable => secrets.IsDurable || protector is not null;

    /// <inheritdoc />
    public string StorageDescriptionKey => secrets.IsDurable
        ? OsStoreDescriptionKey
        : protector is not null
            ? EncryptedAtRestDescriptionKey
            : InMemoryDescriptionKey;

    /// <inheritdoc />
    public string? Read(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        var key = SecretKeyPrefix + connectorId;

        // The OS store is consulted first even when it is not currently durable, so a value written
        // by an earlier, entitled run of the same app is still found.
        var stored = secrets.Read(key);
        return string.IsNullOrEmpty(stored) ? ReadFromFile(key) : stored;
    }

    /// <inheritdoc />
    public void Write(string connectorId, string? secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        if (string.IsNullOrWhiteSpace(secret))
        {
            Delete(connectorId);
            return;
        }

        var key = SecretKeyPrefix + connectorId;
        if (secrets.IsDurable)
        {
            secrets.Write(key, secret);

            // A token that has been promoted into the keychain must not be left behind in the
            // weaker store as well; two copies means the weaker one outlives a revocation.
            RemoveFromFile(key);
            logger.LogInformation(
                "Stored the access token for connector {ConnectorId} in the OS credential store "
                + "(REQ-FN-039); the application database holds only a reference",
                connectorId);
            return;
        }

        if (protector is not null)
        {
            WriteToFile(key, EncryptedPrefix + protector.Protect(secret));
            logger.LogWarning(
                "The OS credential store is not available to this build, so the access token for "
                + "connector {ConnectorId} was encrypted at rest with a machine-bound key instead "
                + "(REQ-NFR-004b). It is not in the application database",
                connectorId);
            return;
        }

        secrets.Write(key, secret);
        logger.LogWarning(
            "No durable secret store is available in this host, so the access token for connector "
            + "{ConnectorId} is held in memory for this run only and must be re-entered after a "
            + "restart. Nothing was written to disk",
            connectorId);
    }

    /// <inheritdoc />
    public bool Delete(string connectorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        var key = SecretKeyPrefix + connectorId;

        // Both locations, always. A delete that cleared only the store currently in use would leave
        // a revoked token recoverable from the other one.
        var removedFromStore = secrets.Delete(key);
        var removedFromFile = RemoveFromFile(key);
        return removedFromStore || removedFromFile;
    }

    /// <summary>Reads and decrypts one value from the sidecar file.</summary>
    /// <param name="key">The store key.</param>
    /// <returns>The token, or <see langword="null"/> when absent or unreadable.</returns>
    private string? ReadFromFile(string key)
    {
        if (protector is null)
        {
            return null;
        }

        var value = ReadAll().GetValueOrDefault(key);
        if (string.IsNullOrEmpty(value) || !value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(value[EncryptedPrefix.Length..]);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Deliberately logs neither the ciphertext nor the exception payload. A rotated or
            // missing key ring means the token cannot be recovered, and the honest outcome is
            // "re-enter it" rather than a run that silently reads the source anonymously.
            logger.LogError(
                "A stored connector access token could not be decrypted ({Reason}); it was ignored. "
                + "Re-enter the token on the connector",
                ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Writes one already-encrypted value into the sidecar file.</summary>
    /// <param name="key">The store key.</param>
    /// <param name="protectedValue">The <c>enc:v1:</c> payload.</param>
    private void WriteToFile(string key, string protectedValue)
    {
        lock (gate)
        {
            var all = ReadAllLocked();
            all[key] = protectedValue;
            SaveLocked(all);
        }
    }

    /// <summary>Removes one value from the sidecar file.</summary>
    /// <param name="key">The store key.</param>
    /// <returns><see langword="true"/> when a value was present and removed.</returns>
    private bool RemoveFromFile(string key)
    {
        lock (gate)
        {
            var all = ReadAllLocked();
            if (!all.Remove(key))
            {
                return false;
            }

            SaveLocked(all);
            return true;
        }
    }

    /// <summary>Reads the whole sidecar file.</summary>
    /// <returns>Every stored key and its encrypted payload.</returns>
    private Dictionary<string, string> ReadAll()
    {
        lock (gate)
        {
            return ReadAllLocked();
        }
    }

    /// <summary>Reads the whole sidecar file. The caller holds <see cref="gate"/>.</summary>
    /// <returns>Every stored key and its encrypted payload.</returns>
    private Dictionary<string, string> ReadAllLocked()
    {
        if (!File.Exists(secretFilePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(secretFilePath), SerializerOptions);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "The connector token file could not be read ({Reason}); stored tokens will need to "
                + "be re-entered",
                ex.GetType().Name);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes the whole sidecar file. The caller holds <see cref="gate"/>.</summary>
    /// <param name="all">Every stored key and its encrypted payload.</param>
    private void SaveLocked(Dictionary<string, string> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretFilePath)!);
            File.WriteAllText(secretFilePath, JsonSerializer.Serialize(all, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "The connector token file could not be written ({Reason}); the token was not saved",
                ex.GetType().Name);
        }
    }
}
