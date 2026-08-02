using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;

namespace TechieRag.Connectors.Email;

/// <summary>
/// Ingests messages, and optionally attachment text, from a mailbox (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Scope first.</b> Folder selection, the date and sender and subject filters, the
/// sent-mail exclusion and the spam exclusion are applied while listing, so a message outside scope
/// is never downloaded — not merely never ingested. On the most sensitive source in the product,
/// "we fetched it and then discarded it" is not the same promise as "we never fetched it".</para>
/// <para><b>Incremental sync leans on immutability.</b> A message never changes, so its UID is its
/// version and an item already seen is provably already ingested. Between runs the connector asks
/// the server only for mail since the previous run — with a day of overlap, because IMAP's date
/// search has day granularity and clocks disagree — and the runner's version check discards the
/// overlap. Because the listing is therefore not a statement about the whole mailbox, this connector
/// reports <see cref="ListsEntireSource"/> as false so that sync state is never pruned against
/// it.</para>
/// <para><b>What that costs.</b> Sync state grows by one entry per message ever ingested and is
/// never pruned. On a large mailbox that is megabytes of caller-persisted state — the honest price
/// of an incremental listing, and much cheaper than the alternative of re-listing the mailbox in
/// full on every run.</para>
/// <para><b>Attachment text never touches disk.</b> Decoded bytes go straight to an
/// <see cref="IDocumentProcessor"/> as a stream. A connector that wrote a confidential attachment to
/// a temporary file so it could be parsed would leave it there for anything on the machine to
/// read.</para>
/// <para><b>One instance drives one run.</b> Listing remembers the headers it produced so that
/// fetching can reach the server with them, so cursors are only meaningful to the connector that
/// issued them.</para>
/// </remarks>
public sealed class EmailConnector : IDataConnector
{
    private readonly IMailTransport transport;
    private readonly EmailConnectorOptions options;
    private readonly IReadOnlyList<IDocumentProcessor> attachmentProcessors;
    private readonly ILogger<EmailConnector> logger;
    private readonly Dictionary<string, MailHeader> listed = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? folders;

    /// <summary>Initializes a new instance of the <see cref="EmailConnector"/> class.</summary>
    /// <param name="transport">Mail access seam.</param>
    /// <param name="options">What to ingest. Its defaults are the narrow ones; see that type.</param>
    /// <param name="attachmentProcessors">Processors used to read attachment text. Only consulted when <see cref="EmailConnectorOptions.IncludeAttachments"/> is set.</param>
    /// <param name="logger">Diagnostics. Receives subjects and folder names, never message bodies or credentials.</param>
    public EmailConnector(
        IMailTransport transport,
        EmailConnectorOptions options,
        IEnumerable<IDocumentProcessor>? attachmentProcessors = null,
        ILogger<EmailConnector>? logger = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.attachmentProcessors = attachmentProcessors is null ? [] : [.. attachmentProcessors];
        this.logger = logger ?? NullLogger<EmailConnector>.Instance;
    }

    /// <inheritdoc />
    public string SourceType => "email";

    /// <inheritdoc />
    public string SourceName => transport.MailboxName;

    /// <inheritdoc />
    /// <remarks>
    /// Always false. An incremental run asks the server for recent mail only, so the items this
    /// connector lists are never the whole mailbox and pruning against them would discard the sync
    /// state that made the run incremental.
    /// </remarks>
    public bool ListsEntireSource => false;

    /// <inheritdoc />
    public async Task<ConnectorPage> ListAsync(
        ConnectorListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selected = await ResolveFoldersAsync(cancellationToken).ConfigureAwait(false);
        if (selected.Count == 0)
        {
            return ConnectorPage.Empty;
        }

        var (folderIndex, skip) = ParseCursor(request.Cursor);
        if (folderIndex >= selected.Count)
        {
            return ConnectorPage.Empty;
        }

        var folder = selected[folderIndex];
        var page = await transport
            .SearchAsync(folder, BuildCriteria(request.PreviousSync), skip, options.PageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<ConnectorItem>(page.Headers.Count);
        foreach (var header in page.Headers)
        {
            if (!options.IncludeSentByMe && IsSentByAccount(header))
            {
                continue;
            }

            var item = ToItem(header);
            listed[item.Id] = header;
            items.Add(item);
        }

        logger.LogDebug(
            "{Source} listed {Count} message(s) from {Folder}", SourceName, items.Count, folder);

        return new ConnectorPage(items, NextCursor(selected, folderIndex, skip, page));
    }

    /// <inheritdoc />
    public async Task<ConnectorDocument> FetchAsync(
        ConnectorItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!listed.TryGetValue(item.Id, out var header))
        {
            throw new InvalidOperationException(
                $"'{item.Name}' was not listed by this connector instance, so the mailbox cannot be asked for it.");
        }

        var raw = await transport.FetchAsync(header, cancellationToken).ConfigureAwait(false);
        var message = MimeParser.Parse(raw);

        var body = ReplyTrimmer.Trim(message.Body, options.StripQuotedReplies, options.StripSignatures);
        var text = new StringBuilder();

        // The envelope is part of the document, not just metadata. Someone searching a mailbox asks
        // "what did legal say about the renewal" — the sender and the subject are the query terms,
        // and a chunk that holds only the body cannot match them.
        Append(text, "Subject", message.Subject.Length > 0 ? message.Subject : item.Name);
        Append(text, "From", message.From);
        Append(text, "To", message.To);
        Append(text, "Date", (message.Date ?? header.Date)?.ToString("u", CultureInfo.InvariantCulture) ?? string.Empty);
        Append(text, "Folder", header.Folder);
        text.Append('\n').Append(body);

        var skippedAttachments = new List<string>();

        // BRD-65: every skip carries a reason an operator can act on, and a message truncated by the
        // parser's own bound is a skip like any other rather than a silently shorter document.
        if (message.Attachments.Count >= MimeParser.MaxAttachments)
        {
            skippedAttachments.Add(
                $"attachment list truncated at {MimeParser.MaxAttachments} (the message declared more parts than are read)");
        }

        if (options.IncludeAttachments)
        {
            await AppendAttachmentsAsync(text, message, skippedAttachments, cancellationToken).ConfigureAwait(false);
        }

        return new ConnectorDocument(WithAttachmentNotes(item, message, skippedAttachments), text.ToString().Trim());
    }

    private async Task AppendAttachmentsAsync(
        StringBuilder text,
        ParsedMailMessage message,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in message.Attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
            if (extension.Length == 0 || !options.AttachmentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                skipped.Add($"{attachment.FileName} (not an included type)");
                continue;
            }

            if (attachment.Content.LongLength > options.MaxAttachmentBytes)
            {
                skipped.Add($"{attachment.FileName} (larger than the attachment limit)");
                continue;
            }

            var processor = attachmentProcessors.FirstOrDefault(
                p => p.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

            if (processor is null)
            {
                skipped.Add($"{attachment.FileName} (no processor for {extension})");
                continue;
            }

            try
            {
                using var stream = new MemoryStream(attachment.Content, writable: false);
                var chunks = await processor
                    .ProcessAsync(stream, attachment.FileName, null, cancellationToken)
                    .ConfigureAwait(false);

                var content = string.Join("\n", chunks.Select(c => c.Text));
                if (string.IsNullOrWhiteSpace(content))
                {
                    skipped.Add($"{attachment.FileName} (no readable text)");
                    continue;
                }

                text.Append("\n\n--- Attachment: ").Append(attachment.FileName).Append(" ---\n").Append(content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A corrupt attachment must not cost the message it arrived on. The message body is
                // usually the part that mattered anyway.
                logger.LogWarning(ex, "Attachment {File} could not be read", attachment.FileName);
                skipped.Add($"{attachment.FileName} ({ex.Message})");
            }
        }
    }

    private static ConnectorItem WithAttachmentNotes(
        ConnectorItem item,
        ParsedMailMessage message,
        List<string> skipped)
    {
        if (message.Attachments.Count == 0 && skipped.Count == 0)
        {
            return item;
        }

        var metadata = item.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(item.Metadata, StringComparer.Ordinal);

        metadata["AttachmentCount"] = message.Attachments.Count.ToString(CultureInfo.InvariantCulture);

        // Attachments that were not read are recorded on the item rather than written into the
        // document's text: a note about a skipped file inside the indexed body would be retrievable
        // as if it were content.
        if (skipped.Count > 0)
        {
            metadata["AttachmentsSkipped"] = string.Join("; ", skipped);
        }

        return item with { Metadata = metadata };
    }

    private ConnectorItem ToItem(MailHeader header)
    {
        var subject = string.IsNullOrWhiteSpace(header.Subject) ? "(no subject)" : header.Subject;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Folder"] = header.Folder,
            ["From"] = header.From,
            ["To"] = header.To,
            ["Subject"] = subject,
        };

        if (header.MessageId is not null)
        {
            metadata["MessageId"] = header.MessageId;
        }

        return new ConnectorItem(
            // UIDVALIDITY is part of the identity, not decoration. When a server resets it every UID
            // in the folder refers to a different message, and an id built from the UID alone would
            // make the whole folder look unchanged.
            $"{header.Folder}/{header.UidValidity}/{header.Uid}",
            subject,
            $"imap://{transport.MailboxName}/{Uri.EscapeDataString(header.Folder)};UID={header.Uid}",
            header.Uid,
            header.Date,
            header.SizeBytes,
            metadata);
    }

    private MailSearchCriteria BuildCriteria(ConnectorSyncState? previousSync)
    {
        var since = options.SinceUtc;

        if (previousSync?.LastRunUtc is { } lastRun)
        {
            // One day of overlap, deliberately. IMAP's SINCE compares dates, not instants, and the
            // server's idea of today may differ from ours by a timezone. Asking from yesterday costs
            // one day of headers; asking from today loses every message that arrived after the
            // previous run started, permanently.
            var incremental = lastRun.AddDays(-1);
            since = since is null || incremental > since ? incremental : since;
        }

        return new MailSearchCriteria(since, options.SenderContains, options.SubjectContains);
    }

    private async Task<IReadOnlyList<string>> ResolveFoldersAsync(CancellationToken cancellationToken)
    {
        if (folders is not null)
        {
            return folders;
        }

        IReadOnlyList<string> requested = options.Folders.Count > 0
            ? [.. options.Folders]
            : await transport.ListFoldersAsync(cancellationToken).ConfigureAwait(false);

        var selected = new List<string>();
        foreach (var folder in requested)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            // The spam exclusion applies to explicitly named folders too. Someone who names a junk
            // folder and leaves IncludeSpam off has contradicted themselves, and the safe reading of
            // the contradiction is the one that keeps adversarial text out of the index.
            if (!options.IncludeSpam && EmailConnectorOptions.IsSpamFolder(folder))
            {
                logger.LogInformation("Skipping junk folder {Folder}; set IncludeSpam to ingest it", folder);
                continue;
            }

            selected.Add(folder);
        }

        folders = selected;
        return folders;
    }

    private bool IsSentByAccount(MailHeader header) =>
        !string.IsNullOrWhiteSpace(options.AccountAddress)
        && header.From.Contains(options.AccountAddress, StringComparison.OrdinalIgnoreCase);

    private static string? NextCursor(
        IReadOnlyList<string> selected,
        int folderIndex,
        int skip,
        MailSearchPage page)
    {
        // Advance by what the server returned, not by what survived filtering: skipping by the
        // filtered count would re-request the messages that were filtered out, forever.
        if (page.HasMore)
        {
            return $"{folderIndex}:{skip + page.Headers.Count}";
        }

        return folderIndex + 1 < selected.Count ? $"{folderIndex + 1}:0" : null;
    }

    private static (int FolderIndex, int Skip) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (0, 0);
        }

        var parts = cursor.Split(':');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var folder)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip)
            ? (folder, skip)
            : (0, 0);
    }

    private static void Append(StringBuilder text, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(label).Append(": ").Append(value.Trim()).Append('\n');
        }
    }
}
