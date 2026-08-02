using TechieDesk.Services.Support;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-047 / BRD-141: the attachment type allowlist and size cap.
/// </summary>
/// <remarks>
/// These assertions are deliberately negative-heavy. The screen's own file picker filters by
/// extension, so the only inputs that reach this policy in anger are the ones that bypassed the
/// picker — a dragged file, a pasted clipboard payload, or a name crafted to look like an image.
/// </remarks>
public sealed class SupportAttachmentPolicyTests
{
    /// <summary>Every type the requirement names is accepted, in either letter case.</summary>
    [Theory]
    [InlineData("screenshot.png")]
    [InlineData("Screenshot.PNG")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("invoice.pdf")]
    [InlineData("techiedesk-20260727.log")]
    public void AllowedTypesAreAccepted(string fileName)
    {
        Assert.Null(SupportAttachmentPolicy.GetRejectionReason(fileName, 2048));
    }

    /// <summary>
    /// Anything outside the allowlist is refused — including formats the document library happily
    /// ingests, because an attachment is a file handed to a human, not a file that gets indexed.
    /// </summary>
    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.sh")]
    [InlineData("contract.docx")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void TypesOutsideTheAllowlistAreRejected(string fileName)
    {
        var reason = SupportAttachmentPolicy.GetRejectionReason(fileName, 2048);

        Assert.NotNull(reason);
        Assert.Contains("PNG, JPG, PDF, LOG", reason);
    }

    /// <summary>A double extension is judged on the real one, which is the last one.</summary>
    [Fact]
    public void DoubleExtensionIsJudgedOnTheFinalExtension()
    {
        Assert.NotNull(SupportAttachmentPolicy.GetRejectionReason("screenshot.png.exe", 2048));
        Assert.Null(SupportAttachmentPolicy.GetRejectionReason("report.exe.pdf", 2048));
    }

    /// <summary>A file exactly on the cap is accepted; one byte past it is not.</summary>
    [Fact]
    public void SizeCapIsInclusiveAtTheLimitAndExclusiveAboveIt()
    {
        Assert.Null(SupportAttachmentPolicy.GetRejectionReason(
            "big.png", SupportAttachmentPolicy.MaxFileSizeBytes));

        var reason = SupportAttachmentPolicy.GetRejectionReason(
            "big.png", SupportAttachmentPolicy.MaxFileSizeBytes + 1);

        Assert.NotNull(reason);
        Assert.Contains("10 MB", reason);
    }

    /// <summary>An empty file is refused rather than attached as a zero-byte curiosity.</summary>
    [Fact]
    public void EmptyFileIsRejected()
    {
        Assert.NotNull(SupportAttachmentPolicy.GetRejectionReason("empty.png", 0));
    }

    /// <summary>The rejection message names the offending file, not just the rule.</summary>
    [Fact]
    public void RejectionMessageNamesTheFile()
    {
        var reason = SupportAttachmentPolicy.GetRejectionReason("secret-plans.exe", 10);

        Assert.Contains("secret-plans.exe", reason);
    }

    /// <summary>Path separators of BOTH conventions are stripped from a staged file name.</summary>
    [Theory]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("..\\..\\windows\\system32\\evil.png", "evil.png")]
    [InlineData("/absolute/path/shot.png", "shot.png")]
    public void SafeFileNameKeepsOnlyTheLeaf(string offered, string expected)
    {
        Assert.Equal(expected, SupportAttachmentPolicy.SafeFileName(offered));
    }

    /// <summary>A name that reduces to a traversal token becomes the fallback, never "..".</summary>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SafeFileNameRefusesTraversalTokens(string? offered)
    {
        Assert.Equal(SupportAttachmentPolicy.FallbackFileName, SupportAttachmentPolicy.SafeFileName(offered));
    }

    /// <summary>An absurdly long name is truncated but keeps its extension, so the type survives.</summary>
    [Fact]
    public void SafeFileNameTruncatesButKeepsTheExtension()
    {
        var offered = new string('a', 500) + ".png";

        var safe = SupportAttachmentPolicy.SafeFileName(offered);

        Assert.EndsWith(".png", safe);
        Assert.Equal(SupportAttachmentPolicy.MaxFileNameLength + ".png".Length, safe.Length);
    }
}
