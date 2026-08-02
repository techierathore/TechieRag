namespace TechieDesk.Services.Updates;

/// <summary>
/// Supplies the version of the running application (REQ-FN-038b).
/// </summary>
/// <remarks>
/// The same platform seam as <c>ISecretStore</c>, and for the same reason: the authoritative answer
/// on a packaged build is <c>Microsoft.Maui.ApplicationModel.AppInfo</c>, which reads the real
/// <c>CFBundleShortVersionString</c> / package version that the packaging workflow stamped — and that
/// type lives in the MAUI head, which this project must never reference or the test project could not
/// build. Core states the contract; the head supplies the platform.
/// </remarks>
public interface IAppVersionProvider
{
    /// <summary>Gets the version of the running application.</summary>
    ReleaseVersion Current { get; }

    /// <summary>Gets the version exactly as the host reported it, for display and diagnostics.</summary>
    /// <remarks>
    /// Kept separate from <see cref="Current"/> because a host can report something unparseable. When
    /// that happens the parsed version degrades to 0.0.0 but this still shows the operator what the
    /// build actually claims, rather than a zero that looks like a bug in the app.
    /// </remarks>
    string RawVersion { get; }
}
