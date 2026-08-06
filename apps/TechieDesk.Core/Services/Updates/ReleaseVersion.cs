using System.Globalization;

namespace TechieDesk.Services.Updates;

/// <summary>
/// A comparable application version parsed from a release tag or an assembly version string
/// (REQ-FN-038b / BRD-131).
/// </summary>
/// <remarks>
/// <para><b>Why not <see cref="Version"/>.</b> Release tags carry a prerelease suffix
/// (<c>desktop-v1.2.0-beta.1</c>) that <see cref="Version"/> cannot parse at all, and the ordering
/// rule for that suffix is the opposite of intuition: <c>1.2.0-beta.1</c> is *older* than
/// <c>1.2.0</c>, because a prerelease precedes the release it leads to. Getting that backwards would
/// offer every stable user a downgrade to a beta, so it is expressed once here and tested.</para>
/// <para>This is a deliberately small subset of SemVer: a numeric core plus an optional prerelease
/// label. Build metadata (<c>+sha</c>) is parsed off and ignored for ordering, per SemVer.</para>
/// </remarks>
public readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    /// <summary>Tag prefix used by the desktop release workflow (see publish-desktop.yml).</summary>
    /// <remarks>
    /// The desktop app deliberately tags <c>desktop-v*</c> rather than the <c>v*</c> the NuGet
    /// workflow uses, because the library and the app version independently. Filtering on this
    /// prefix is what stops a library release from being offered as an app update.
    /// </remarks>
    public const string TagPrefix = "desktop-v";

    private ReleaseVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>Gets the major component.</summary>
    public int Major { get; }

    /// <summary>Gets the minor component.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch component.</summary>
    public int Patch { get; }

    /// <summary>Gets the prerelease label, or null for a stable release.</summary>
    public string? Prerelease { get; }

    /// <summary>Gets a value indicating whether this version is a prerelease.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Parses a version from a release tag (<c>desktop-v1.2.0</c>), a bare version (<c>1.2.0</c>),
    /// or an informational assembly version (<c>1.2.0+abc123</c>).
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="version">The parsed version when this method returns true.</param>
    /// <returns>True when <paramref name="value"/> was a version this app understands.</returns>
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[TagPrefix.Length..];
        }
        else if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        // SemVer build metadata never participates in ordering, so drop it before anything else.
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        // A fourth component is accepted (assembly versions are 4-part) but ignored: the release
        // feed only ever publishes three, so comparing on a component the feed cannot express would
        // make an installed build look permanently newer than anything offered.
        Span<int> numbers = stackalloc int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length)
            {
                numbers[i] = 0;
                continue;
            }

            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return false;
            }

            numbers[i] = n;
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        // Equal cores: a prerelease sorts BELOW the stable release of the same number.
        return (Prerelease, other.Prerelease) switch
        {
            (null, null) => 0,
            (null, not null) => 1,
            (not null, null) => -1,
            var (mine, theirs) => string.CompareOrdinal(mine, theirs),
        };
    }

    /// <summary>Determines whether the left version precedes the right.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is older.</returns>
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether the left version follows the right.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is newer.</returns>
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether the left version precedes or equals the right.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is not newer.</returns>
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left version follows or equals the right.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is not older.</returns>
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() =>
        Prerelease is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Prerelease}");
}
