using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Support;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-047 with REQ-FN-037: where a staged support attachment lands, and what it refuses to
/// write.
/// </summary>
/// <remarks>
/// The location assertions are the point of this file. REQ-FN-037's single-authority rule has
/// already been broken twice in this codebase — by the Serilog sink and by
/// <c>AppDbConnectionFactory</c>, both of which resolved their own path and both of which would
/// have written inside the read-only signed <c>.app</c> bundle. A test that only checked "a file
/// appeared" would pass in both of those defects.
/// </remarks>
public sealed class SupportAttachmentStoreTests : IDisposable
{
    private readonly string sandbox;
    private readonly SupportAttachmentStore store;

    /// <summary>Points the data directory at a private sandbox for one test.</summary>
    public SupportAttachmentStoreTests()
    {
        sandbox = Path.Combine(Path.GetTempPath(), "techiedesk-support-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataDirectory.ConfigKey] = sandbox
            })
            .Build();

        store = new SupportAttachmentStore(configuration, NullLogger<SupportAttachmentStore>.Instance);
    }

    /// <summary>Removes the sandbox.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private static Stream Bytes(int count) => new MemoryStream(new byte[count]);

    /// <summary>
    /// The staged file lands under the data directory's support-attachments folder — the path the
    /// single authority owns — and nowhere near the executable.
    /// </summary>
    [Fact]
    public async Task AttachmentIsWrittenUnderTheDataDirectory()
    {
        var draft = store.BeginDraft();

        var attachment = await store.SaveAsync(draft, "shot.png", "image/png", Bytes(1024));

        var expectedRoot = Path.Combine(sandbox, DataDirectory.SupportAttachmentsDirectoryName);
        Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, attachment.FullPath);
        Assert.True(File.Exists(attachment.FullPath));
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Equal("shot.png", attachment.FileName);
    }

    /// <summary>
    /// Nothing is ever written beside the running assembly. This is the REQ-FN-037 defect shape
    /// stated as an assertion rather than as a comment.
    /// </summary>
    /// <remarks>
    /// Asserted against the resolved root and the written path rather than against "does a folder
    /// exist next to the executable": ambient file-system state is shared with every other test run
    /// on this machine, and a residue-based assertion fails for reasons that have nothing to do with
    /// the code under test.
    /// </remarks>
    [Fact]
    public async Task NothingIsWrittenBesideTheExecutable()
    {
        var draft = store.BeginDraft();

        var attachment = await store.SaveAsync(draft, "shot.png", "image/png", Bytes(64));

        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        Assert.DoesNotContain(baseDirectory, Path.GetFullPath(attachment.FullPath));
        Assert.DoesNotContain(baseDirectory, Path.GetFullPath(store.RootDirectory));
    }

    /// <summary>A disallowed type is refused before a single byte is written.</summary>
    [Fact]
    public async Task DisallowedTypeIsRefusedAndLeavesNoFile()
    {
        var draft = store.BeginDraft();

        await Assert.ThrowsAsync<SupportAttachmentRejectedException>(
            () => store.SaveAsync(draft, "payload.exe", "application/octet-stream", Bytes(64)));

        var draftDirectory = Path.Combine(sandbox, DataDirectory.SupportAttachmentsDirectoryName, draft);
        Assert.True(!Directory.Exists(draftDirectory) || Directory.GetFiles(draftDirectory).Length == 0);
    }

    /// <summary>
    /// The cap is enforced against the bytes that actually arrive, not against a declared length,
    /// and the partial file is removed rather than left as a large orphan.
    /// </summary>
    [Fact]
    public async Task OversizeStreamIsRefusedAndThePartialFileIsDeleted()
    {
        var draft = store.BeginDraft();
        var oversize = Bytes((int)SupportAttachmentPolicy.MaxFileSizeBytes + 1024);

        var exception = await Assert.ThrowsAsync<SupportAttachmentRejectedException>(
            () => store.SaveAsync(draft, "huge.png", "image/png", oversize));

        Assert.Contains("10 MB", exception.Message);
        var draftDirectory = Path.Combine(sandbox, DataDirectory.SupportAttachmentsDirectoryName, draft);
        Assert.Empty(Directory.GetFiles(draftDirectory));
    }

    /// <summary>A file exactly on the cap is written, so the limit is a limit and not a margin.</summary>
    [Fact]
    public async Task FileExactlyOnTheCapIsAccepted()
    {
        var draft = store.BeginDraft();

        var attachment = await store.SaveAsync(
            draft, "atlimit.png", "image/png", Bytes((int)SupportAttachmentPolicy.MaxFileSizeBytes));

        Assert.Equal(SupportAttachmentPolicy.MaxFileSizeBytes, attachment.SizeBytes);
    }

    /// <summary>
    /// A traversal-shaped name cannot escape the attachments folder: it is staged as its leaf name
    /// inside the draft, not resolved against the parent directories it names.
    /// </summary>
    [Fact]
    public async Task TraversalNameCannotEscapeTheAttachmentsFolder()
    {
        var draft = store.BeginDraft();

        var attachment = await store.SaveAsync(
            draft, "../../../escaped.png", "image/png", Bytes(32));

        var expectedRoot = Path.Combine(sandbox, DataDirectory.SupportAttachmentsDirectoryName);
        Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, attachment.FullPath);
        Assert.Equal("escaped.png", attachment.FileName);
        Assert.False(File.Exists(Path.Combine(sandbox, "..", "escaped.png")));
    }

    /// <summary>Two files with the same name both survive instead of one overwriting the other.</summary>
    [Fact]
    public async Task SameNameTwiceKeepsBothFiles()
    {
        var draft = store.BeginDraft();

        var first = await store.SaveAsync(draft, "shot.png", "image/png", Bytes(10));
        var second = await store.SaveAsync(draft, "shot.png", "image/png", Bytes(20));

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.True(File.Exists(first.FullPath));
        Assert.True(File.Exists(second.FullPath));
    }

    /// <summary>Discarding a draft removes its files, so an abandoned form leaves no litter.</summary>
    [Fact]
    public async Task DiscardingADraftRemovesItsFiles()
    {
        var draft = store.BeginDraft();
        var attachment = await store.SaveAsync(draft, "shot.png", "image/png", Bytes(16));

        store.DiscardDraft(draft);

        Assert.False(File.Exists(attachment.FullPath));
    }

    /// <summary>Removing one attachment leaves the rest of the draft alone.</summary>
    [Fact]
    public async Task RemovingOneAttachmentLeavesTheOthers()
    {
        var draft = store.BeginDraft();
        var kept = await store.SaveAsync(draft, "kept.png", "image/png", Bytes(16));
        var dropped = await store.SaveAsync(draft, "dropped.pdf", "application/pdf", Bytes(16));

        store.Remove(dropped);

        Assert.False(File.Exists(dropped.FullPath));
        Assert.True(File.Exists(kept.FullPath));
    }

    /// <summary>A missing content type is inferred from the extension rather than left blank.</summary>
    [Fact]
    public async Task ContentTypeIsInferredWhenTheClientSendsNone()
    {
        var draft = store.BeginDraft();

        var attachment = await store.SaveAsync(draft, "report.pdf", null, Bytes(8));

        Assert.Equal("application/pdf", attachment.ContentType);
    }

    /// <summary>An empty stream is refused and nothing is left behind.</summary>
    [Fact]
    public async Task EmptyStreamIsRefused()
    {
        var draft = store.BeginDraft();

        await Assert.ThrowsAsync<SupportAttachmentRejectedException>(
            () => store.SaveAsync(draft, "empty.png", "image/png", new MemoryStream()));

        var draftDirectory = Path.Combine(sandbox, DataDirectory.SupportAttachmentsDirectoryName, draft);
        Assert.Empty(Directory.GetFiles(draftDirectory));
    }

    /// <summary>The bytes on disk are the bytes supplied — staging is not lossy.</summary>
    [Fact]
    public async Task StagedBytesRoundTrip()
    {
        var draft = store.BeginDraft();
        var payload = Encoding.UTF8.GetBytes("2026-07-27 [ERR] something went wrong");

        var attachment = await store.SaveAsync(
            draft, "techiedesk.log", "text/plain", new MemoryStream(payload));

        Assert.Equal(payload, await File.ReadAllBytesAsync(attachment.FullPath));
    }
}
