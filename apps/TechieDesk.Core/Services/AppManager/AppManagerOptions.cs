namespace TechieDesk.Services.AppManager;

/// <summary>
/// Configuration for the AppManager integration, bound from the <c>AppManager</c> section.
/// </summary>
/// <remarks>
/// Credentials must come from environment configuration or user-secrets — never from committed
/// appsettings values (the committed section holds empty placeholders only). When
/// <see cref="BaseUrl"/> is empty the application runs in offline single-user mode (BRD-54)
/// and no AppManager call is ever made.
/// </remarks>
public sealed class AppManagerOptions
{
    /// <summary>Name of the configuration section this options class binds to.</summary>
    public const string SectionName = "AppManager";

    /// <summary>Base URL of the AppManager API. Empty means offline single-user mode.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key sent as the <c>X-Api-Key</c> header on every AppManager call.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API secret sent as the <c>X-Api-Secret</c> header on every AppManager call.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit ApplicationId, sent as the v1.4 <c>aApplicationId</c> query parameter
    /// on endpoints that accept it (the API key headers normally resolve it server-side).
    /// </summary>
    public int? ApplicationId { get; set; }

    /// <summary>Number of seconds before access-token expiry at which a silent refresh is triggered.</summary>
    public int TokenRefreshLeadSeconds { get; set; } = 120;

    /// <summary>
    /// When <see langword="true"/>, the AppManager <see cref="System.Net.Http.HttpClient"/> accepts an
    /// untrusted / self-signed TLS server certificate. DEVELOPMENT ONLY — the wiring in
    /// <c>AddTechieDeskAuth</c> honours this flag exclusively when the host environment is Development,
    /// so it can never relax certificate validation in Production. Set it in the gitignored
    /// <c>appsettings.Development.json</c> when the local AppManager host uses a self-signed certificate.
    /// </summary>
    public bool AllowUntrustedServerCertificate { get; set; }

    /// <summary>Gets a value indicating whether an AppManager base URL is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
