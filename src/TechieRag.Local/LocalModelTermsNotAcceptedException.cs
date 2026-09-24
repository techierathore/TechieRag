namespace TechieRag.Local;

/// <summary>
/// Thrown when a local model would have to download but the host has not signalled that the user
/// accepted its terms; nothing was requested from the network (REQ-RAG-062 / BRD-101).
/// </summary>
/// <remarks>
/// The host shows <see cref="Terms"/> (licence name and URL), and on acceptance sets
/// <see cref="LocalLlmOptions.TermsAccepted"/> or answers <see cref="LocalLlmOptions.ConfirmTermsAsync"/>
/// with true, then tries again.
/// </remarks>
public sealed class LocalModelTermsNotAcceptedException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="terms">The terms that were not accepted.</param>
    public LocalModelTermsNotAcceptedException(LocalModelTerms terms)
        : base($"The local model '{terms?.ModelId}' is not downloaded, and its terms ({terms?.LicenceName}, {terms?.TermsUrl}) "
               + "have not been accepted. Show them to the user, then set LocalLlmOptions.TermsAccepted or answer "
               + "LocalLlmOptions.ConfirmTermsAsync with true.")
    {
        ArgumentNullException.ThrowIfNull(terms);
        Terms = terms;
    }

    /// <summary>Gets the terms to show the user.</summary>
    public LocalModelTerms Terms { get; }
}
