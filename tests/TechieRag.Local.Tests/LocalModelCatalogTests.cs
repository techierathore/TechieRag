using System.Text.RegularExpressions;
using System.Xml.Linq;
using TechieRag.Local.Runtime;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// The shipped model catalog and the package boundary: pinned, hashed downloads with their terms, and
/// no weights inside the package (REQ-RAG-061, REQ-RAG-062 / BRD-101, REQ-RAG-057).
/// </summary>
public sealed partial class LocalModelCatalogTests
{
    /// <summary>
    /// Every file of every shipped model is sized and SHA-256 hashed, and its default source is either
    /// pinned to a repository commit or absent (a mirror-only file set, the owner's own conversion).
    /// </summary>
    [Fact]
    public void CatalogFilesArePinnedAndHashed()
    {
        var variants = LocalModel.All.SelectMany(m => m.Variants).ToList();

        Assert.All(variants.Where(v => v.DefaultBaseUrl is not null), v => Assert.Matches(PinnedCommit(), v.DefaultBaseUrl!));
        Assert.All(variants.SelectMany(v => v.Files), f =>
        {
            Assert.Matches(Sha256(), f.Hash);
            Assert.True(f.Bytes > 0);
        });
    }

    /// <summary>
    /// One format per model: every shipped model has exactly one file set, in ONNX Runtime GenAI's
    /// format, the engine every platform runs (DECISIONS.md 2026-09-25).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-058 EveryModelHasOneOnnxFileSet")]
    public void EveryModelHasOneOnnxFileSet() =>
        Assert.All(LocalModel.All, m => Assert.Equal(LocalModelFormat.OnnxGenAi, Assert.Single(m.Variants).Format));

    /// <summary>
    /// The phone model is the owner's own conversion: its file list is the six files the ONNX Runtime
    /// GenAI 0.16.0 builder wrote, served from the owner's Hugging Face repository at a pinned commit.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-057 PhoneModelIsOwnConversion")]
    public void PhoneModelIsOwnConversion()
    {
        var variant = Assert.Single(LocalModel.Qwen25Instruct05B.Variants);

        Assert.Equal(
            "https://huggingface.co/techierathore/Qwen2.5-0.5B-Instruct-onnx-genai/resolve/c056eda7447d7df98eba0950ffabd1f95d7aab55",
            variant.DefaultBaseUrl);
        Assert.Equal(
            ["chat_template.jinja", "genai_config.json", "model.onnx", "model.onnx.data", "tokenizer.json", "tokenizer_config.json"],
            variant.Files.Select(f => f.FileName));
        Assert.Equal(332_589_148, variant.DownloadBytes);
    }

    /// <summary>Every shipped model carries a licence name and a terms URL the host can show.</summary>
    [Fact]
    public void EveryModelHasTerms() =>
        Assert.All(LocalModel.All, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.LicenceName));
            Assert.Equal("https", m.TermsUrl!.Scheme);
        });

    /// <summary>The phone default runs on phones; the desktop default is refused there.</summary>
    [Fact]
    public void PhoneDefaultIsSmall()
    {
        Assert.True(LocalModel.DefaultFor(isPhone: true).RunsOnPhones);
        Assert.False(LocalModel.DefaultFor(isPhone: false).RunsOnPhones);
    }

    /// <summary>
    /// The package project packs no model weights and holds none on disk: weights are downloaded,
    /// never inside the package or an app bundle.
    /// </summary>
    [Fact]
    public void WeightsAreNeverPacked()
    {
        var projectFolder = Path.Combine(RepoRoot(), "src", "TechieRag.Local");
        string[] weightExtensions = [".gguf", ".onnx", ".data", ".bin", ".safetensors"];
        var project = XDocument.Load(Path.Combine(projectFolder, "TechieRag.Local.csproj"));

        var packed = project.Descendants().Where(e => e.Attribute("Pack")?.Value == "true").Select(e => e.Attribute("Include")?.Value ?? string.Empty);
        var onDisk = Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        Assert.DoesNotContain(packed, p => weightExtensions.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(onDisk, f => weightExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (TechieRag.slnx) not found.");
    }

    [GeneratedRegex("/resolve/[0-9a-f]{40}(/|$)")]
    private static partial Regex PinnedCommit();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
