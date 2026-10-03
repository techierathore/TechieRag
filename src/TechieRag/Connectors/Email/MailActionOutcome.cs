namespace TechieRag.Connectors.Email;

/// <summary>How one mail action ended, or would end in a dry run (REQ-RAG-110 / BRD-169).</summary>
public enum MailActionOutcome
{
    /// <summary>The action was carried out (in a dry run: it would be).</summary>
    Done,

    /// <summary>Nothing needed doing, for example the message is already in the target folder. <see cref="MailActionResult.Code"/> says why.</summary>
    Skipped,

    /// <summary>The action could not be carried out. <see cref="MailActionResult.Code"/> and <see cref="MailActionResult.Reason"/> say why.</summary>
    Failed,
}
