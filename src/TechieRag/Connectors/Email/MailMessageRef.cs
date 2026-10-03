using System.Globalization;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Which message a mail action acts on: a folder and the message's IMAP UID in it
/// (REQ-RAG-110 / BRD-169).
/// </summary>
/// <remarks>
/// <para><b>The same coordinates the reader reports.</b> <see cref="MailHeader"/> from
/// <see cref="ImapMailTransport"/> carries exactly these three values, and the email connector's item
/// id is <c>folder/uidvalidity/uid</c>, so a message found by reading can be acted on without a
/// second lookup: <see cref="FromHeader"/> or <see cref="FromConnectorItemId"/>.</para>
/// <para><b>Why <see cref="UidValidity"/> is checked.</b> A UID only names a message while the
/// folder's UIDVALIDITY holds. When the server has changed it, the same number may name a different
/// message, and moving that one to Trash would act on mail nobody chose. A value is refused with
/// <see cref="MailActionCodes.UidValidityChanged"/> rather than guessed at; leave it null (or
/// <c>"0"</c>) to skip the check.</para>
/// </remarks>
/// <param name="Folder">The folder the message is in, spelled as the server lists it.</param>
/// <param name="Uid">The message's IMAP UID in that folder.</param>
/// <param name="UidValidity">The folder's UIDVALIDITY when the UID was read, or null to skip the check.</param>
public sealed record MailMessageRef(string Folder, string Uid, string? UidValidity = null)
{
    /// <summary>Creates a reference from a header the IMAP transport returned.</summary>
    /// <param name="header">The header.</param>
    /// <returns>A reference to the same message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    public static MailMessageRef FromHeader(MailHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new MailMessageRef(header.Folder, header.Uid, header.UidValidity);
    }

    /// <summary>Creates a reference from an email connector item id (<c>folder/uidvalidity/uid</c>).</summary>
    /// <param name="itemId">The id from <c>ConnectorItem.Id</c> of an IMAP-backed <see cref="EmailConnector"/>.</param>
    /// <returns>A reference to the same message.</returns>
    /// <exception cref="ArgumentException">The id is not in the IMAP connector's form.</exception>
    /// <remarks>The folder may itself contain <c>/</c>, so the id is split from the right.</remarks>
    public static MailMessageRef FromConnectorItemId(string itemId)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        var last = itemId.LastIndexOf('/');
        var middle = last > 0 ? itemId.LastIndexOf('/', last - 1) : -1;
        if (middle <= 0
            || !long.TryParse(itemId[(middle + 1)..last], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            || !long.TryParse(itemId[(last + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("The id is not an IMAP email connector item id (folder/uidvalidity/uid).", nameof(itemId));
        }

        return new MailMessageRef(itemId[..middle], itemId[(last + 1)..], itemId[(middle + 1)..last]);
    }
}
