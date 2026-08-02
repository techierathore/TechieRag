namespace TechieRag.Connectors;

/// <summary>
/// One item a connector run could not turn into a document (REQ-RAG-032, BRD-65).
/// </summary>
/// <remarks>
/// A first-class value rather than a log line. BRD-65 requires per-item results and per-item failure
/// reasons, and a run that reports "412 of 500 ingested" without saying which 88 failed and why is
/// indistinguishable from silent data loss.
/// </remarks>
/// <param name="ItemId">Identifier of the item that failed, so a retry can name it.</param>
/// <param name="ItemName">Human-facing name of the item — a path, a title, a subject.</param>
/// <param name="Reason">What went wrong, in terms an operator can act on. Never contains a credential.</param>
public sealed record ConnectorItemFailure(string ItemId, string ItemName, string Reason);
