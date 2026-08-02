namespace TechieDesk.Services.AppManager;

/// <summary>
/// Why a promo code failed the local, pre-network validation performed before
/// <c>POST /PaymentSvc/promo-codes/validate</c> is called (REQ-FN-026 / BRD-79).
/// </summary>
public enum PromoCodeFormat
{
    /// <summary>The code is well-formed and worth sending to AppManager.</summary>
    Valid = 0,

    /// <summary>Nothing was entered.</summary>
    Empty,

    /// <summary>The code is shorter than <see cref="PromoCodeValidator.MinLength"/> characters.</summary>
    TooShort,

    /// <summary>The code is longer than <see cref="PromoCodeValidator.MaxLength"/> characters.</summary>
    TooLong,

    /// <summary>The code contains characters outside <c>A-Z</c>, <c>0-9</c> and <c>-</c>.</summary>
    IllegalCharacters
}

/// <summary>
/// Why a promo code was refused before the network call, as a resource KEY plus the values that
/// fill it (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="MessageKey">A key in <c>AppStrings.resx</c>.</param>
/// <param name="Arguments">Format arguments — the length bounds, which are numbers, not words.</param>
public sealed record PromoCodeFailure(string MessageKey, object[] Arguments);

/// <summary>
/// Local shape check for promotional codes. Cheap client-side rejection of input that could
/// never be a promo code, so an obvious typo produces an immediate, specific message instead of
/// a network round-trip and a generic <c>PROMO_CODE_NOT_FOUND</c> (REQ-FN-026 / BRD-79).
/// </summary>
/// <remarks>
/// <para>
/// This is a shape check only — it never decides that a code is redeemable. Existence, activity,
/// expiry, exhaustion and application scoping are AppManager's decisions and are always resolved
/// by the round-trip that follows.
/// </para>
/// <para>
/// <b>REQ-UI-055 / BRD-91.</b> The messages are resource KEYS resolved by the pricing page. The CODE
/// itself is untouched wire vocabulary: <see cref="Normalize"/> still upper-cases with
/// <c>ToUpperInvariant</c> and still accepts only <c>A-Z</c>, <c>0-9</c> and <c>-</c>, so the string
/// posted to <c>/PaymentSvc/promo-codes/validate</c> is byte-identical whatever the app is running
/// in. A culture-sensitive upper-case here is the classic Turkish-i defect: <c>i</c> would become
/// <c>İ</c> on a <c>tr</c> machine and the server would reject a code the user typed correctly.
/// </para>
/// </remarks>
public static class PromoCodeValidator
{
    /// <summary>The shortest accepted promo code.</summary>
    public const int MinLength = 3;

    /// <summary>The longest accepted promo code.</summary>
    public const int MaxLength = 40;

    /// <summary>Resource key for an empty entry.</summary>
    public const string EmptyKey = "PromoCodeErrorEmpty";

    /// <summary>Resource key for a code under the floor. Takes <see cref="MinLength"/>.</summary>
    public const string TooShortKey = "PromoCodeErrorTooShort";

    /// <summary>Resource key for a code over the cap. Takes <see cref="MaxLength"/>.</summary>
    public const string TooLongKey = "PromoCodeErrorTooLong";

    /// <summary>Resource key for a code carrying characters outside the alphabet.</summary>
    public const string IllegalCharactersKey = "PromoCodeErrorIllegalCharacters";

    /// <summary>Resource key for an outcome this switch does not otherwise name.</summary>
    public const string UnrecognisedKey = "PromoCodeErrorUnrecognised";

    /// <summary>
    /// Normalizes a user-entered promo code — trims surrounding whitespace and upper-cases it —
    /// and reports whether the result is worth sending to AppManager.
    /// </summary>
    /// <param name="input">The raw text the user typed, which may be null.</param>
    /// <param name="normalized">
    /// The trimmed, upper-cased code when the result is <see cref="PromoCodeFormat.Valid"/>;
    /// otherwise the trimmed input as-is (never null).
    /// </param>
    /// <returns>The validation outcome.</returns>
    public static PromoCodeFormat Normalize(string? input, out string normalized)
    {
        normalized = (input ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            return PromoCodeFormat.Empty;
        }

        if (normalized.Length < MinLength)
        {
            return PromoCodeFormat.TooShort;
        }

        if (normalized.Length > MaxLength)
        {
            return PromoCodeFormat.TooLong;
        }

        normalized = normalized.ToUpperInvariant();
        foreach (var character in normalized)
        {
            var allowed = character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character == '-';
            if (!allowed)
            {
                return PromoCodeFormat.IllegalCharacters;
            }
        }

        return PromoCodeFormat.Valid;
    }

    /// <summary>
    /// Renders a <see cref="PromoCodeFormat"/> as the message shown under the promo-code field.
    /// </summary>
    /// <param name="format">The outcome from <see cref="Normalize(string?, out string)"/>.</param>
    /// <returns>The failure to render, or null when the code is well-formed.</returns>
    public static PromoCodeFailure? DescribeFailure(PromoCodeFormat format) => format switch
    {
        PromoCodeFormat.Valid => null,
        PromoCodeFormat.Empty => new PromoCodeFailure(EmptyKey, []),
        PromoCodeFormat.TooShort => new PromoCodeFailure(TooShortKey, [MinLength]),
        PromoCodeFormat.TooLong => new PromoCodeFailure(TooLongKey, [MaxLength]),
        PromoCodeFormat.IllegalCharacters => new PromoCodeFailure(IllegalCharactersKey, []),
        _ => new PromoCodeFailure(UnrecognisedKey, [])
    };
}
