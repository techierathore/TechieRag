using Microsoft.Extensions.AI;
using TechieRag.Models;
using CoreChatMessage = TechieRag.Models.ChatMessage;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Interop;

/// <summary>Maps an MEAI request (messages plus <see cref="ChatOptions"/>) to a core request (REQ-RAG-017).</summary>
internal static class ChatOptionsMapper
{
    /// <summary>Converts messages and options.</summary>
    /// <param name="messages">The MEAI messages.</param>
    /// <param name="options">The MEAI options, or null.</param>
    /// <returns>The core messages (instructions prepended as a system message) and options.</returns>
    public static (List<CoreChatMessage> Messages, LlmCompletionOptions Options) ToCore(IEnumerable<MeaiChatMessage> messages, ChatOptions? options)
    {
        var coreMessages = ChatMessageMapper.ToCore(messages);

        // Every built-in provider reads the system prompt from a system message on the chat path, so
        // ChatOptions.Instructions (which is where Agent Framework puts the agent's instructions)
        // becomes one, merged into an existing leading system message rather than a second one.
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            PrependInstructions(coreMessages, options.Instructions);
        }

        var tools = options?.Tools?.OfType<AIFunctionDeclaration>().Select(ToDefinition).ToList();
        var coreOptions = new LlmCompletionOptions
        {
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxOutputTokens,
            TopP = options?.TopP,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
            StopSequences = options?.StopSequences?.ToList(),
            Seed = options?.Seed is { } seed ? (int)seed : null,
            Model = options?.ModelId,
            JsonMode = options?.ResponseFormat is ChatResponseFormatJson,
            Tools = tools is { Count: > 0 } ? tools : null,
            ToolChoice = tools is { Count: > 0 } ? ToToolChoice(options?.ToolMode) : null
        };

        return (coreMessages, coreOptions);
    }

    /// <summary>Builds a core tool definition from an MEAI function declaration, carrying the raw JSON schema.</summary>
    /// <param name="function">The declaration.</param>
    /// <returns>The definition.</returns>
    public static ToolDefinition ToDefinition(AIFunctionDeclaration function) => new()
    {
        Name = function.Name,
        Description = function.Description ?? string.Empty,
        ParametersSchema = function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Object
            ? function.JsonSchema.GetRawText()
            : """{"type":"object","properties":{}}""",
        RequiresConfirmation = function.GetService(typeof(ApprovalRequiredAIFunction)) is not null
    };

    private static void PrependInstructions(List<CoreChatMessage> messages, string instructions)
    {
        if (messages.Count > 0 && messages[0].Role == "system")
        {
            messages[0].Content = instructions + "\n\n" + messages[0].Content;
            return;
        }

        messages.Insert(0, CoreChatMessage.System(instructions));
    }

    private static string? ToToolChoice(ChatToolMode? mode) => mode switch
    {
        NoneChatToolMode => "none",
        RequiredChatToolMode => "required",
        _ => "auto"
    };
}
