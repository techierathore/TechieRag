namespace TechieDesk.Services.Hosting;

/// <summary>
/// Host-agnostic view of the application environment.
/// </summary>
/// <remarks>
/// REQ-FN-035: the MAUI Blazor Hybrid head has no <c>IWebHostEnvironment</c> — that type ships with
/// ASP.NET Core hosting, which a native desktop app does not use. Services that only needed a content
/// root were otherwise forced to take a dependency on the whole web hosting stack, which is what made
/// them un-portable to the desktop head. Only the members the app actually consumes are exposed here;
/// today that is <see cref="ContentRootPath"/> alone.
/// </remarks>
public interface IAppEnvironment
{
    /// <summary>
    /// Absolute path to the directory the application treats as its content root. Used to resolve the
    /// legacy app-relative artefact locations that <c>DataDirectory</c> relocates from (REQ-FN-034).
    /// </summary>
    string ContentRootPath { get; }
}
