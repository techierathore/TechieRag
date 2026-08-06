using TechieDesk.Services.Data;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// REQ-RAG-011 / BRD-41: the document library accepts every type backed by a TechieRag
/// processor — including the XLSX/PPTX/CSV formats added by REQ-RAG-033 — and gives a clear
/// per-file rejection for formats that genuinely have no processor.
/// </summary>
public sealed class UploadTypePolicyTests
{
    /// <summary>Every processor-backed type is accepted, XLSX and PPTX included.</summary>
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("spec.docx")]
    [InlineData("budget.xlsx")]
    [InlineData("kickoff.pptx")]
    [InlineData("people.csv")]
    [InlineData("metrics.tsv")]
    [InlineData("notes.md")]
    [InlineData("readme.txt")]
    [InlineData("page.html")]
    [InlineData("config.json")]
    [InlineData("settings.toml")]
    [InlineData("Program.cs")]
    public void AcceptsSupportedTypes(string fileName)
    {
        Assert.True(UploadTypePolicy.IsSupported(fileName));
        Assert.Null(UploadTypePolicy.GetRejection(fileName));
    }

    /// <summary>Extension matching ignores case, so uppercase uploads still land.</summary>
    [Theory]
    [InlineData("BUDGET.XLSX")]
    [InlineData("Kickoff.PPTX")]
    public void AcceptsSupportedTypesRegardlessOfCase(string fileName)
    {
        Assert.True(UploadTypePolicy.IsSupported(fileName));
    }

    /// <summary>Binary formats with no processor are rejected with a readable reason.</summary>
    [Theory]
    [InlineData("photo.png")]
    [InlineData("clip.mp4")]
    [InlineData("bundle.zip")]
    [InlineData("tool.exe")]
    [InlineData("model.onnx")]
    public void RejectsUnsupportedBinaryTypes(string fileName)
    {
        var rejection = UploadTypePolicy.GetRejection(fileName);

        Assert.False(UploadTypePolicy.IsSupported(fileName));
        Assert.NotNull(rejection);
        Assert.Equal(UploadTypePolicy.UnsupportedTypeKey, rejection.MessageKey);
    }

    /// <summary>Legacy binary Office formats stay rejected — only the OpenXml containers are supported.</summary>
    [Theory]
    [InlineData("old.xls")]
    [InlineData("old.ppt")]
    [InlineData("old.doc")]
    public void RejectsLegacyBinaryOfficeTypes(string fileName)
    {
        Assert.False(UploadTypePolicy.IsSupported(fileName));
    }

    /// <summary>No rejection key still promises a "later release" — the wait is over.</summary>
    [Theory]
    [InlineData("budget.xlsx")]
    [InlineData("old.xls")]
    [InlineData("photo.png")]
    public void NeverPromisesALaterRelease(string fileName)
    {
        var key = UploadTypePolicy.GetRejection(fileName)?.MessageKey ?? string.Empty;

        Assert.DoesNotContain("later", key, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Files with no extension are rejected rather than guessed at.</summary>
    [Fact]
    public void RejectsFileWithoutExtension()
    {
        var rejection = UploadTypePolicy.GetRejection("LICENSE");

        Assert.NotNull(rejection);
        Assert.Equal(UploadTypePolicy.NoExtensionKey, rejection.MessageKey);
        Assert.Equal("LICENSE", Assert.Single(rejection.Arguments));
    }

    /// <summary>The picker filter advertises the newly supported spreadsheet and deck types.</summary>
    [Fact]
    public void AcceptFilterAdvertisesOfficeTypes()
    {
        Assert.Contains(".xlsx", UploadTypePolicy.AcceptTypes);
        Assert.Contains(".pptx", UploadTypePolicy.AcceptTypes);
        Assert.Contains(".csv", UploadTypePolicy.AcceptTypes);
    }
}
