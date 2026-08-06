using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// A format argument that is itself a resource KEY rather than a value (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="Key">A key present in <c>AppStrings.resx</c>.</param>
/// <remarks>
/// <para>
/// Licence prose is the one place in the service layer where a sentence has to embed a word this
/// app owns — "<i>Team</i> — your seat is assigned" — rather than a value that came from somewhere
/// else. Passing the raw <see cref="InstanceMode"/> would put the English enum name into a Hindi
/// sentence; passing a pre-resolved string would put a localizer back into the resolver, which is
/// exactly what REQ-UI-051 took out. So the argument travels as a key and
/// <see cref="LicenseMessage.Resolve"/> translates it on the way through.
/// </para>
/// <para>
/// It is deliberately the ONLY nesting mechanism, and it is one level deep. A tier name, an hour
/// count or an expiry date is a plain argument: those are server-supplied or numeric and there is
/// nothing to translate.
/// </para>
/// </remarks>
public sealed record LocalizedArgument(string Key);

/// <summary>
/// Turns a licence resource key plus its arguments into the reader's language (REQ-UI-055 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// <b>The REQ-UI-051 shape, applied to licensing.</b> <see cref="LicenseStatus"/>,
/// <see cref="InstanceModeStatus"/> and <see cref="FeatureDecision"/> carry a resource key and its
/// arguments, never a sentence. Nothing under <c>Services/Licensing/</c> can hand English to a
/// screen, because none of it holds a localizer and none of it builds a sentence.
/// </para>
/// <para>
/// <b>Why this is separate from the status types.</b> Three unrelated records need the same
/// key-plus-arguments resolution and all three are rendered by different components — the shell
/// banner, the licence card, the feature gate. One function, called from each.
/// </para>
/// </remarks>
public static class LicenseMessage
{
    /// <summary>
    /// Resolves a key and its arguments, translating any argument that is itself a key.
    /// </summary>
    /// <param name="localize">The caller's localizer, normally <c>(k, a) =&gt; Localizer[k, a!]</c>.</param>
    /// <param name="key">The resource key, or null/blank when there is nothing to say.</param>
    /// <param name="arguments">The format arguments, which may include <see cref="LocalizedArgument"/>.</param>
    /// <returns>The translated sentence, or the empty string when <paramref name="key"/> is blank.</returns>
    public static string Resolve(LocalizeText localize, string? key, IReadOnlyList<object?>? arguments)
    {
        ArgumentNullException.ThrowIfNull(localize);

        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (arguments is null || arguments.Count == 0)
        {
            return localize(key);
        }

        var resolved = new object?[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            resolved[i] = arguments[i] is LocalizedArgument nested ? localize(nested.Key) : arguments[i];
        }

        return localize(key, resolved);
    }
}
