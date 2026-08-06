using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TechieDesk.Services.Auth;
using TechieDeskDb;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// The default <see cref="IMcpSecretStore"/>: the OS credential store first, an encrypted sidecar
/// second, and never the application database (REQ-FN-039, REQ-NFR-004b, REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Strongest scheme first, exactly as <c>ConnectorSecretStore</c> does it.</b> When
/// <see cref="ISecretStore.IsDurable"/> is true the whole name/value map for one server is written
/// to Keychain / the Credential Manager under <c>techiedesk.mcp.&lt;workspace&gt;.&lt;server&gt;</c>
/// as a single JSON document, and nothing sensitive touches the disk.</para>
/// <para><b>The known Mac Catalyst constraint, handled rather than fought (REQ-FN-043).</b> Keychain
/// access depends on the app's entitlements and code signature, so an unsigned developer build is
/// refused with <c>errSecMissingEntitlement</c> and <c>OsCredentialStore</c> degrades to an
/// in-memory store. In-memory alone would mean re-typing a bearer token on every launch, so this
/// class falls back to the REQ-NFR-004b scheme instead: ASP.NET Core Data Protection, written as
/// <c>enc:v1:&lt;payload&gt;</c> into a sidecar under the data directory, machine- and user-bound by
/// the persisted key ring. That is weaker than the keychain — a machine-bound file is not a keychain
/// — and it is still not cleartext, and it is still not the database. Whichever tier is in force is
/// reported through <see cref="Protection"/> and shown on the MCP tab, so an operator is never
/// guessing which one they got.</para>
/// <para><b>One document per server, not one entry per header.</b> A per-header entry would leak the
/// header names into the platform store's key space, and would make a partially-written credential
/// set possible — half the headers rotated, half not. The map is atomic.</para>
/// <para><b>Nothing here is logged but counts and outcomes.</b> Not a value, not the ciphertext, not
/// the file's contents.</para>
/// </remarks>
public sealed class McpSecretStore : IMcpSecretStore
{
    /// <summary>The prefix MCP credential maps are stored under in the OS credential store.</summary>
    public const string SecretKeyPrefix = "techiedesk.mcp.";

    /// <summary>The prefix on every Data-Protection-encrypted value in the sidecar file.</summary>
    public const string EncryptedPrefix = "enc:v1:";

    /// <summary>The sidecar file holding encrypted maps when no durable OS store is available.</summary>
    public const string SecretFileName = "mcp-secrets.json";

    private const string ProtectorPurpose = "TechieDesk.Mcp.ServerCredentials.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, string> NoSecrets =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly ISecretStore secrets;
    private readonly IDataProtector? protector;
    private readonly ILogger<McpSecretStore> logger;
    private readonly string secretFilePath;
    private readonly object gate = new();

    /// <summary>Initializes a new instance of the <see cref="McpSecretStore"/> class.</summary>
    /// <param name="secrets">The app's OS credential store abstraction (REQ-FN-039).</param>
    /// <param name="configuration">Application configuration, used to locate the data directory.</param>
    /// <param name="logger">Diagnostics. Never receives a credential or a ciphertext.</param>
    /// <param name="dataProtectionProvider">
    /// The host's Data Protection provider, used only for the fallback. Optional: a host that does
    /// not register one simply has no fallback and reports itself not durable rather than inventing
    /// a weaker scheme.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public McpSecretStore(
        ISecretStore secrets,
        IConfiguration configuration,
        ILogger<McpSecretStore> logger,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        protector = dataProtectionProvider?.CreateProtector(ProtectorPurpose);
        secretFilePath = Path.Combine(
            DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]), SecretFileName);
    }

    /// <inheritdoc />
    public bool IsDurable => secrets.IsDurable || protector is not null;

    /// <inheritdoc />
    public McpCredentialProtection Protection => secrets.IsDurable
        ? McpCredentialProtection.Keychain
        : protector is not null
            ? McpCredentialProtection.EncryptedSidecar
            : McpCredentialProtection.MemoryOnly;

    /// <summary>
    /// Builds the platform store key for one workspace's server.
    /// </summary>
    /// <param name="workspaceId">The workspace the server is registered in.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <returns>The credential reference recorded on the row — a name, never a value.</returns>
    /// <remarks>
    /// Public because <c>SqliteMcpServerRegistry</c> writes this string into the row's
    /// <c>CredentialRef</c> column, and a reader of that column must be able to see that it is a
    /// lookup key and nothing more.
    /// </remarks>
    public static string CredentialRef(string workspaceId, string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        return $"{SecretKeyPrefix}{workspaceId}.{serverName}";
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Read(string workspaceId, string serverName)
    {
        var key = CredentialRef(workspaceId, serverName);

        // The OS store is consulted first even when it is not currently durable, so a value written
        // by an earlier, entitled run of the same app is still found.
        var stored = secrets.Read(key);
        return Deserialize(string.IsNullOrEmpty(stored) ? ReadFromFile(key) : stored);
    }

    /// <inheritdoc />
    public void Write(string workspaceId, string serverName, IReadOnlyDictionary<string, string>? secretValues)
    {
        var key = CredentialRef(workspaceId, serverName);

        if (secretValues is null || secretValues.Count == 0)
        {
            Delete(workspaceId, serverName);
            return;
        }

        var payload = JsonSerializer.Serialize(
            secretValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        if (secrets.IsDurable)
        {
            secrets.Write(key, payload);

            // A credential promoted into the keychain must not be left behind in the weaker store
            // as well; two copies means the weaker one outlives a revocation.
            RemoveFromFile(key);
            logger.LogInformation(
                "Stored {Count} credential value(s) for MCP server {ServerName} in the OS credential "
                + "store (REQ-FN-039); the application database holds only their names",
                secretValues.Count, serverName);
            return;
        }

        if (protector is not null)
        {
            WriteToFile(key, EncryptedPrefix + protector.Protect(payload));
            logger.LogWarning(
                "The OS credential store is not available to this build, so {Count} credential "
                + "value(s) for MCP server {ServerName} were encrypted at rest with a machine-bound "
                + "key instead (REQ-NFR-004b). They are not in the application database",
                secretValues.Count, serverName);
            return;
        }

        secrets.Write(key, payload);
        logger.LogWarning(
            "No durable secret store is available in this host, so the credential values for MCP "
            + "server {ServerName} are held in memory for this run only and must be re-entered after "
            + "a restart. Nothing was written to disk",
            serverName);
    }

    /// <inheritdoc />
    public bool Delete(string workspaceId, string serverName)
    {
        var key = CredentialRef(workspaceId, serverName);

        // Both locations, always. A delete that cleared only the store currently in use would leave
        // a revoked token recoverable from the other one.
        var removedFromStore = secrets.Delete(key);
        var removedFromFile = RemoveFromFile(key);
        return removedFromStore || removedFromFile;
    }

    /// <summary>Turns a stored JSON document back into a name/value map.</summary>
    /// <param name="payload">The stored document, or null.</param>
    /// <returns>The map, or an empty map when absent or unreadable.</returns>
    private IReadOnlyDictionary<string, string> Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return NoSecrets;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
            return parsed is null
                ? NoSecrets
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // Deliberately logs nothing of the payload.
            logger.LogError(
                "A stored MCP credential document could not be parsed and was ignored; re-enter the "
                + "server's credentials");
            return NoSecrets;
        }
    }

    /// <summary>Reads and decrypts one document from the sidecar file.</summary>
    /// <param name="key">The store key.</param>
    /// <returns>The JSON document, or <see langword="null"/> when absent or unreadable.</returns>
    private string? ReadFromFile(string key)
    {
        if (protector is null) return null;

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
            // Neither the ciphertext nor the exception payload is logged. A rotated or missing key
            // ring means the credential cannot be recovered, and the honest outcome is "re-enter
            // it" rather than a run that quietly calls the server unauthenticated.
            logger.LogError(
                "Stored MCP credentials could not be decrypted ({Reason}); they were ignored. "
                + "Re-enter them on the server",
                ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Writes one already-encrypted document into the sidecar file.</summary>
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

    /// <summary>Removes one document from the sidecar file.</summary>
    /// <param name="key">The store key.</param>
    /// <returns><see langword="true"/> when a value was present and removed.</returns>
    private bool RemoveFromFile(string key)
    {
        lock (gate)
        {
            var all = ReadAllLocked();
            if (!all.Remove(key)) return false;

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
                "The MCP credential file could not be read ({Reason}); stored credentials will need "
                + "to be re-entered",
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
                "The MCP credential file could not be written ({Reason}); the credentials were not saved",
                ex.GetType().Name);
        }
    }
}
