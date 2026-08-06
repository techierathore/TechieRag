using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace TechieDesk.Services.Auth;

/// <summary>
/// The platform half of REQ-FN-039: an <see cref="ISecretStore"/> over
/// <see cref="SecureStorage"/> — Keychain on macOS / Mac Catalyst, DPAPI-backed
/// <c>PasswordVault</c> / Credential Manager on Windows.
/// </summary>
/// <remarks>
/// <para><b>Why SecureStorage and not P/Invoke.</b> MAUI already wraps exactly the two OS stores
/// BRD-132 names, per platform, with the right item accessibility flags. Hand-rolling
/// <c>SecItemAdd</c> / <c>CredWrite</c> would add two native code paths to maintain and test for no
/// property this does not already give: values are bound to the machine and the signed-in user
/// account, and they are not readable from a plain file or from the WebView.</para>
/// <para><b>Why it lives in the head.</b> <c>SecureStorage</c> is MAUI, and TechieDesk.Core is plain
/// <c>net10.0</c> — a platform-targeted reference there would stop the net10.0 test project
/// referencing Core at all (the REQ-FN-035 constraint). Core owns the contract, the head owns the
/// platform.</para>
/// <para><b>Why it probes at construction.</b> Keychain access on Mac Catalyst depends on the app's
/// entitlements and code signature, so an unsigned developer build can be refused at runtime. Rather
/// than discovering that at sign-in and losing the session silently, the store proves it can
/// round-trip one throwaway value up front and reports the answer through
/// <see cref="IsDurable"/>. When it cannot, secrets are held in process memory — never written to a
/// file, because a file-backed fallback would be exactly the weakening REQ-FN-039 forbids.</para>
/// <para><b>Why the calls are blocking.</b> <see cref="ISecretStore"/> is synchronous by design (see
/// its remarks). The underlying Keychain and Credential Manager calls are synchronous system calls
/// that MAUI merely presents as tasks, and the work is dispatched to the thread pool before it is
/// waited on, so nothing here can deadlock the UI thread on its own synchronization context.</para>
/// </remarks>
public sealed class OsCredentialStore : ISecretStore
{
    private const string ProbeKey = "techiedesk.securestorage.probe";

    private readonly ISecretStore fallback = new EphemeralSecretStore();
    private readonly ILogger<OsCredentialStore> logger;
    private readonly bool available;

    /// <summary>
    /// Creates the store and proves the platform credential store is actually usable.
    /// </summary>
    /// <param name="logger">Logger for the availability decision and any store failure.</param>
    public OsCredentialStore(ILogger<OsCredentialStore> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.available = Probe();

        if (available)
        {
            logger.LogInformation(
                "Secrets are held in the OS credential store (REQ-FN-039): tokens and provider keys "
                + "are bound to this machine and user account, and are on no plain file");
        }
        else
        {
            logger.LogWarning(
                "The OS credential store is not available to this build, so secrets are held in "
                + "memory for this run only. Nothing sensitive is written to disk, but a sign-in "
                + "will not survive a restart until the app is signed with a keychain entitlement");
        }
    }

    /// <inheritdoc />
    public bool IsDurable => available;

    /// <inheritdoc />
    public string? Read(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!available)
        {
            return fallback.Read(key);
        }

        try
        {
            return Block(() => SecureStorage.Default.GetAsync(key));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read {Key} from the OS credential store", key);
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!available)
        {
            fallback.Write(key, value);
            return;
        }

        try
        {
            Block(async () =>
            {
                await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
                return true;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write {Key} to the OS credential store", key);
        }
    }

    /// <inheritdoc />
    public bool Delete(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!available)
        {
            return fallback.Delete(key);
        }

        try
        {
            return SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not remove {Key} from the OS credential store", key);
            return false;
        }
    }

    /// <summary>
    /// Round-trips a throwaway value to decide whether the platform store can be relied on.
    /// </summary>
    /// <returns>True when a write, read-back and delete all succeeded.</returns>
    private bool Probe()
    {
        var canary = Guid.NewGuid().ToString("N");
        try
        {
            Block(async () =>
            {
                await SecureStorage.Default.SetAsync(ProbeKey, canary).ConfigureAwait(false);
                return true;
            });
            var readBack = Block(() => SecureStorage.Default.GetAsync(ProbeKey));
            SecureStorage.Default.Remove(ProbeKey);
            return string.Equals(readBack, canary, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            // The message is a platform status (e.g. a Keychain OSStatus), never key material — and
            // it is the only thing that tells an operator WHY, so it is logged in full.
            logger.LogWarning(
                "The OS credential store rejected this process ({Reason}): {Detail}",
                ex.GetType().Name, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Runs an asynchronous platform call to completion on the thread pool and returns its result.
    /// </summary>
    private static TResult Block<TResult>(Func<Task<TResult>> call) =>
        Task.Run(call).GetAwaiter().GetResult();
}
