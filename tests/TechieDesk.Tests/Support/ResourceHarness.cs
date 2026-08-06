using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Localization;

namespace TechieDesk.Tests.Support;

/// <summary>
/// Resolves resource keys through the REAL <see cref="IStringLocalizer{T}"/> the app uses, in a
/// nominated culture (REQ-UI-051 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the real localizer.</b> REQ-UI-051 moved user-visible text out of the service layer and
/// into resource keys. A test that asserted on the key string alone would prove nothing: the whole
/// class of defect it replaced was a value that LOOKED right in code and rendered English on a
/// translated install. Going through the container proves the key exists, that it resolves in Hindi
/// as well as English, and — because <c>ResourceManagerStringLocalizer</c> returns the KEY NAME as
/// the value when the resource set is missing — that the lookup actually landed.
/// </para>
/// <para>
/// <b>Culture is ambient and this restores it.</b> <c>CurrentUICulture</c> is what the localizer
/// reads, so it has to be set for the duration; leaving a test run in Hindi would make every later
/// test in the same collection assert against the wrong language, which is the sort of failure that
/// only reproduces in one ordering. Disposing puts it back.
/// </para>
/// </remarks>
public sealed class ResourceHarness : IDisposable
{
    private readonly ServiceProvider services;
    private readonly CultureInfo originalCulture = CultureInfo.CurrentUICulture;

    /// <summary>Builds a localization container equivalent to the app's.</summary>
    /// <param name="culture">The UI culture to resolve in, e.g. <c>en</c> or <c>hi</c>.</param>
    public ResourceHarness(string culture)
    {
        services = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider();

        Localizer = services.GetRequiredService<IStringLocalizer<AppStrings>>();
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    }

    /// <summary>Gets the localizer, resolving in the culture this harness was built for.</summary>
    public IStringLocalizer<AppStrings> Localizer { get; }

    /// <summary>
    /// Gets the delegate the service layer takes, bound to this harness's localizer.
    /// </summary>
    public LocalizeText Localize => (key, arguments) => Localizer[key, arguments!].Value;

    /// <summary>
    /// Gets the keys THIS culture's own resource file carries, parent cultures excluded.
    /// </summary>
    /// <remarks>
    /// The distinction that matters. A key present in <c>AppStrings.resx</c> but missing from
    /// <c>AppStrings.hi.resx</c> resolves to the ENGLISH text with <c>ResourceNotFound</c> FALSE —
    /// so <see cref="Require"/> is happy and a Hindi screen renders an English row. Comparing
    /// against the culture's own key set is the only check that sees it. English itself has no
    /// parent to fall back to, so it includes them.
    /// </remarks>
    public IReadOnlySet<string> OwnKeys =>
        Localizer
            .GetAllStrings(includeParentCultures: CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en")
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Resolves a key, failing loudly when the resource set does not carry it.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">Format arguments, when the key carries placeholders.</param>
    /// <returns>The translated value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the key is missing.</exception>
    public string Require(string key, params object?[] arguments)
    {
        // The no-argument indexer returns the value RAW. The args overload runs it through
        // string.Format, which throws on a "{0}" with nothing to substitute — so a key is only
        // formatted when the caller actually supplied something to put in it.
        var value = arguments.Length == 0 ? Localizer[key] : Localizer[key, arguments!];
        return value.ResourceNotFound
            ? throw new InvalidOperationException(
                $"'{key}' is not in the {CultureInfo.CurrentUICulture.Name} resources, so whatever " +
                "asked for it renders the key name on screen.")
            : value.Value;
    }

    /// <summary>Restores the culture the test started in.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = originalCulture;
        services.Dispose();
    }
}
