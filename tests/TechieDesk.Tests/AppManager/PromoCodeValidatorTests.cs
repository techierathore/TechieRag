using TechieDesk.Services.AppManager;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-FN-026 / BRD-79: the local shape check that runs before a promo code is sent to
/// <c>POST /PaymentSvc/promo-codes/validate</c>.
/// </summary>
public sealed class PromoCodeValidatorTests
{
    /// <summary>A well-formed code is accepted and returned trimmed and upper-cased.</summary>
    [Fact]
    public void NormalizesTrimmedUpperCase()
    {
        var result = PromoCodeValidator.Normalize("  save20  ", out var normalized);

        Assert.Equal(PromoCodeFormat.Valid, result);
        Assert.Equal("SAVE20", normalized);
    }

    /// <summary>Hyphens are legal, so a hyphenated campaign code survives normalization.</summary>
    [Fact]
    public void AcceptsHyphenatedCode()
    {
        var result = PromoCodeValidator.Normalize("launch-2026", out var normalized);

        Assert.Equal(PromoCodeFormat.Valid, result);
        Assert.Equal("LAUNCH-2026", normalized);
    }

    /// <summary>An empty or whitespace-only entry is reported as empty, not as too short.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyInput(string? input)
    {
        var result = PromoCodeValidator.Normalize(input, out var normalized);

        Assert.Equal(PromoCodeFormat.Empty, result);
        Assert.Equal(string.Empty, normalized);
    }

    /// <summary>A code below the minimum length is rejected without a network round-trip.</summary>
    [Fact]
    public void RejectsTooShortCode()
    {
        Assert.Equal(PromoCodeFormat.TooShort, PromoCodeValidator.Normalize("AB", out _));
    }

    /// <summary>A code above the maximum length is rejected without a network round-trip.</summary>
    [Fact]
    public void RejectsTooLongCode()
    {
        var overlong = new string('A', PromoCodeValidator.MaxLength + 1);

        Assert.Equal(PromoCodeFormat.TooLong, PromoCodeValidator.Normalize(overlong, out _));
    }

    /// <summary>
    /// Characters outside letters, digits and hyphens are rejected locally — this is what stops a
    /// pasted URL or an injected separator from being sent to the licence server as a code.
    /// </summary>
    [Theory]
    [InlineData("SAVE 20")]
    [InlineData("SAVE_20")]
    [InlineData("SAVE/20")]
    [InlineData("SAVE%20")]
    public void RejectsIllegalCharacters(string input)
    {
        Assert.Equal(PromoCodeFormat.IllegalCharacters, PromoCodeValidator.Normalize(input, out _));
    }

    /// <summary>Every rejection carries a message; a valid code carries none.</summary>
    [Fact]
    public void DescribesEveryRejection()
    {
        Assert.Null(PromoCodeValidator.DescribeFailure(PromoCodeFormat.Valid));
        Assert.NotNull(PromoCodeValidator.DescribeFailure(PromoCodeFormat.Empty));
        Assert.NotNull(PromoCodeValidator.DescribeFailure(PromoCodeFormat.TooShort));
        Assert.NotNull(PromoCodeValidator.DescribeFailure(PromoCodeFormat.TooLong));
        Assert.NotNull(PromoCodeValidator.DescribeFailure(PromoCodeFormat.IllegalCharacters));
    }
}
