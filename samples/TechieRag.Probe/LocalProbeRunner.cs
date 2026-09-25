using System.Diagnostics;
using System.Text;
using TechieRag.Local;
using TechieRag.Models;

namespace TechieRag.Probe;

/// <summary>
/// The probe's second measured action: generate one sentence from the local model and report the
/// time to first token and tokens per second (REQ-FN-059 / BRD-106).
/// </summary>
/// <remarks>
/// Uses <c>TechieRag.Local</c> exactly as an app does: the platform default model, the terms shown
/// through <see cref="LocalLlmOptions.ConfirmTermsAsync"/> before any download, the model kept loaded
/// between presses so the second press measures a warm model.
/// </remarks>
public sealed class LocalProbeRunner
{
    /// <summary>The prompt; one sentence back is a pass.</summary>
    public const string Prompt = "Write one sentence about the sea.";

    private readonly Func<LocalModelTerms, CancellationToken, Task<bool>> confirmTerms;
    private LocalLlmProvider? provider;

    /// <summary>Creates the runner.</summary>
    /// <param name="confirmTerms">Shows the model's terms to the user and returns whether they accepted.</param>
    public LocalProbeRunner(Func<LocalModelTerms, CancellationToken, Task<bool>> confirmTerms)
    {
        this.confirmTerms = confirmTerms;
    }

    /// <summary>Gets the model the local provider runs on this platform.</summary>
    public static LocalModel Model => LocalModel.PlatformDefault;

    /// <summary>Loads the model (downloading it once after terms) and generates one sentence.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The measurements.</returns>
    public async Task<GenerationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        provider ??= new LocalLlmProvider(new LocalLlmOptions { ConfirmTermsAsync = confirmTerms });
        var stopwatch = Stopwatch.StartNew();
        await provider.LoadAsync(cancellationToken);
        var loadMs = stopwatch.Elapsed.TotalMilliseconds;

        var text = new StringBuilder();
        double? firstTokenMs = null;
        TokenUsage? usage = null;
        stopwatch.Restart();
        var options = new LlmCompletionOptions { MaxTokens = 64, Temperature = 0f, StopSequences = ["\n"] };
        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User(Prompt)], options, cancellationToken))
        {
            if (streamEvent.Kind == LlmStreamEventKind.TextDelta)
            {
                firstTokenMs ??= stopwatch.Elapsed.TotalMilliseconds;
                text.Append(streamEvent.Text);
            }
            else if (streamEvent.Kind == LlmStreamEventKind.Completed)
            {
                usage = streamEvent.Usage;
            }
        }

        var totalMs = stopwatch.Elapsed.TotalMilliseconds;
        var outputTokens = usage?.OutputTokens ?? 0;
        var ttft = firstTokenMs ?? totalMs;
        var tokensPerSecond = outputTokens > 1 && totalMs > ttft ? (outputTokens - 1) / ((totalMs - ttft) / 1000d) : 0;
        return new GenerationResult(provider.ModelName, text.ToString().Trim(), loadMs, ttft, tokensPerSecond, outputTokens, PeakMemory.ReadBytes());
    }
}
