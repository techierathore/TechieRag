namespace TechieDesk.Services.Install;

/// <summary>
/// The held data-directory lock (REQ-FN-051 clause 3). Disposing it releases the directory.
/// </summary>
/// <remarks>
/// The exclusion itself is the open <see cref="FileStream"/>, not the file's existence: the OS drops
/// the underlying lock when the handle closes, including when the process is killed or crashes. That
/// is what makes the guard self-healing and is why the sentinel file is never deleted — deleting it
/// would race a second process that is mid-acquire, for no benefit.
/// </remarks>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream stream;
    private readonly string ownerFilePath;

    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="SingleInstanceLock"/> class.</summary>
    /// <param name="stream">The exclusively opened sentinel file.</param>
    /// <param name="ownerFilePath">Path of the readable ownership record to remove on release.</param>
    internal SingleInstanceLock(FileStream stream, string ownerFilePath)
    {
        this.stream = stream;
        this.ownerFilePath = ownerFilePath;
    }

    /// <summary>Releases the data directory so another instance may take it.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            File.Delete(ownerFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover ownership record is harmless: the next launch finds its process dead and
            // reclaims. Failing a shutdown over it would be worse.
        }

        stream.Dispose();
    }
}
