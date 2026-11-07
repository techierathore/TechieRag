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
    /// <summary>Records the pictures and the prompt, then plays the script (REQ-RAG-147).</summary>
    public IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        IReadOnlyList<LocalImage> images,
        int promptTokens,
        LocalGenerationSettings settings,
        CancellationToken cancellationToken)
    {
        runtime.Images.Add(images);
        if (runtime.ImagePromptTooLong is { } tokens)
        {
            return Refuse(tokens);
        }

        return GenerateAsync(prompt, settings, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Refuse(int tokens)
    {
        await Task.Yield();
        throw new LocalImagePromptTooLongException(tokens);
#pragma warning disable CS0162 // An iterator needs a yield; the throw above always runs first.
        yield break;
#pragma warning restore CS0162
    }

    public void Dispose()
    {
    }

    [GeneratedRegex(@"<\|[^|]+\|>|\w+|[^\w\s]")]
    private static partial Regex TokenPattern();
}
