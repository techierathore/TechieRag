using TechieDesk.Services.Support;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-047: the exact text TechieDesk writes onto a support thread for attachments and for a
/// priority change.
/// </summary>
/// <remarks>
/// Asserted word for word because a support engineer reads it. The wire contract has no attachment
/// upload, so the manifest must never imply a file was transferred — that is the difference between
/// a helpful note and a false claim, and it is one adjective wide.
/// </remarks>
public sealed class SupportThreadNoteTests
{
    private static SupportAttachment Attachment(string name, long size, string type) =>
        new(name, size, type, Path.Combine("/tmp", name));

    /// <summary>No attachments means no manifest and no stray blank lines.</summary>
    [Fact]
    public void ComposeWithoutAttachmentsReturnsTheBodyUnchanged()
    {
        Assert.Equal(
            "Export spins forever.",
            SupportThreadNote.Compose("Export spins forever.", Array.Empty<SupportAttachment>()));
    }

    /// <summary>The manifest lists each file with its type and size.</summary>
    [Fact]
    public void ManifestListsEveryAttachmentWithTypeAndSize()
    {
        var manifest = SupportThreadNote.FormatAttachmentManifest(
        [
            Attachment("export-spinner.png", 253952, "image/png"),
            Attachment("techiedesk.log", 2048, "text/plain")
        ]);

        Assert.Contains("export-spinner.png (image/png, 248 KB)", manifest);
        Assert.Contains("techiedesk.log (text/plain, 2 KB)", manifest);
    }

    /// <summary>
    /// The heading says the files are on the sender's device. AppManager has no attachment endpoint,
    /// so a manifest that read "attached" would be a claim the wire never made.
    /// </summary>
    [Fact]
    public void ManifestDoesNotClaimTheFilesWereUploaded()
    {
        var manifest = SupportThreadNote.FormatAttachmentManifest([Attachment("a.png", 10, "image/png")]);

        Assert.Contains("held on the sender's device", manifest);
    }

    /// <summary>A body plus a manifest are separated, and the body is not lost.</summary>
    [Fact]
    public void ComposeKeepsBothTheBodyAndTheManifest()
    {
        var composed = SupportThreadNote.Compose(
            "  Screenshot below.  ", [Attachment("a.png", 10, "image/png")]);

        Assert.StartsWith("Screenshot below.", composed);
        Assert.Contains("a.png", composed);
    }

    /// <summary>An attachment-only comment still carries its manifest.</summary>
    [Fact]
    public void ComposeWithEmptyBodyStillCarriesTheManifest()
    {
        var composed = SupportThreadNote.Compose("   ", [Attachment("a.png", 10, "image/png")]);

        Assert.StartsWith(SupportThreadNote.AttachmentHeading, composed);
    }

    /// <summary>A priority change names both ends using the labels the user chose between.</summary>
    [Fact]
    public void PriorityChangeNamesBothPriorities()
    {
        var note = SupportThreadNote.FormatPriorityChange("Low", "Critical", null);

        Assert.Equal("Priority changed from Low to Critical.", note);
    }

    /// <summary>The optional reason lands on the thread when supplied.</summary>
    [Fact]
    public void PriorityChangeRecordsTheReasonWhenGiven()
    {
        var note = SupportThreadNote.FormatPriorityChange("Low", "High", "  Now blocking the Pune rollout  ");

        Assert.Contains("Priority changed from Low to High.", note);
        Assert.Contains("Reason: Now blocking the Pune rollout", note);
    }

    /// <summary>A blank reason adds no empty "Reason:" line.</summary>
    [Fact]
    public void PriorityChangeOmitsAnEmptyReason()
    {
        Assert.DoesNotContain("Reason:", SupportThreadNote.FormatPriorityChange("Low", "High", "   "));
    }

    /// <summary>An issue with no priority yet reads as "set to", not "changed from  to".</summary>
    [Fact]
    public void PriorityChangeFromUnknownReadsAsSet()
    {
        Assert.Equal("Priority set to Medium.", SupportThreadNote.FormatPriorityChange(null, "Medium", null));
    }
}
