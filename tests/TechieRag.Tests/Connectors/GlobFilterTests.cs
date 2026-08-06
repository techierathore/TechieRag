using TechieRag.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-019 / BRD-63: the include/exclude semantics a user relies on to keep lockfiles and build
/// output out of an index.
/// </summary>
public sealed class GlobFilterTests
{
    /// <summary>A star matches within one segment and never crosses a slash.</summary>
    [Theory]
    [InlineData("docs/*.md", "docs/readme.md", true)]
    [InlineData("docs/*.md", "docs/deep/readme.md", false)]
    public void StarStaysInsideOneSegment(string pattern, string path, bool expected) =>
        Assert.Equal(expected, new GlobFilter([pattern], null).IsMatch(path));

    /// <summary>A double star crosses any number of segments, including none.</summary>
    [Theory]
    [InlineData("docs/**", "docs/readme.md", true)]
    [InlineData("docs/**", "docs/a/b/c.md", true)]
    [InlineData("**/test/**", "src/test/a.cs", true)]
    [InlineData("docs/**", "src/readme.md", false)]
    public void DoubleStarCrossesSegments(string pattern, string path, bool expected) =>
        Assert.Equal(expected, new GlobFilter([pattern], null).IsMatch(path));

    /// <summary>
    /// A pattern with no slash matches the file name anywhere in the tree. Without this rule the
    /// most common pattern anyone types — "*.md" — would match only files in the root.
    /// </summary>
    [Fact]
    public void BareNamePatternMatchesAtAnyDepth()
    {
        var filter = new GlobFilter(["*.md"], null);

        Assert.True(filter.IsMatch("readme.md"));
        Assert.True(filter.IsMatch("docs/guides/deep/readme.md"));
    }

    /// <summary>An exclude beats an include that also matches.</summary>
    [Fact]
    public void ExcludeWinsOverInclude()
    {
        var filter = new GlobFilter(["**/*.json"], ["package-lock.json"]);

        Assert.True(filter.IsMatch("config/app.json"));
        Assert.False(filter.IsMatch("package-lock.json"));
    }

    /// <summary>No include patterns means everything is included.</summary>
    [Fact]
    public void EmptyIncludeListAdmitsEverything()
    {
        var filter = new GlobFilter(null, null);

        Assert.True(filter.IsMatch("anything/at/all.bin"));
    }

    /// <summary>Excludes still apply when there are no includes.</summary>
    [Fact]
    public void ExcludeAppliesWithoutIncludes()
    {
        var filter = new GlobFilter(null, ["**/node/modules/**"]);

        Assert.False(filter.IsMatch("web/node/modules/left/pad.js"));
        Assert.True(filter.IsMatch("web/src/app.js"));
    }

    /// <summary>A question mark matches exactly one character, and not a slash.</summary>
    [Theory]
    [InlineData("v?.md", "v1.md", true)]
    [InlineData("v?.md", "v12.md", false)]
    public void QuestionMarkMatchesOneCharacter(string pattern, string path, bool expected) =>
        Assert.Equal(expected, new GlobFilter([pattern], null).IsMatch(path));

    /// <summary>Matching ignores case, because a user who writes *.MD means *.md.</summary>
    [Fact]
    public void MatchingIgnoresCase() =>
        Assert.True(new GlobFilter(["*.MD"], null).IsMatch("docs/Readme.md"));

    /// <summary>Backslashes are treated as path separators so Windows-style paths still match.</summary>
    [Fact]
    public void NormalizesWindowsSeparators() =>
        Assert.True(new GlobFilter(["docs/**"], null).IsMatch("docs\\deep\\readme.md"));

    /// <summary>An empty path matches nothing rather than everything.</summary>
    [Fact]
    public void RejectsAnEmptyPath() =>
        Assert.False(GlobFilter.AllowAll.IsMatch("   "));

    /// <summary>Regex metacharacters in a pattern are literal, not operators.</summary>
    [Fact]
    public void EscapesRegexMetacharacters()
    {
        var filter = new GlobFilter(["a+b.md"], null);

        Assert.True(filter.IsMatch("a+b.md"));
        Assert.False(filter.IsMatch("aab.md"));
    }
}
