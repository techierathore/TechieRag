using TechieRag.Local.Runtime;

namespace TechieRag.Local.Tests.TestDoubles;

/// <summary>
/// A scripted <see cref="ILocalLlmRuntime"/>: a word-and-marker tokenizer and a generator that plays
/// back pieces chosen by <see cref="Script"/>, recording every prompt and setting it was given.
/// </summary>
internal sealed class FakeLocalLlmRuntime : ILocalLlmRuntime
{
    /// <summary>Gets or sets what the model "generates" for a prompt and settings, one piece per token.</summary>
    public Func<string, LocalGenerationSettings, IEnumerable<string>> Script { get; set; } =
        (_, _) => ["Hello", " there", "."];

    /// <summary>Gets the templated prompts the runtime received, in order.</summary>
    public List<string> Prompts { get; } = [];

    /// <summary>Gets the generation settings the runtime received, in order.</summary>
    public List<LocalGenerationSettings> Settings { get; } = [];

    /// <summary>Gets how many times a model was loaded.</summary>
    public int LoadCount { get; private set; }

    /// <summary>Gets how many generations started.</summary>
    public int GenerateCount { get; set; }

    /// <summary>Gets the context size of the last load.</summary>
    public int LastContextSize { get; private set; }

    /// <inheritdoc/>
    public string Name => "fake";

    /// <inheritdoc/>
    public LocalModelFormat Format => LocalModelFormat.Test;

    /// <inheritdoc/>
    public ILocalTokenizer LoadTokenizer(string modelDirectory) => new FakeLocalModel(this);

    /// <inheritdoc/>
    public ILocalLlmModel Load(string modelDirectory, int contextSize, int? threads)
    {
        LoadCount++;
        LastContextSize = contextSize;
        return new FakeLocalModel(this);
    }
}
