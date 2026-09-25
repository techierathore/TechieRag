using System.Globalization;
using System.Text;
using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.Conformance;
using TechieRag.Models;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// REQ-RAG-108 / BRD-166 for real: Arm's Gemma 3 1B named on Hugging Face, its licence shown first,
/// downloaded through the library, every file checked against Hugging Face's fingerprint, then answering
/// a prompt and a typed request with its own chat template and no SentencePiece markers in the text.
/// Opt-in (<see cref="LiveHuggingFaceFactAttribute"/>).
/// </summary>
[Collection(LiveLocalLlmCollection.Name)]
public sealed class LiveHuggingFaceTests
{
    private const string Repository = "Arm/gemma-3-1b-instruct-onnx-genai-int4-emb-int8";

    private readonly ITestOutputHelper output;

    /// <summary>Creates the test.</summary>
    /// <param name="output">The test's output.</param>
    public LiveHuggingFaceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// UseLocalLlm's model from FromHuggingFace: the terms carry the model card's licence before any file,
    /// the download lands only the seven runtime files, each re-hashed here against the fingerprint
    /// Hugging Face lists, and the provider answers a prompt, an indented-JSON prompt (no U+2581 in the
    /// streamed text) and a typed request.
    /// </summary>
    [LiveHuggingFaceFact(DisplayName = "REQ-RAG-108 LiveHuggingFaceGemmaAnswers")]
    public async Task LiveHuggingFaceGemmaAnswers()
    {
        var artifacts = Path.Combine(Path.GetDirectoryName(RepoFiles.Locate("TechieRag.slnx"))!, "tests", ".artifacts", "hf-live");
        var model = LocalModel.FromHuggingFace(Repository, null, null, Path.Combine(artifacts, "model-root"));
        LocalModelTerms? shown = null;
        var options = new LocalLlmOptions
        {
            Model = model,
            Temperature = 0f,
            ConfirmTermsAsync = (terms, _) => Task.FromResult((shown = terms) is not null)
        };
        using var provider = new LocalLlmProvider(options);
        var report = new StringBuilder();

        var terms = await provider.GetTermsAsync();
        report.AppendLine(CultureInfo.InvariantCulture, $"terms: {terms.LicenceName} {terms.TermsUrl} {terms.DownloadBytes} bytes");
        var loadWatch = System.Diagnostics.Stopwatch.StartNew();
        await provider.LoadAsync();
        report.AppendLine(CultureInfo.InvariantCulture, $"ready in {loadWatch.Elapsed.TotalSeconds:0.0} s; terms asked: {shown is not null}; commit {model.Resolution!.Snapshot.Commit}");

        var resolution = model.Resolution!;
        foreach (var file in resolution.Variant.Files)
        {
            var path = Path.Combine(resolution.Directory, file.FileName);
            var actual = file.HashKind == LocalModelHashKind.GitBlobSha1
                ? await LocalModelStore.HashGitBlobAsync(path, CancellationToken.None)
                : await LocalModelStore.HashAsync(path, CancellationToken.None);
            Assert.Equal(file.Hash, actual);
            report.AppendLine(CultureInfo.InvariantCulture, $"  {file.FileName} {file.Bytes} {file.HashKind} {file.Hash[..12]}… ok");
        }

        var speeds = new List<double>();
        provider.OnCompletionCompleted += (_, e) => speeds.Add(e.OutputTokens / Math.Max(e.Duration.TotalSeconds, 0.001));

        var sea = await provider.CompleteAsync("Write one sentence about the sea.", new LlmCompletionOptions { MaxTokens = 64 });
        var indented = new StringBuilder();
        await foreach (var piece in provider.CompleteStreamAsync(
                           "Give Paris's city, country and continent as a JSON object indented with four spaces per level.",
                           new LlmCompletionOptions { MaxTokens = 96 }))
        {
            indented.Append(piece);
        }

        var person = await provider.CompleteAsync<LocalLlmConformanceTests.PersonAnswer>("Give the name and age at death of Ada Lovelace.");

        report.AppendLine(CultureInfo.InvariantCulture, $"sea: {sea.Content} ({sea.Usage.InputTokens} in, {sea.Usage.OutputTokens} out)");
        report.AppendLine(CultureInfo.InvariantCulture, $"indented: {indented}");
        report.AppendLine(CultureInfo.InvariantCulture, $"typed: {person.Name}, {person.Age}");
        report.AppendLine(CultureInfo.InvariantCulture, $"context {model.ContextLength}, kv {model.KvBytesPerToken} B/token, tokens/s {string.Join(", ", speeds.Select(s => s.ToString("0", CultureInfo.InvariantCulture)))}");
        output.WriteLine(report.ToString());
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "report.txt"), report.ToString());

        Assert.Equal("gemma", terms.LicenceName);
        Assert.Equal(7, resolution.Variant.Files.Count);
        Assert.False(string.IsNullOrWhiteSpace(sea.Content));
        Assert.DoesNotContain(DecodedText.SentencePieceSpace, sea.Content + indented);
        Assert.Contains("Paris", indented.ToString(), StringComparison.Ordinal);
        Assert.Contains("Lovelace", person.Name, StringComparison.Ordinal);
    }
}
