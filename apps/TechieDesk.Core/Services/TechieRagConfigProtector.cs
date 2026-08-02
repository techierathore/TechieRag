using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using TechieDesk.Services.Auth;
using TechieRag;

namespace TechieDesk.Services;

/// <summary>
/// Keeps the credential-bearing fields of a <see cref="TechieRagConfig"/> out of the on-disk
/// <c>techierag-config.json</c>, so it never holds provider API keys or data-store connection
/// strings in cleartext (REQ-NFR-004b, REQ-NFR-012, REQ-FN-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provider API keys are <i>outbound</i> credentials — the app must be able to
/// replay them to the LLM / embedding / vector-store providers — so hashing is not applicable. The
/// vector-store and persistence connection strings are credential-bearing for exactly the same
/// reason: a PgVector or Postgres DSN embeds <c>Password=…</c>, and the app must replay it verbatim
/// to open the connection (REQ-NFR-012).</para>
/// <para><b>Two schemes, strongest first.</b> When a durable <see cref="ISecretStore"/> is available
/// (Keychain / Windows Credential Manager, REQ-FN-039) the key itself goes into the OS store and the
/// file receives only an opaque reference, <c>enc:v2:&lt;field&gt;</c> — there is no ciphertext on
/// disk at all, and the secret is bound to this machine and user account. With no durable store the
/// original REQ-NFR-004b scheme still applies: ASP.NET Core Data Protection, written as
/// <c>enc:v1:&lt;payload&gt;</c>. The fallback is deliberate rather than automatic-best-effort: an
/// in-memory store would make a reference unresolvable after a restart, which would lose an
/// operator's saved key.</para>
/// <para><b>Code Flow:</b> Constructed by <see cref="TechieRagConfigService"/> and
/// <see cref="TechieRagManager"/>. <see cref="CreateProtectedClone"/> is called immediately before
/// the configuration is serialized to disk; <see cref="RevealSecrets"/> immediately after it is
/// deserialized.</para>
/// <para><b>Backward compatibility:</b> every earlier on-disk shape keeps working. A legacy
/// cleartext value is used as-is; an <c>enc:v1:</c> value is decrypted with Data Protection. Both
/// cause the caller to be told to rewrite the file, which is how an existing install migrates up to
/// the OS store on its next save. An existing working configuration is never discarded.</para>
/// <para><b>Logging:</b> only the fact and the count of an operation is logged. Neither the cleartext
/// key, the ciphertext, nor an OS-store payload is ever written to a log sink.</para>
/// <para><b>Standing limit (recorded, not fixed here):</b> on macOS and Linux the Data Protection key
/// ring is persisted unencrypted beside the data it protects, because there is no DPAPI/KMS to wrap
/// it with. The <c>enc:v1:</c> scheme is therefore <i>key separation</i> — it defeats a casual read of
/// the configuration file, a backup copy or a support bundle — not a KMS boundary against an attacker
/// who already has the data directory. The <c>enc:v2:</c> scheme has no such limit.</para>
/// </remarks>
public sealed class TechieRagConfigProtector
{
    /// <summary>
    /// Marker prefixed to every Data-Protection-encrypted value written to
    /// <c>techierag-config.json</c> (REQ-NFR-004b).
    /// </summary>
    public const string EncryptedPrefix = "enc:v1:";

    /// <summary>
    /// Marker prefixed to every OS-credential-store reference written to
    /// <c>techierag-config.json</c> (REQ-FN-039). The remainder names the field, not the secret.
    /// </summary>
    public const string SecretReferencePrefix = "enc:v2:";

    /// <summary>Prefix of the keys provider credentials are stored under in the OS store.</summary>
    public const string SecretKeyPrefix = "techiedesk.provider.";

    private const string ProtectorPurpose = "TechieDesk.TechieRagConfig.ProviderApiKeys.v1";

    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDataProtector protector;
    private readonly ISecretStore? secrets;
    private readonly ILogger<TechieRagConfigProtector> logger;

    /// <summary>
    /// Creates a new <see cref="TechieRagConfigProtector"/>.
    /// </summary>
    /// <param name="dataProtectionProvider">The host Data Protection provider. Its key ring must be
    /// persisted to disk, otherwise legacy <c>enc:v1:</c> values become unreadable after a restart.</param>
    /// <param name="logger">Logger used for security-event logging. Never receives key material.</param>
    /// <param name="secretStore">The OS credential store (REQ-FN-039). Optional: when absent or not
    /// durable, the REQ-NFR-004b Data Protection scheme is used instead, so a host without a platform
    /// store still encrypts at rest.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
    public TechieRagConfigProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TechieRagConfigProtector> logger,
        ISecretStore? secretStore = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        this.logger = logger;
        this.secrets = secretStore;
    }

    /// <summary>
    /// Determines whether a stored value already carries one of the protected-at-rest markers.
    /// </summary>
    /// <param name="value">The raw value read from the configuration file.</param>
    /// <returns><c>true</c> when the value is ciphertext or an OS-store reference.</returns>
    public static bool IsProtected(string? value) =>
        value is not null
        && (value.StartsWith(EncryptedPrefix, StringComparison.Ordinal)
            || value.StartsWith(SecretReferencePrefix, StringComparison.Ordinal));

    /// <summary>
    /// Determines whether a stored value is a reference into the OS credential store.
    /// </summary>
    /// <param name="value">The raw value read from the configuration file.</param>
    /// <returns><c>true</c> when the value names an OS-store entry rather than carrying ciphertext.</returns>
    public static bool IsSecretReference(string? value) =>
        value is not null && value.StartsWith(SecretReferencePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Produces a deep copy of the configuration whose credential fields are protected, leaving the
    /// supplied instance untouched so the caller can keep using it in memory.
    /// </summary>
    /// <param name="config">The configuration holding cleartext credentials.</param>
    /// <returns>A clone that is safe to serialize to disk.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public TechieRagConfig CreateProtectedClone(TechieRagConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var clone = JsonSerializer.Deserialize<TechieRagConfig>(
            JsonSerializer.Serialize(config, CloneOptions), CloneOptions) ?? new TechieRagConfig();

        var useOsStore = UsesOsStore;
        var storedCount = 0;
        var encryptedCount = 0;
        foreach (var field in EnumerateSecretFields(clone))
        {
            var value = field.Read();
            if (string.IsNullOrEmpty(value))
            {
                // A key the operator has just cleared must not be left behind in the OS store.
                secrets?.Delete(SecretKeyPrefix + field.Name);
                continue;
            }

            if (IsProtected(value))
            {
                continue;
            }

            if (useOsStore)
            {
                secrets!.Write(SecretKeyPrefix + field.Name, value);
                field.Write(SecretReferencePrefix + field.Name);
                storedCount++;
                continue;
            }

            field.Write(EncryptedPrefix + protector.Protect(value));
            encryptedCount++;
        }

        logger.LogInformation(
            "Protected {Total} credential field(s) before writing the TechieRag configuration "
            + "({Stored} in the OS credential store, {Encrypted} encrypted at rest) "
            + "(REQ-NFR-004b/REQ-NFR-012/REQ-FN-039)",
            storedCount + encryptedCount, storedCount, encryptedCount);
        return clone;
    }

    /// <summary>
    /// Resolves the credential fields of a configuration that was just read from disk, in place.
    /// </summary>
    /// <returns><c>true</c> when the file holds credentials in a weaker form than this host can
    /// provide — legacy cleartext, or <c>enc:v1:</c> ciphertext on a host with a durable OS store —
    /// AND everything resolved cleanly, meaning the caller should rewrite the file to upgrade it.</returns>
    /// <param name="config">The configuration deserialized from <c>techierag-config.json</c>.</param>
    /// <remarks>
    /// A value that cannot be resolved (missing or rotated key ring, a hand-edited file, an OS-store
    /// entry that is gone) is dropped from the in-memory configuration and this method returns
    /// <c>false</c>, so the caller leaves the saved file intact rather than overwriting the
    /// operator's data.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public bool RevealSecrets(TechieRagConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var useOsStore = UsesOsStore;
        var legacyCount = 0;
        var upgradableCount = 0;
        var failureCount = 0;
        foreach (var field in EnumerateSecretFields(config))
        {
            var value = field.Read();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (IsSecretReference(value))
            {
                failureCount += TryResolveReference(field) ? 0 : 1;
                continue;
            }

            if (!IsProtected(value))
            {
                legacyCount++;
                continue;
            }

            if (!TryReveal(field, value))
            {
                failureCount++;
                continue;
            }

            if (useOsStore)
            {
                upgradableCount++;
            }
        }

        LogRevealOutcome(legacyCount, upgradableCount, failureCount);
        return (legacyCount > 0 || upgradableCount > 0) && failureCount == 0;
    }

    /// <summary>Gets a value indicating whether an OS credential store is present AND durable.</summary>
    private bool UsesOsStore => secrets is { IsDurable: true };

    /// <summary>
    /// Replaces an <c>enc:v2:</c> reference with the value the OS credential store holds for that
    /// field, failing safe by clearing the field when the entry has gone.
    /// </summary>
    private bool TryResolveReference(SecretField field)
    {
        // The reference body names the field it belongs to; the lookup key is derived from the field
        // being resolved, so a hand-edited file cannot point one provider's slot at another's secret.
        var stored = secrets?.Read(SecretKeyPrefix + field.Name);
        if (!string.IsNullOrEmpty(stored))
        {
            field.Write(stored);
            return true;
        }

        logger.LogError(
            "The OS credential store holds no entry for the {Field} credential; the value was ignored. "
            + "The saved configuration file was left untouched — re-enter the value in Settings",
            field.Name);
        field.Write(null);
        return false;
    }

    /// <summary>
    /// Decrypts a single marked value, failing safe by clearing the field when the payload is
    /// unreadable.
    /// </summary>
    private bool TryReveal(SecretField field, string value)
    {
        try
        {
            field.Write(protector.Unprotect(value[EncryptedPrefix.Length..]));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Deliberately logs neither the ciphertext nor the exception payload.
            logger.LogError(
                "Unable to decrypt the stored {Field} credential ({Reason}); the value was ignored. " +
                "The saved configuration file was left untouched — re-enter the value in Settings",
                field.Name,
                ex.GetType().Name);
            field.Write(null);
            return false;
        }
    }

    /// <summary>
    /// Emits the security events for a resolve pass without ever revealing key material.
    /// </summary>
    private void LogRevealOutcome(int legacyCount, int upgradableCount, int failureCount)
    {
        if (legacyCount > 0)
        {
            logger.LogWarning(
                "Found {Count} credential field(s) stored in legacy cleartext; they will be re-saved protected at rest (REQ-NFR-004b/REQ-NFR-012)",
                legacyCount);
        }

        if (upgradableCount > 0)
        {
            logger.LogInformation(
                "Found {Count} credential field(s) encrypted in the configuration file; they will be moved into the OS credential store (REQ-FN-039)",
                upgradableCount);
        }

        if (failureCount > 0)
        {
            logger.LogError(
                "{Count} credential field(s) could not be resolved and were dropped from the loaded configuration",
                failureCount);
        }

        if (legacyCount == 0 && upgradableCount == 0 && failureCount == 0)
        {
            logger.LogDebug("Credential fields resolved from the saved TechieRag configuration");
        }
    }

    /// <summary>
    /// Enumerates every credential-bearing field of the configuration as a read/write accessor pair.
    /// </summary>
    /// <remarks>
    /// <para>Covers the outbound provider keys — embedding, vector store, primary LLM, fallback LLM
    /// and the rerank stage — plus the two data-store connection strings (REQ-NFR-012). Add new
    /// secret-shaped fields here: protection, resolution, the upgrade-on-save decision and the
    /// OS-store key are all driven from this single list, so a field added here needs no other
    /// change.</para>
    /// <para><b>Why the connection strings belong here.</b> A PgVector or Postgres DSN carries
    /// <c>Password=…</c> inline, so leaving it in cleartext left a database credential on disk that
    /// the API-key work (REQ-NFR-004b) had already removed for every other secret. The local
    /// SqliteVec and Qdrant forms are file paths and URLs rather than credentials, but they go
    /// through the same path deliberately: a rule that inspected the value to decide would silently
    /// stop protecting the moment a new store type or DSN shape appeared, which is the failure mode
    /// worth designing out.</para>
    /// </remarks>
    private static IEnumerable<SecretField> EnumerateSecretFields(TechieRagConfig config)
    {
        if (config.Embedding is { } embedding)
        {
            yield return new SecretField("Embedding:ApiKey", () => embedding.ApiKey, value => embedding.ApiKey = value);
        }

        if (config.VectorStore is { } vectorStore)
        {
            yield return new SecretField("VectorStore:ApiKey", () => vectorStore.ApiKey, value => vectorStore.ApiKey = value);

            // ConnectionString is declared non-nullable with a local default, so the fail-safe clear
            // writes string.Empty rather than null — the same "the value was ignored, re-enter it"
            // outcome an unreadable API key gets, expressed in this property's own type.
            yield return new SecretField(
                "VectorStore:ConnectionString",
                () => vectorStore.ConnectionString,
                value => vectorStore.ConnectionString = value ?? string.Empty);
        }

        if (config.Llm is { } llm)
        {
            yield return new SecretField("Llm:ApiKey", () => llm.ApiKey, value => llm.ApiKey = value);
        }

        if (config.LlmFallback is { } fallback)
        {
            yield return new SecretField("LlmFallback:ApiKey", () => fallback.ApiKey, value => fallback.ApiKey = value);
        }

        if (config.Rerank is { } rerank)
        {
            yield return new SecretField("Rerank:ApiKey", () => rerank.ApiKey, value => rerank.ApiKey = value);
        }

        if (config.Persistence is { } persistence)
        {
            yield return new SecretField(
                "Persistence:ConnectionString",
                () => persistence.ConnectionString,
                value => persistence.ConnectionString = value);
        }
    }

    /// <summary>
    /// A single credential-bearing configuration field, addressed by accessor so the protect and
    /// resolve passes share one definition of "what is a secret".
    /// </summary>
    private sealed record SecretField(string Name, Func<string?> Read, Action<string?> Write);
}
