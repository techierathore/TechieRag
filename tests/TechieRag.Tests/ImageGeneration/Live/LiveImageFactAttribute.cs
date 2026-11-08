using TechieRag.Tests.VectorStores.Live;
using Xunit;

namespace TechieRag.Tests.ImageGeneration.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that calls a real image service with a key from the git-ignored
/// <c>livetests.settings.json</c> beside the tests (REQ-RAG-155 / BRD-213); skipped with the reason when the key
/// is absent, never failed. Name the key with <see cref="KeyName"/>: <c>[LiveImageFact(KeyName = "OpenAIKey")]</c>.
/// </summary>
public sealed class LiveImageFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveImages";

    /// <summary>The settings section that holds the keys.</summary>
    public const string Section = "ImageGeneration";

    private string? keyName;

    /// <summary>Initializes a new instance of the <see cref="LiveImageFactAttribute"/> class; skipped until a key is named.</summary>
    public LiveImageFactAttribute()
    {
        Skip = "No image key is named on this test; set KeyName to the settings key it needs.";
    }

    /// <summary>Gets or sets the key's name in the <c>ImageGeneration</c> section, for example <c>OpenAIKey</c>.</summary>
    public string? KeyName
    {
        get => keyName;
        set
        {
            keyName = value;
            Skip = value is not null && Key(value) is not null
                ? null
                : $"No {Section}:{value} in {LiveTestSettings.FileName} beside the tests; add one (see "
                    + "livetests.settings.example.json) to run this billed call.";
        }
    }

    /// <summary>Reads a key from the settings file.</summary>
    /// <param name="name">The key's name in the section.</param>
    /// <returns>The key, or null.</returns>
    public static string? Key(string name) => LiveTestSettings.Get(Section, name);
}
