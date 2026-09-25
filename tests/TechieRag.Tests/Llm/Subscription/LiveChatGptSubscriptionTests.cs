using TechieRag.Llm;
using TechieRag.Models;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Tests.Llm.Subscription;

/// <summary>
/// The live ChatGPT subscription sign-in against OpenAI (REQ-RAG-069), opt-in through
/// <see cref="LiveChatGptSubscriptionFactAttribute"/>.
/// </summary>
public class LiveChatGptSubscriptionTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the sign-in page and code are shown to the person running the test.</param>
    public LiveChatGptSubscriptionTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// A real device-code sign-in: the page and code are written to the test output (and to stderr so a
    /// console run shows them at once), the person approves it in a browser, and the signed-in provider
    /// answers one chat call. Device-code sign-in must be enabled in the ChatGPT account's security settings.
    /// </summary>
    [LiveChatGptSubscriptionFact]
    [Trait("Category", LiveChatGptSubscriptionFactAttribute.CategoryName)]
    public async Task LiveSignInAnswersChat()
    {
        using var provider = new ChatGptSubscriptionLlmProvider((prompt, _) =>
        {
            var text = $"Open {prompt.VerificationUri} and enter {prompt.UserCode} before {prompt.ExpiresAt:t}.";
            output.WriteLine(text);
            Console.Error.WriteLine(text);
            return Task.CompletedTask;
        });

        var response = await provider.ChatAsync([ChatMessage.User("Reply with the single word: ready")]);

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }
}
