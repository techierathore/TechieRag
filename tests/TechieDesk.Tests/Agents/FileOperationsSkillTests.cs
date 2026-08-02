using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 safety boundary — the <c>file-operations</c> skill against a REAL directory. The
/// point of these tests is not that reading and writing work; it is that an LLM-driven agent given
/// hostile paths still cannot leave the one directory it was granted, and that the destructive
/// verbs do not exist to be called.
/// </summary>
public sealed class FileOperationsSkillTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "TechieDeskFileSkill" + Guid.NewGuid().ToString("N"), "area");

    private readonly string outside;

    /// <summary>Creates a sandbox root with a file in it, and a secret file outside it.</summary>
    public FileOperationsSkillTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "notes.md"), "# Notes\nInside the sandbox.");

        outside = Path.Combine(Directory.GetParent(root)!.FullName, "secrets.txt");
        File.WriteAllText(outside, "API key nobody should read");
    }

    /// <summary>The skill binds to the catalogue name the toggles and the resolver use.</summary>
    [Fact]
    public void BindsToTheCatalogueName()
    {
        Assert.Equal(SkillCatalog.FileOperations, FileOperationsSkill.Create(null).SkillName);
    }

    /// <summary>With no file area configured the skill reports itself unavailable.</summary>
    [Fact]
    public async Task WithNoSandboxItReportsUnavailable()
    {
        var result = await FileOperationsSkill.Create(null)
            .Invoke("""{"operation":"list"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
    }

    /// <summary>Listing the root shows what is actually there.</summary>
    [Fact]
    public async Task ListingTheRootShowsItsFiles()
    {
        var result = await Skill().Invoke("""{"operation":"list"}""", CancellationToken.None);

        Assert.Contains("notes.md", result, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.txt", result, StringComparison.Ordinal);
    }

    /// <summary>Reading a file inside the sandbox returns its real content.</summary>
    [Fact]
    public async Task ReadingAFileReturnsItsContent()
    {
        var result = await Skill().Invoke(
            """{"operation":"read","path":"notes.md"}""", CancellationToken.None);

        Assert.Contains("Inside the sandbox.", result, StringComparison.Ordinal);
    }

    /// <summary>Writing creates the file on disk, and the tool says it was a new file.</summary>
    [Fact]
    public async Task WritingCreatesTheFileOnDisk()
    {
        var result = await Skill().Invoke(
            """{"operation":"write","path":"reports/summary.md","content":"Total: 42"}""",
            CancellationToken.None);

        Assert.Contains("Wrote", result, StringComparison.Ordinal);
        Assert.Equal("Total: 42", File.ReadAllText(Path.Combine(root, "reports", "summary.md")));
    }

    /// <summary>Replacing an existing file is reported as an overwrite, not as a plain write.</summary>
    [Fact]
    public async Task OverwritingIsReportedAsSuch()
    {
        var result = await Skill().Invoke(
            """{"operation":"write","path":"notes.md","content":"replaced"}""", CancellationToken.None);

        Assert.Contains("Overwrote", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE boundary test. Every shape of escape an agent might compose is refused, and the file
    /// outside the sandbox is never read.
    /// </summary>
    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("subdir/../../secrets.txt")]
    [InlineData("./../secrets.txt")]
    public async Task TraversalOutOfTheSandboxIsRefused(string path)
    {
        var result = await Skill().Invoke(
            $$"""{"operation":"read","path":"{{path}}"}""", CancellationToken.None);

        Assert.StartsWith("Refused:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("API key", result, StringComparison.Ordinal);
    }

    /// <summary>An absolute path is refused however plausible it looks.</summary>
    [Fact]
    public async Task AnAbsolutePathIsRefused()
    {
        var result = await Skill().Invoke(
            $$"""{"operation":"read","path":"{{outside.Replace("\\", "/")}}"}""", CancellationToken.None);

        Assert.Contains("absolute path", result, StringComparison.Ordinal);
        Assert.DoesNotContain("API key", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// A symbolic link planted inside the sandbox that points out of it is refused too — checking
    /// only the composed path would make the sandbox a suggestion.
    /// </summary>
    [Fact]
    public async Task ALinkPointingOutOfTheSandboxIsRefused()
    {
        var link = Path.Combine(root, "leak.txt");
        File.CreateSymbolicLink(link, outside);

        var result = await Skill().Invoke(
            """{"operation":"read","path":"leak.txt"}""", CancellationToken.None);

        Assert.Contains("points outside", result, StringComparison.Ordinal);
        Assert.DoesNotContain("API key", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file kind outside the allow-list is refused, so an agent cannot read a key store or a
    /// database file out through a chat reply.
    /// </summary>
    [Fact]
    public async Task AnExtensionOutsideTheAllowListIsRefused()
    {
        File.WriteAllText(Path.Combine(root, "store.db"), "binary-ish");

        var result = await Skill().Invoke(
            """{"operation":"read","path":"store.db"}""", CancellationToken.None);

        Assert.Contains("not readable or writable by agents", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// The destructive verbs are not implemented at all. Asking for one is refused with the allowed
    /// list named, so the model stops rather than trying a synonym.
    /// </summary>
    [Theory]
    [InlineData("delete")]
    [InlineData("move")]
    [InlineData("rename")]
    [InlineData("execute")]
    public async Task DestructiveVerbsAreRefused(string operation)
    {
        var result = await Skill().Invoke(
            $$"""{"operation":"{{operation}}","path":"notes.md"}""", CancellationToken.None);

        Assert.Contains("cannot delete, move or rename", result, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "notes.md")));
    }

    /// <summary>A write over the size cap is refused before anything reaches the disk.</summary>
    [Fact]
    public async Task AnOversizedWriteIsRefused()
    {
        var sandbox = new FileOperationsSandbox(root, maxFileBytes: 16);

        var result = await FileOperationsSkill.Create(sandbox).Invoke(
            """{"operation":"write","path":"big.txt","content":"far more than sixteen bytes of text"}""",
            CancellationToken.None);

        Assert.Contains("over the 16-byte limit", result, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "big.txt")));
    }

    /// <summary>A read over the size cap is refused rather than streamed into the transcript.</summary>
    [Fact]
    public async Task AnOversizedReadIsRefused()
    {
        File.WriteAllText(Path.Combine(root, "big.txt"), new string('x', 200));
        var sandbox = new FileOperationsSandbox(root, maxFileBytes: 16);

        var result = await FileOperationsSkill.Create(sandbox).Invoke(
            """{"operation":"read","path":"big.txt"}""", CancellationToken.None);

        Assert.Contains("over the 16-byte limit", result, StringComparison.Ordinal);
    }

    /// <summary>A missing file is reported plainly rather than thrown.</summary>
    [Fact]
    public async Task AMissingFileIsReportedNotThrown()
    {
        var result = await Skill().Invoke(
            """{"operation":"read","path":"absent.md"}""", CancellationToken.None);

        Assert.Contains("No file", result, StringComparison.Ordinal);
    }

    /// <summary>A malformed payload becomes a reportable bad call, never an unhandled exception.</summary>
    [Fact]
    public async Task AMalformedCallIsReportedNotThrown()
    {
        var result = await Skill().Invoke("not json at all", CancellationToken.None);

        Assert.Contains("No operation supplied", result, StringComparison.Ordinal);
    }

    /// <summary>A sandbox whose root does not exist reports unavailability, not an error.</summary>
    [Fact]
    public async Task AMissingRootReportsUnavailable()
    {
        var sandbox = new FileOperationsSandbox(Path.Combine(root, "nowhere"));

        var result = await FileOperationsSkill.Create(sandbox)
            .Invoke("""{"operation":"list"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
    }

    /// <summary>Removes the throwaway directory tree.</summary>
    public void Dispose()
    {
        var parent = Directory.GetParent(root)?.FullName;
        if (parent is not null && Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>Builds the skill over the throwaway sandbox.</summary>
    /// <returns>The skill implementation.</returns>
    private SkillImplementation Skill() => FileOperationsSkill.Create(new FileOperationsSandbox(root));
}
