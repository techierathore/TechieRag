using TechieDesk.Services.Updates;
using Xunit;

namespace TechieDesk.Tests.Updates;

/// <summary>
/// REQ-FN-038b: the version comparison the update check turns on. Every "is there an update"
/// decision reduces to <see cref="ReleaseVersion.CompareTo"/>, so a fault here is not a formatting
/// bug — it is either a missed security fix or an offered downgrade.
/// </summary>
public sealed class ReleaseVersionTests
{
    /// <summary>The workflow's own tag shape parses.</summary>
    [Theory]
    [InlineData("desktop-v1.2.3", 1, 2, 3)]
    [InlineData("DESKTOP-V1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    public void ParsesTheTagShapesTheWorkflowProduces(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.False(version.IsPrerelease);
    }

    /// <summary>
    /// A four-part assembly version parses and its fourth component is ignored. The feed only ever
    /// publishes three, so comparing on a component the feed cannot express would leave an install
    /// permanently "newer" than anything offered and it would never update.
    /// </summary>
    [Fact]
    public void IgnoresTheFourthComponentOfAnAssemblyVersion()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.3.4", out var fourPart));
        Assert.True(ReleaseVersion.TryParse("1.2.3", out var threePart));

        Assert.Equal(0, fourPart.CompareTo(threePart));
    }

    /// <summary>SemVer build metadata is stripped and never participates in ordering.</summary>
    [Fact]
    public void IgnoresBuildMetadata()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.3+abc123", out var stamped));
        Assert.True(ReleaseVersion.TryParse("1.2.3", out var plain));

        Assert.Equal(0, stamped.CompareTo(plain));
        Assert.False(stamped.IsPrerelease);
    }

    /// <summary>Text that is not a version is rejected rather than silently becoming 0.0.0.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    public void RejectsWhatIsNotAVersion(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    /// <summary>Ordinary numeric ordering across each component.</summary>
    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.1.0", "1.2.0")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.9.0", "1.10.0")]
    public void OrdersByNumericComponents(string older, string newer)
    {
        Assert.True(ReleaseVersion.TryParse(older, out var left));
        Assert.True(ReleaseVersion.TryParse(newer, out var right));

        Assert.True(left < right);
        Assert.True(right > left);
    }

    /// <summary>
    /// THE rule that is easy to get backwards: a prerelease is OLDER than the stable release of the
    /// same number. Inverting this would offer every stable user a "newer" beta — a downgrade
    /// presented as an upgrade.
    /// </summary>
    [Fact]
    public void PrereleaseSortsBelowItsOwnStableRelease()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.0-beta.1", out var beta));
        Assert.True(ReleaseVersion.TryParse("1.2.0", out var stable));

        Assert.True(beta < stable);
        Assert.True(beta.IsPrerelease);
        Assert.False(stable.IsPrerelease);
    }

    /// <summary>A prerelease of a higher version still outranks a lower stable one.</summary>
    [Fact]
    public void PrereleaseOfAHigherVersionOutranksALowerStable()
    {
        Assert.True(ReleaseVersion.TryParse("1.3.0-beta.1", out var beta));
        Assert.True(ReleaseVersion.TryParse("1.2.0", out var stable));

        Assert.True(beta > stable);
    }

    /// <summary>Prereleases of the same version order among themselves.</summary>
    [Fact]
    public void OrdersPrereleasesAmongThemselves()
    {
        Assert.True(ReleaseVersion.TryParse("1.2.0-beta.1", out var first));
        Assert.True(ReleaseVersion.TryParse("1.2.0-beta.2", out var second));

        Assert.True(first < second);
    }

    /// <summary>Round-trips through its own text form.</summary>
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-beta.1")]
    public void RoundTripsThroughItsTextForm(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(text, version.ToString());
    }
}
