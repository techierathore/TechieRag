namespace TechieDesk.Resources;

/// <summary>
/// Resource anchor for the localized UI strings (REQ-UI-039 / BRD-91). Injected as
/// <c>IStringLocalizer&lt;AppStrings&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This type exists only to name a resource set; it is never instantiated. Its namespace is what
/// binds it to the .resx files, so it must stay in <c>Resources/</c>:
/// <c>ResourceManagerStringLocalizerFactory</c> uses the type's full name
/// (<c>TechieDesk.Resources.AppStrings</c>) as the resource base name, and the SDK names the
/// embedded resource from this project's RootNamespace (<c>TechieDesk</c>) plus the folder — the
/// same string. Moving either the file or the namespace silently breaks every lookup into
/// key-name-as-value, which is exactly what <c>LocalizationTests</c> guards.
/// </para>
/// <para>
/// No <c>ResourcesPath</c> is configured on the localization services for the same reason: setting
/// it would prepend the folder a SECOND time and produce <c>TechieDesk.Resources.Resources.AppStrings</c>.
/// </para>
/// </remarks>
public sealed class AppStrings
{
    private AppStrings()
    {
    }
}
