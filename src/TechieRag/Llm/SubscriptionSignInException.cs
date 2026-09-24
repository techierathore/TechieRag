namespace TechieRag.Llm;

/// <summary>
/// A subscription sign-in or session failed in a way the host should show the user
/// (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// The <see cref="Code"/> is what a host branches on and renders in the user's language; the message is
/// for logs. Neither ever contains a token.
/// </remarks>
public sealed class SubscriptionSignInException : InvalidOperationException
{
    /// <summary>The user did not authorise before the code expired.</summary>
    public const string CodeExpired = "SubscriptionSignInExpired";

    /// <summary>The vendor refused the sign-in request or the code exchange.</summary>
    public const string CodeRejected = "SubscriptionSignInRejected";

    /// <summary>The vendor refused the saved session and it could not be refreshed.</summary>
    public const string CodeSessionRejected = "SubscriptionSessionRejected";

    /// <summary>The vendor's terms do not permit this subscription to be used from a third-party app.</summary>
    public const string CodeNotPermitted = "SubscriptionNotPermitted";

    /// <summary>Initializes a new instance of the <see cref="SubscriptionSignInException"/> class.</summary>
    /// <param name="code">One of the <c>Code*</c> constants.</param>
    /// <param name="message">A log message without secrets.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public SubscriptionSignInException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Gets the failure code.</summary>
    public string Code { get; }
}
