using System.Text.Json;
using TechieRag.Local.Runtime;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// REQ-RAG-108: what an arbitrary Hugging Face model needs besides its files — the conversation handed
/// to its own chat template, SentencePiece space markers cleaned from decoded text, and its memory
/// needs derived from its genai_config.json.
/// </summary>
public sealed class ModelDefinedTemplateTests
{
    /// <summary>
    /// The messages for a model's own template: every system message joined into one first message, a
    /// tool result sent as a user turn, and consecutive turns of one role joined so they alternate.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 MessagesForOwnTemplateAlternate")]
    public void MessagesForOwnTemplateAlternate()
    {
        ChatMessage[] conversation =
        [
            ChatMessage.User("Hi"),
            ChatMessage.System("Be brief."),
            new ChatMessage { Role = "tool", Content = "42" },
            ChatMessage.Assistant("Hello."),
            ChatMessage.User("Bye")
        ];

        var json = ChatTemplateFormatter.ToMessagesJson(conversation);

        var turns = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json)!
            .Select(t => (t["role"], t["content"]));
        Assert.Equal(
            [("system", "Be brief."), ("user", "Hi\n\n42"), ("assistant", "Hello."), ("user", "Bye")],
            turns);
    }

    /// <summary>A model's own template adds no stop sequence: its end-of-turn token is an end token of the engine.</summary>
    [Fact]
    public void OwnTemplateHasNoEndOfTurnText() =>
        Assert.Null(ChatTemplateFormatter.EndOfTurn(LocalChatTemplate.ModelDefined));

    /// <summary>
    /// Gemma 3's tokens for runs of spaces decode to U+2581 markers in ONNX Runtime GenAI 0.16.0; every
    /// marker becomes a space, and text without one is returned unchanged.
    /// </summary>
    /// <param name="piece">The decoded piece.</param>
    /// <param name="expected">The cleaned piece.</param>
    [Theory(DisplayName = "REQ-RAG-108 SentencePieceMarkersBecomeSpaces")]
    [InlineData("▁▁", "  ")]
    [InlineData("▁▁▁▁\"city\"", "    \"city\"")]
    [InlineData("Hello▁▁▁world", "Hello   world")]
    [InlineData(" leading", " leading")]
    [InlineData("", "")]
    public void SentencePieceMarkersBecomeSpaces(string piece, string expected) =>
        Assert.Equal(expected, DecodedText.Clean(piece));

    /// <summary>
    /// The cache size per token is layers × key/value heads × head size × 2 × 2 bytes: Arm's Gemma 3 1B
    /// config gives 26 × 1 × 256 × 2 × 2 = 26,624 bytes and a 4,096-token context, and the only file it
    /// names is model.onnx.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-108 KvBytesDerivedFromGenAiConfig")]
    public void KvBytesDerivedFromGenAiConfig()
    {
        var config = GenAiConfigInfo.Parse(TestDoubles.FakeHuggingFaceRepository.GemmaGenAiConfig);

        Assert.Equal((4_096, 26_624L), (config.ContextLength, config.KvBytesPerToken));
        Assert.Equal(["model.onnx"], config.FileNames);
    }

    /// <summary>
    /// Without head_size or num_key_value_heads the formula falls back to hidden_size / attention heads
    /// and to the attention heads; a 32-bit cache type doubles the element size. Qwen2.5 0.5B's shape
    /// (24 layers, 2 key/value heads, head size 64) gives the catalogue's 12,288 at 2 bytes.
    /// </summary>
    [Fact]
    public void KvBytesFallBackAndHonourCacheType()
    {
        const string qwen = """{"model":{"context_length":32768,"decoder":{"num_hidden_layers":24,"num_key_value_heads":2,"head_size":64,"filename":"model.onnx"}}}""";
        const string noHeadSize = """{"model":{"context_length":2048,"decoder":{"num_hidden_layers":2,"num_attention_heads":4,"hidden_size":256,"kv_cache_dtype":"float32"}}}""";

        var fromQwen = GenAiConfigInfo.Parse(qwen).KvBytesPerToken;
        var fromFallback = GenAiConfigInfo.Parse(noHeadSize).KvBytesPerToken;

        Assert.Equal((12_288L, 2L * 4 * 64 * 2 * 4), (fromQwen, fromFallback));
    }
}
