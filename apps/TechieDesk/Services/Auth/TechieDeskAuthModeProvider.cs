using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Derives the auth mode from configuration: AppManager mode when <c>AppManager:BaseUrl</c>
/// is set, offline single-user mode otherwise (BRD-54).
/// </summary>
public sealed class TechieDeskAuthModeProvider : ITechieDeskAuthModeProvider
{
    private readonly AppManagerOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskAuthModeProvider"/> class.
    /// </summary>
    /// <param name="options">The AppManager configuration.</param>
    public TechieDeskAuthModeProvider(IOptions<AppManagerOptions> options)
    {
        this.options = options.Value;
    }

    /// <inheritdoc />
    public TechieDeskAuthMode Mode =>
        options.IsConfigured ? TechieDeskAuthMode.AppManager : TechieDeskAuthMode.Offline;

    /// <inheritdoc />
    public bool IsAppManagerEnabled => Mode == TechieDeskAuthMode.AppManager;
}
