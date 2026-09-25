using System.Text.Json;

namespace TechieRag.Local.Runtime;

/// <summary>
/// What the provider reads from a model's <c>genai_config.json</c>: its context length, the memory its
/// context cache takes per token, and the files the engine will open (REQ-RAG-108).
/// </summary>
/// <remarks>
/// <para><b>Memory per token.</b> The key/value cache holds, for every token, one key and one value
/// vector per layer and per key/value head:
/// <c>num_hidden_layers × num_key_value_heads × head_size × 2 × bytes per element</c>.
/// <c>num_key_value_heads</c> defaults to <c>num_attention_heads</c> and <c>head_size</c> to
/// <c>hidden_size / num_attention_heads</c> when the file leaves them out. The element size is read from
/// <c>model.decoder.kv_cache_dtype</c> when a file carries it; ONNX Runtime GenAI 0.16.0's builder writes
/// none, and 2 bytes (16-bit) is assumed, the same figure the built-in catalogue uses (Qwen2.5 0.5B:
/// 24 × 2 × 64 × 2 × 2 = 12,288).</para>
/// </remarks>
/// <param name="ContextLength">The longest context the model supports, in tokens (<c>model.context_length</c>).</param>
/// <param name="KvBytesPerToken">The context cache's size per token, in bytes.</param>
/// <param name="FileNames">Every <c>filename</c> the configuration names, which must be in the model folder.</param>
internal sealed record GenAiConfigInfo(int ContextLength, long KvBytesPerToken, IReadOnlyList<string> FileNames)
{
    /// <summary>The configuration file's name.</summary>
    internal const string FileName = "genai_config.json";

    /// <summary>The element size assumed when the configuration names no cache type.</summary>
    internal const int DefaultKvElementBytes = 2;

    /// <summary>Reads a model folder's configuration.</summary>
    /// <param name="modelDirectory">The folder.</param>
    /// <returns>The values.</returns>
    /// <exception cref="InvalidOperationException">The file lacks a value the formula needs.</exception>
    public static GenAiConfigInfo Read(string modelDirectory) =>
        Parse(File.ReadAllText(Path.Combine(modelDirectory, FileName)));

    /// <summary>Parses a configuration's text.</summary>
    /// <param name="json">The <c>genai_config.json</c> text.</param>
    /// <returns>The values.</returns>
    /// <exception cref="InvalidOperationException">The file lacks a value the formula needs.</exception>
    public static GenAiConfigInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var model = Property(document.RootElement, "model")
            ?? throw new InvalidOperationException($"{FileName} has no 'model' section.");
        var decoder = Property(model, "decoder")
            ?? throw new InvalidOperationException($"{FileName} has no 'model.decoder' section.");

        var contextLength = Number(model, "context_length")
            ?? throw new InvalidOperationException($"{FileName} has no 'model.context_length'.");
        var layers = Number(decoder, "num_hidden_layers")
            ?? throw new InvalidOperationException($"{FileName} has no 'model.decoder.num_hidden_layers'.");
        var attentionHeads = Number(decoder, "num_attention_heads");
        var kvHeads = Number(decoder, "num_key_value_heads") ?? attentionHeads
            ?? throw new InvalidOperationException($"{FileName} has neither 'num_key_value_heads' nor 'num_attention_heads'.");
        var headSize = Number(decoder, "head_size")
            ?? (Number(decoder, "hidden_size") is { } hidden && attentionHeads is > 0 ? hidden / attentionHeads : null)
            ?? throw new InvalidOperationException($"{FileName} has neither 'head_size' nor 'hidden_size' with 'num_attention_heads'.");

        var kvBytes = (long)layers * kvHeads * headSize * 2 * ElementBytes(decoder);
        return new GenAiConfigInfo((int)contextLength, kvBytes, CollectFileNames(model));
    }

    private static int ElementBytes(JsonElement decoder)
    {
        var type = Property(decoder, "kv_cache_dtype") is { ValueKind: JsonValueKind.String } value
            ? value.GetString()?.ToLowerInvariant()
            : null;
        return type switch
        {
            "float32" or "fp32" => 4,
            "int8" or "uint8" or "fp8" => 1,
            _ => DefaultKvElementBytes
        };
    }

    private static List<string> CollectFileNames(JsonElement element)
    {
        var names = new List<string>();
        Walk(element, names);
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Walk(JsonElement element, List<string> names)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, names);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("filename") && property.Value.ValueKind == JsonValueKind.String)
            {
                names.Add(property.Value.GetString()!);
            }
            else
            {
                Walk(property.Value, names);
            }
        }
    }

    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static long? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) && number > 0
            ? number
            : null;
}
