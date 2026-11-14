using TechieRag.Tests.VectorStores.Live;
using Xunit;

namespace TechieRag.Tests.Llm.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that calls the real Anthropic Messages API with the key in the
/// git-ignored <c>livetests.settings.json</c> beside the tests (<c>Anthropic:ApiKey</c>; REQ-RAG-158). Skipped with
/// the reason when the key is absent, never failed.
/// </summary>
public sealed class LiveAnthropicFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveAnthropic";

    /// <summary>Initializes a new instance of the <see cref="LiveAnthropicFactAttribute"/> class.</summary>
    public LiveAnthropicFactAttribute()
    {
        if (Key is null)
        {
            Skip = $"No Anthropic:ApiKey in {LiveTestSettings.FileName} beside the tests; add one (see "
                + "livetests.settings.example.json) to run this billed call.";
        }
    }

    /// <summary>Gets the key from the settings file, or null.</summary>
    public static string? Key => LiveTestSettings.Get("Anthropic", "ApiKey");
}
