using System.Globalization;
using System.Text.Json;

namespace TechieRag.Llm;

/// <summary>
/// Reads what one entry of a service's model list says about price, data handling and reasoning
/// (REQ-RAG-166, Lekhak TR-RAG-011).
/// </summary>
/// <remarks>
/// <para>Recognises two published shapes, by their fields rather than by the service's name:</para>
/// <list type="bullet">
/// <item><description>Venice (https://api.venice.ai/api/v1/models, read 2026-11-19): <c>model_spec.pricing.input.usd</c>
/// and <c>model_spec.pricing.output.usd</c> in US dollars per million tokens; <c>model_spec.privacy</c>,
/// <c>"private"</c> (Venice runs the model) or <c>"anonymized"</c> (Venice passes the text to the model's own
/// company); <c>model_spec.capabilities.supportsReasoning</c>.</description></item>
/// <item><description>OpenRouter (https://openrouter.ai/api/v1/models, read 2026-11-19): <c>pricing.prompt</c> and
/// <c>pricing.completion</c> as decimal text in US dollars per token, a negative value meaning "varies"; and
/// <c>supported_parameters</c>, which names <c>reasoning</c> for a model that reasons.</description></item>
/// </list>
/// <para>Anything else is left empty. A value that is there but cannot be read is left empty too, never guessed.</para>
/// </remarks>
internal static class ModelDetailsReader
{
    private const decimal TokensPerMillion = 1_000_000m;

    /// <summary>Reads one list entry.</summary>
    /// <param name="id">The model id already read from the entry.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="local">Whether the connector is a local runtime (LM Studio, Ollama).</param>
    /// <returns>The model's details.</returns>
    public static LlmModelDetails Read(string id, JsonElement entry, bool local)
    {
        decimal? input = null;
        decimal? output = null;
        bool? reasoning = null;
        var handling = local ? LlmModelDataHandling.Local : LlmModelDataHandling.NotKnown;

        if (TryObject(entry, "model_spec", out var spec))
        {
            if (TryObject(spec, "pricing", out var pricing))
            {
                input = UsdOf(pricing, "input");
                output = UsdOf(pricing, "output");
            }

            if (spec.TryGetProperty("privacy", out var privacy) && privacy.ValueKind == JsonValueKind.String)
            {
                handling = privacy.GetString() switch
                {
                    "private" => LlmModelDataHandling.RunsItself,
                    "anonymized" => LlmModelDataHandling.PassesOn,
                    _ => handling
                };
            }

            if (TryObject(spec, "capabilities", out var capabilities)
                && capabilities.TryGetProperty("supportsReasoning", out var supports)
                && supports.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                reasoning = supports.GetBoolean();
            }
        }

        if (input is null && output is null && TryObject(entry, "pricing", out var perToken))
        {
            input = PerTokenToPerMillion(perToken, "prompt");
            output = PerTokenToPerMillion(perToken, "completion");
        }

        if (reasoning is null
            && entry.TryGetProperty("supported_parameters", out var parameters)
            && parameters.ValueKind == JsonValueKind.Array)
        {
            reasoning = parameters.EnumerateArray()
                .Any(parameter => parameter.ValueKind == JsonValueKind.String && parameter.GetString() == "reasoning");
        }

        return new LlmModelDetails
        {
            Id = id,
            InputPricePerMillionUsd = input,
            OutputPricePerMillionUsd = output,
            DataHandling = handling,
            SupportsReasoning = reasoning
        };
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement child)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out child)
            && child.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        child = default;
        return false;
    }

    /// <summary>Reads Venice's <c>{"usd": 2.5, …}</c> price, already per million tokens.</summary>
    private static decimal? UsdOf(JsonElement pricing, string name) =>
        TryObject(pricing, name, out var price) && price.TryGetProperty("usd", out var usd) ? NonNegative(usd) : null;

    /// <summary>Reads OpenRouter's per-token price text and scales it to a million tokens.</summary>
    private static decimal? PerTokenToPerMillion(JsonElement pricing, string name) =>
        pricing.TryGetProperty(name, out var value) && NonNegative(value) is { } perToken ? perToken * TokensPerMillion : null;

    /// <summary>Reads a number or decimal text; a negative value ("varies") or anything unreadable is null.</summary>
    private static decimal? NonNegative(JsonElement value)
    {
        decimal number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number))
        {
            return number >= 0 ? number : null;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && number >= 0
                ? number
                : null;
    }
}
