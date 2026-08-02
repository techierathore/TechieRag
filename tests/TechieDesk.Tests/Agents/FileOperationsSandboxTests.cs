using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 safety boundary — the sandbox itself, tested apart from the tool that uses it. Path
/// containment is the whole protection for <c>file-operations</c>, so it is worth proving against
/// the raw resolver rather than only through a JSON payload, which cannot express every hostile
/// shape (a Windows-style separator, for one, is not valid inside a JSON string escape).
/// </summary>
public sealed class FileOperationsSandboxTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "TechieDeskSandbox" + Guid.NewGuid().ToString("N"), "area");

    /// <summary>Creates the sandbox root.</summary>
    public FileOperationsSandboxTests() => Directory.CreateDirectory(root);

    /// <summary>A plain relative path resolves inside the root.</summary>
    [Fact]
    public void ARelativePathResolvesInsideTheRoot()
    {
        var refusal = new FileOperationsSandbox(root).Resolve("notes.md", true, out var full);

        Assert.Null(refusal);
        Assert.Equal(Path.Combine(root, "notes.md"), full);
    }

    /// <summary>An empty path is the root itself when a directory is what was asked for.</summary>
    [Fact]
    public void AnEmptyPathIsTheRootForListing()
    {
        var refusal = new FileOperationsSandbox(root).Resolve(string.Empty, false, out var full);

        Assert.Null(refusal);
        Assert.Equal(root, full);
    }

    /// <summary>Both separator styles are treated as traversal, not as a filename.</summary>
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"a\..\..\escape.txt")]
    [InlineData("a/b/../../../escape.txt")]
    public void EverySeparatorStyleOfTraversalIsRefused(string path)
    {
        Assert.NotNull(new FileOperationsSandbox(root).Resolve(path, true, out _));
    }

    /// <summary>A home-relative path is refused; the sandbox is the only root there is.</summary>
    [Fact]
    public void AHomeRelativePathIsRefused()
    {
        Assert.NotNull(new FileOperationsSandbox(root).Resolve("~/.ssh/idrsa", true, out _));
    }

    /// <summary>The allow-list refusal names what is allowed, so the model can pick a valid form.</summary>
    [Fact]
    public void TheExtensionRefusalNamesWhatIsAllowed()
    {
        var refusal = new FileOperationsSandbox(root).Resolve("keys.pem", true, out _);

        Assert.Contains(".md", refusal!, StringComparison.Ordinal);
    }

    /// <summary>Display paths are relative to the root, so no absolute path reaches the model.</summary>
    [Fact]
    public void DisplayPathsNeverLeakTheAbsoluteRoot()
    {
        var sandbox = new FileOperationsSandbox(root);

        var display = sandbox.ToDisplayPath(Path.Combine(root, "reports", "q1.csv"));

        Assert.Equal("reports/q1.csv", display);
    }

    /// <summary>A nonsensical size cap falls back to the default rather than disabling the cap.</summary>
    [Fact]
    public void ANonPositiveSizeCapFallsBackToTheDefault()
    {
        Assert.Equal(
            FileOperationsSandbox.DefaultMaxFileBytes,
            new FileOperationsSandbox(root, maxFileBytes: 0).MaxFileBytes);
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
}
