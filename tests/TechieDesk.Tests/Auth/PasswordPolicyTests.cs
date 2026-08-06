using TechieDesk.Services.Auth;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-UI-006 / BRD-12: the client-side password complexity pre-flight — at least 8 chars with
/// an uppercase letter, a digit, and a special character.
/// </summary>
public sealed class PasswordPolicyTests
{
    /// <summary>A password meeting every rule is accepted.</summary>
    [Theory]
    [InlineData("Passw0rd!")]
    [InlineData("Aa1@aaaa")]
    [InlineData("Str0ng#Password")]
    public void AcceptsCompliantPasswords(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password));
    }

    /// <summary>A password missing any single rule is rejected.</summary>
    [Theory]
    [InlineData("")]                 // empty
    [InlineData("Aa1@aa")]           // too short (6 chars)
    [InlineData("password1!")]       // no uppercase
    [InlineData("Password!")]        // no digit
    [InlineData("Password1")]        // no special character
    public void RejectsNonCompliantPasswords(string password)
    {
        Assert.False(PasswordPolicy.IsValid(password));
    }

    /// <summary>A null password is rejected without throwing.</summary>
    [Fact]
    public void RejectsNull()
    {
        Assert.False(PasswordPolicy.IsValid(null));
    }
}
