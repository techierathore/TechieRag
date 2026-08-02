using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Branding;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Appearance;

/// <summary>
/// Registration for the appearance, branding and localization services
/// (REQ-UI-037 / REQ-UI-038 / REQ-UI-039).
/// </summary>
public static class AppearanceServiceCollectionExtensions
{
    /// <summary>
    /// Adds the appearance, white-label branding and localization services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Scoped rather than singleton, matching every other store in this app: a BlazorWebView creates
    /// one scope for the lifetime of the window, so scoped already means one instance per run, and
    /// keeping the lifetime uniform means a store can take a scoped dependency later without a
    /// captive-dependency bug.
    /// <para>
    /// <c>AddLocalization</c> is called WITHOUT a <c>ResourcesPath</c> — see
    /// <c>TechieDesk.Resources.AppStrings</c> for why adding one would break every lookup.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskAppearance(this IServiceCollection services)
    {
        services.TryAddScoped<IAppearanceStore, AppearanceStore>();
        services.TryAddScoped<IBrandingStore, BrandingStore>();
        services.TryAddScoped<ILanguageStore, LanguageStore>();

        services.AddLocalization();

        return services;
    }
}
