using Xunit;

namespace TechieRag.Tests.Llm.Subscription;

/// <summary>
/// A <see cref="FactAttribute"/> for the live ChatGPT subscription sign-in test (REQ-RAG-069).
/// </summary>
/// <remarks>
/// A real sign-in needs a person to approve it in a browser within fifteen minutes, so it can never run
/// unattended. It is skipped with a reason unless <c>TechieRagLiveChatGptSubscription</c> is set, and it
/// carries <c>[Trait("Category", "LiveSubscription")]</c> for filtering. No token is ever written to the
/// repository: the test keeps the session in memory only.
/// </remarks>
public sealed class LiveChatGptSubscriptionFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveSubscription";

    /// <summary>Environment variable that enables the live test.</summary>
    public const string OptInVariable = "TechieRagLiveChatGptSubscription";

    /// <summary>Initializes a new instance of the <see cref="LiveChatGptSubscriptionFactAttribute"/> class.</summary>
    public LiveChatGptSubscriptionFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Live ChatGPT subscription sign-in needs a person to approve it in a browser. Set {OptInVariable}=1 to run it.";
        }
    }

    /// <summary>Gets a value indicating whether the live test was opted into.</summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(OptInVariable) is { Length: > 0 } value
        && !value.Equals("0", StringComparison.Ordinal)
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
