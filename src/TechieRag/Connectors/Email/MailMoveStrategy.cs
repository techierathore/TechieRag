namespace TechieRag.Connectors.Email;

/// <summary>How a move (or a move to Trash) is carried out on this server (REQ-RAG-110 / BRD-169).</summary>
public enum MailMoveStrategy
{
    /// <summary>No move is involved: a label action, or an action that was refused before a strategy applied.</summary>
    None,

    /// <summary><c>UID MOVE</c> (RFC 6851), used when the server advertises <c>MOVE</c>.</summary>
    Move,

    /// <summary>
    /// <c>UID COPY</c>, then <c>UID STORE +FLAGS.SILENT (\Deleted)</c> and <c>UID EXPUNGE</c> of that one
    /// UID (RFC 4315), used when the server advertises <c>UIDPLUS</c> but not <c>MOVE</c>.
    /// </summary>
    CopyAndExpunge,
}
