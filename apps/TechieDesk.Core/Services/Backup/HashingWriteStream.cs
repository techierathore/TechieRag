using System.Security.Cryptography;

namespace TechieDesk.Services.Backup;

/// <summary>
/// A write-only pass-through stream that hashes and measures what flows through it.
/// </summary>
/// <remarks>
/// The manifest records a SHA-256 and a length for every content entry so a restore can verify
/// integrity before applying anything (REQ-FN-047d). Hashing as the bytes stream past is what keeps
/// that promise compatible with the streaming requirement — the alternative, hashing the entry after
/// writing it, would mean holding or re-reading the whole entry, and a large instance must never be
/// buffered in memory (ADR-013).
/// </remarks>
internal sealed class HashingWriteStream : Stream
{
    private readonly Stream inner;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    /// <summary>Wraps a destination stream.</summary>
    /// <param name="inner">The stream to forward writes to. Not owned; never disposed here.</param>
    internal HashingWriteStream(Stream inner) => this.inner = inner;

    /// <summary>Gets the number of bytes written so far.</summary>
    internal long BytesWritten { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => BytesWritten;

    /// <inheritdoc />
    public override long Position
    {
        get => BytesWritten;
        set => throw new NotSupportedException();
    }

    /// <summary>Completes the hash and renders it as lowercase hex.</summary>
    /// <returns>The SHA-256 of everything written, as 64 lowercase hex characters.</returns>
    internal string FinishHex() => Convert.ToHexStringLower(hash.GetHashAndReset());

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        hash.AppendData(buffer);
        BytesWritten += buffer.Length;
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
