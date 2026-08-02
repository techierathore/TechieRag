using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Derives the auth mode from configuration: AppManager mode when <c>AppManager:BaseUrl</c>
/// is set, offline single-user mode otherwise (BRD-54).
/// </summary>
/// <remarks>
/// REQ-FN-036: the mode now selects only whether there is a licence server to sign in to. It does
/// NOT decide whether the app is usable — both modes serve every route (BRD-129).
/// <para>
/// The startup log line below was previously written by <c>TechieDeskAuthExtensions</c>, which
/// REQ-FN-035 excluded from compilation with the rest of the web pipeline; nothing replaced it, so
/// a desktop build reported nothing at all about which mode it had resolved. It is emitted here
/// instead — this singleton is constructed once per app run — so the resolved mode stays visible
/// in the boot log without reintroducing a host-specific registration step.
/// </para>
/// </remarks>
public sealed class TechieDeskAuthModeProvider : ITechieDeskAuthModeProvider
{
    private readonly AppManagerOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskAuthModeProvider"/> class.
    /// </summary>
    /// <param name="options">The AppManager configuration.</param>
    /// <param name="logger">Logger for the resolved-mode boot diagnostic.</param>
    public TechieDeskAuthModeProvider(
        IOptions<AppManagerOptions> options,
        ILogger<TechieDeskAuthModeProvider> logger)
    {
        this.options = options.Value;

        if (IsAppManagerEnabled)
        {
            logger.LogInformation(
                "TechieDesk auth mode: AppManager — sign-in available to activate a licence; local use is never gated");
        }
        else
        {
            logger.LogInformation(
                "TechieDesk auth mode: Offline single-user — no AppManager:BaseUrl configured, running as built-in Admin with no sign-in");
        }
    }

    /// <inheritdoc />
    public TechieDeskAuthMode Mode =>
        options.IsConfigured ? TechieDeskAuthMode.AppManager : TechieDeskAuthMode.Offline;

    /// <inheritdoc />
    public bool IsAppManagerEnabled => Mode == TechieDeskAuthMode.AppManager;
}
