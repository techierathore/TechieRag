using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>The four chat formats the provider applies in managed code (REQ-RAG-059 / BRD-98).</summary>
public sealed class ChatTemplateFormatterTests
{
    private static readonly ChatMessage[] Conversation =
    [
        ChatMessage.System("Be brief."),
        ChatMessage.User("Hi"),
        ChatMessage.Assistant("Hello."),
        ChatMessage.User("Bye")
    ];

    /// <summary>ChatML wraps each turn in im_start / im_end and opens the assistant turn.</summary>
    [Fact]
    public void ChatMlFormatsEveryTurn() =>
        Assert.Equal(
            "<|im_start|>system\nBe brief.<|im_end|>\n<|im_start|>user\nHi<|im_end|>\n<|im_start|>assistant\nHello.<|im_end|>\n"
            + "<|im_start|>user\nBye<|im_end|>\n<|im_start|>assistant\n",
            ChatTemplateFormatter.Format(LocalChatTemplate.ChatMl, Conversation));

    /// <summary>Phi-3 uses role tags closed by end and opens the assistant tag.</summary>
    [Fact]
    public void Phi3FormatsEveryTurn() =>
        Assert.Equal(
            "<|system|>\nBe brief.<|end|>\n<|user|>\nHi<|end|>\n<|assistant|>\nHello.<|end|>\n<|user|>\nBye<|end|>\n<|assistant|>\n",
            ChatTemplateFormatter.Format(LocalChatTemplate.Phi3, Conversation));

    /// <summary>Llama 3 uses header ids and eot_id.</summary>
    [Fact]
    public void Llama3FormatsEveryTurn() =>
        Assert.Equal(
            "<|start_header_id|>system<|end_header_id|>\n\nBe brief.<|eot_id|><|start_header_id|>user<|end_header_id|>\n\nHi<|eot_id|>"
            + "<|start_header_id|>assistant<|end_header_id|>\n\nHello.<|eot_id|><|start_header_id|>user<|end_header_id|>\n\nBye<|eot_id|>"
            + "<|start_header_id|>assistant<|end_header_id|>\n\n",
            ChatTemplateFormatter.Format(LocalChatTemplate.Llama3, Conversation));

    /// <summary>Gemma has no system role: the system text is folded into the first user turn.</summary>
    [Fact]
    public void GemmaFoldsSystemIntoFirstUserTurn() =>
        Assert.Equal(
            "<start_of_turn>user\nBe brief.\n\nHi<end_of_turn>\n<start_of_turn>model\nHello.<end_of_turn>\n"
            + "<start_of_turn>user\nBye<end_of_turn>\n<start_of_turn>model\n",
            ChatTemplateFormatter.Format(LocalChatTemplate.Gemma, Conversation));

    /// <summary>Each format's end-of-turn marker is the stop sequence the provider adds.</summary>
    [Fact]
    public void EndOfTurnMarkersMatchTheFormats() =>
        Assert.Equal(
            ["<|im_end|>", "<|end|>", "<|eot_id|>", "<end_of_turn>"],
            Enum.GetValues<LocalChatTemplate>().Select(ChatTemplateFormatter.EndOfTurn));
}
