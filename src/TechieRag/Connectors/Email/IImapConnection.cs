namespace TechieRag.Connectors.Email;

/// <summary>
/// A byte pipe to an IMAP server (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>The seam is below the protocol, not above it.</b> Faking <see cref="IMailTransport"/>
/// proves the connector; it proves nothing about the IMAP conversation itself — and that
/// conversation is where the genuinely error-prone code lives: literal framing, tagged responses,
/// UID parsing, folder selection. Putting the seam at the socket means all of that is driven by a
/// scripted fake connection in tests, with no server and no account.</para>
/// <para><b>Lines and byte counts, both.</b> IMAP interleaves text lines with counted binary
/// literals — <c>{2048}</c> followed by exactly 2048 bytes that may contain anything, newlines
/// included. A reader that only offers lines will read into the middle of a message body and lose
/// the frame, which is why <see cref="ReadExactAsync"/> exists alongside
/// <see cref="ReadLineAsync"/> and why a <see cref="System.IO.StreamReader"/> cannot be used here.</para>
/// </remarks>
public interface IImapConnection : IDisposable
{
    /// <summary>Gets a value indicating whether the connection is encrypted.</summary>
    /// <remarks>Checked before credentials are sent. False means the connection is refused, not warned about.</remarks>
    bool IsSecure { get; }

    /// <summary>Opens the connection and reads the server greeting.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The greeting line.</returns>
    /// <exception cref="ConnectorException">The server could not be reached, or TLS could not be established.</exception>
    Task<string> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one command line, appending the protocol's line terminator.</summary>
    /// <param name="line">The command, without a trailing newline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the line has been written.</returns>
    Task WriteLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>Reads one line of response.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The line without its terminator, or null when the server closed the connection.</returns>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads exactly the requested number of bytes.</summary>
    /// <param name="count">How many bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes read.</returns>
    /// <exception cref="ConnectorException">The connection ended before the count was satisfied.</exception>
    Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken = default);
}
