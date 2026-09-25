using System.Runtime.CompilerServices;
using TechieRag.Local.Runtime;

namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>
/// Wraps a real <see cref="ILocalLlmRuntime"/> and counts the generations that reach it, so the
/// conformance suite can assert "no inference" against a real engine.
/// </summary>
internal sealed class CountingLocalLlmRuntime : ILocalLlmRuntime
{
    private readonly ILocalLlmRuntime inner;
    private int generateCount;

    /// <summary>Wraps a runtime.</summary>
    /// <param name="inner">The real runtime.</param>
    public CountingLocalLlmRuntime(ILocalLlmRuntime inner)
    {
        this.inner = inner;
    }

    /// <summary>Gets how many generations started.</summary>
    public int GenerateCount => Volatile.Read(ref generateCount);

    /// <inheritdoc/>
    public string Name => inner.Name;

    /// <inheritdoc/>
    public LocalModelFormat Format => inner.Format;

    /// <inheritdoc/>
    public ILocalTokenizer LoadTokenizer(string modelDirectory) => inner.LoadTokenizer(modelDirectory);

    /// <inheritdoc/>
    public ILocalLlmModel Load(string modelDirectory, int contextSize, int? threads) =>
        new CountingModel(inner.Load(modelDirectory, contextSize, threads), this);

    private sealed class CountingModel : ILocalLlmModel
    {
        private readonly ILocalLlmModel model;
        private readonly CountingLocalLlmRuntime owner;

        public CountingModel(ILocalLlmModel model, CountingLocalLlmRuntime owner)
        {
            this.model = model;
            this.owner = owner;
        }

        public int CountTokens(string text) => model.CountTokens(text);

        public async IAsyncEnumerable<string> GenerateAsync(
            string prompt,
            LocalGenerationSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner.generateCount);
            await foreach (var piece in model.GenerateAsync(prompt, settings, cancellationToken).ConfigureAwait(false))
            {
                yield return piece;
            }
        }

        public void Dispose() => model.Dispose();
    }
}
