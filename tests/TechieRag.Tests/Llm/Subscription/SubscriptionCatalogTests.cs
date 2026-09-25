using System.Reflection;
using TechieRag.Llm;
using Xunit;

namespace TechieRag.Tests.Llm.Subscription;

/// <summary>
/// Tests for the subscription rows of the connector catalog, the factory arm and routing
/// (REQ-RAG-070 / BRD-113), against the vendor research recorded in DECISIONS.md (REQ-FN-062).
/// </summary>
public class SubscriptionCatalogTests
{
    private static readonly string[] ResearchedVendors =
        ["chatgpt-subscription", "claude-subscription", "gemini-subscription", "grok-subscription", "groq-subscription", "meta-subscription"];

    private static IEnumerable<LlmConnectorDescriptor> SubscriptionRows =>
        LlmConnectorCatalog.All.Where(c => c.Source == LlmSource.Subscription);

    /// <summary>Every vendor BRD-114 names has exactly one subscription row.</summary>
    [Fact]
    public void EveryResearchedVendorHasOneRow()
    {
        var names = SubscriptionRows.Select(c => c.Name).OrderBy(n => n);

        Assert.Equal(ResearchedVendors.OrderBy(n => n), names);
    }

    /// <summary>
    /// Acceptance of REQ-RAG-070: each subscription row carries the vendor's terms as text, who they apply
    /// to, at least one source, and the date checked (2026-09-24).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-070 EveryRowCarriesTermsAndCheckDate")]
    public void EveryRowCarriesTermsAndCheckDate()
    {
        Assert.All(SubscriptionRows, row =>
        {
            Assert.NotNull(row.Subscription);
            Assert.False(string.IsNullOrWhiteSpace(row.Subscription.Terms));
            Assert.False(string.IsNullOrWhiteSpace(row.Subscription.AppliesTo));
            Assert.NotEmpty(row.Subscription.Sources);
            Assert.Equal(new DateOnly(2026, 9, 24), row.Subscription.CheckedOn);
        });
    }

    /// <summary>Acceptance of REQ-RAG-070: Anthropic's row reads not permitted.</summary>
    [Fact(DisplayName = "REQ-RAG-070 AnthropicRowReadsNotPermitted")]
    public void AnthropicRowReadsNotPermitted()
    {
        var terms = LlmConnectorCatalog.Require("claude-subscription").Subscription!;

        Assert.False(terms.Permitted);
        Assert.StartsWith("Not permitted", terms.Terms, StringComparison.Ordinal);
    }

    /// <summary>Only OpenAI's row is permitted, and it names the builder method that exists.</summary>
    [Fact]
    public void OnlyChatGptIsPermitted()
    {
        var permitted = SubscriptionRows.Where(r => r.Subscription!.Permitted).Select(r => r.Name);

        Assert.Equal(["chatgpt-subscription"], permitted);
    }

    /// <summary>
    /// A vendor with no permitted flow has no builder method: the only subscription method on the
    /// builder is the one the permitted row names, and no row that is not permitted names one.
    /// </summary>
    [Fact]
    public void BuilderMethodsMatchPermittedRows()
    {
        var builderMethods = typeof(TechieRagBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => n.Contains("Subscription", StringComparison.Ordinal))
            .Distinct();
        var namedMethods = SubscriptionRows.Select(r => r.Subscription!.BuilderMethod).OfType<string>();

        Assert.Equal(["UseChatGptSubscriptionLlm"], builderMethods);
        Assert.Equal(["UseChatGptSubscriptionLlm"], namedMethods);
    }

    /// <summary><c>ModelRouter</c> resolves an explicit subscription model to the subscription connector.</summary>
    [Fact]
    public void RouterResolvesSubscriptionModel()
    {
        var route = ModelRouter.Require("chatgpt-subscription/gpt-6-sol");

        Assert.Equal(LlmSource.Subscription, route.Source);
        Assert.Equal("gpt-6-sol", route.ModelId);
    }

    /// <summary>Naming the subscription connector alone resolves to its default model.</summary>
    [Fact]
    public void RouterUsesSubscriptionDefaultModel()
    {
        var route = ModelRouter.Require("chatgpt-subscription/");

        Assert.Equal("gpt-6-luna", route.ModelId);
    }

    /// <summary>A bare <c>gpt-</c> name still routes to the API-key OpenAI connector, never to a subscription.</summary>
    [Fact]
    public void BareGptNameStaysOnApiKeyConnector()
    {
        var route = ModelRouter.Require("gpt-4o");

        Assert.Equal("openai", route.Connector.Name);
    }

    /// <summary>The factory builds the ChatGPT provider for a subscription route with a sign-in callback.</summary>
    [Fact]
    public void FactoryCreatesChatGptProvider()
    {
        var provider = LlmProviderFactory.CreateSubscription(
            ModelRouter.Require("chatgpt-subscription/gpt-6-sol"), (_, _) => Task.CompletedTask);

        Assert.IsType<ChatGptSubscriptionLlmProvider>(provider);
        Assert.Equal("gpt-6-sol", provider.ModelName);
    }

    /// <summary>The factory refuses a vendor that is not permitted, with the not-permitted code and the terms.</summary>
    [Fact]
    public void FactoryRefusesNotPermittedVendor()
    {
        var route = ModelRouter.Require("claude-subscription/claude-sonnet-4-5");

        var failure = Assert.Throws<SubscriptionSignInException>(() => LlmProviderFactory.CreateSubscription(route, (_, _) => Task.CompletedTask));

        Assert.Equal(SubscriptionSignInException.CodeNotPermitted, failure.Code);
        Assert.Contains("Not permitted", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The API-key factory arm for a permitted subscription explains that a sign-in callback is needed.</summary>
    [Fact]
    public void ApiKeyArmPointsAtSignIn()
    {
        var route = ModelRouter.Require("chatgpt-subscription/gpt-6-luna");

        var failure = Assert.Throws<InvalidOperationException>(() => LlmProviderFactory.Create(route, apiKey: null));

        Assert.Contains("UseChatGptSubscriptionLlm", failure.Message, StringComparison.Ordinal);
    }
}
