namespace TechieDesk.Services.Localization;

/// <summary>
/// One language offered by the language picker (REQ-UI-039 / BRD-91).
/// </summary>
/// <param name="Culture">The BCP-47 culture name, e.g. <c>hi</c>.</param>
/// <param name="EnglishName">The language name in English, for logs and diagnostics.</param>
/// <param name="NativeName">
/// The language name in that language, which is what the picker shows. Someone who has landed in a
/// UI they cannot read is looking for "हिन्दी", not for "Hindi".
/// </param>
public sealed record AppLanguage(string Culture, string EnglishName, string NativeName);
