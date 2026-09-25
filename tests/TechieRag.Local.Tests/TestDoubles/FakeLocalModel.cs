using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TechieRag.Local.Runtime;

namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>The model a <see cref="FakeLocalLlmRuntime"/> loads.</summary>
internal sealed partial class FakeLocalModel : ILocalLlmModel
{
    private readonly FakeLocalLlmRuntime runtime;

    /// <summary>Creates the model.</summary>
    /// <param name="runtime">The runtime that records calls.</param>
    public FakeLocalModel(FakeLocalLlmRuntime runtime)
    {
        this.runtime = runtime;
    }

    /// <summary>
    /// Counts like a real tokenizer would differ from four characters per token: a special marker,
    /// a word, or a punctuation mark is one token each.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The token count.</returns>
    public static int Count(string text) => TokenPattern().Count(text);

    /// <inheritdoc/>
    public int CountTokens(string text) => Count(text);

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        LocalGenerationSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        runtime.GenerateCount++;
        runtime.Prompts.Add(prompt);
        runtime.Settings.Add(settings);
        foreach (var piece in runtime.Script(prompt, settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return piece;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    [GeneratedRegex(@"<\|[^|]+\|>|\w+|[^\w\s]")]
    private static partial Regex TokenPattern();
}
