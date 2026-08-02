using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TechieDesk.Services.Auth;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// An <see cref="ISecretStore"/> that reports itself durable, standing in for Keychain / the Windows
/// Credential Manager.
/// </summary>
/// <remarks>
/// <see cref="EphemeralSecretStore"/> already covers the "no platform store" case and reports
/// <see cref="ISecretStore.IsDurable"/> false; the credential tests need the other branch too,
/// because the branch decides whether a token goes to the OS store or to the machine-bound encrypted
/// fallback. Both are asserted, so neither can rot unnoticed.
/// </remarks>
public sealed class DurableSecretStoreDouble : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsDurable => true;

    /// <summary>Gets every key currently held, for assertions about what was written where.</summary>
    public IReadOnlyCollection<string> Keys => values.Keys.ToList();

    /// <summary>Gets every value currently held, for assertions that a token really is here.</summary>
    public IReadOnlyCollection<string> Values => values.Values.ToList();

    /// <inheritdoc />
    public string? Read(string key) => values.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void Write(string key, string value) => values[key] = value;

    /// <inheritdoc />
    public bool Delete(string key) => values.TryRemove(key, out _);
}

/// <summary>
/// A canned GitHub REST API on loopback, so the whole connector path can be exercised with no
/// network and no timing.
/// </summary>
/// <remarks>
/// <para><b>A real socket, deliberately.</b> Substituting the library's transport would have skipped
/// the three things most likely to be wrong in this cluster: that the private-network opt-in reaches
/// BOTH call sites the library requires it at, that a token stored in the credential store is
/// actually attached to the request, and that the resolver's client is built at all. A fake transport
/// proves the JSON parsing the library already tests, and nothing this cluster owns.</para>
/// <para>Every response is served from a dictionary the test writes, so there is no sleeping, no
/// retrying and no polling anywhere in these tests.</para>
/// </remarks>
public sealed class FakeGitHubHost : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> shas = new(StringComparer.Ordinal);
    private readonly Task loop;

    /// <summary>Starts the host on a free loopback port.</summary>
    public FakeGitHubHost()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        ApiBaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add($"{ApiBaseUrl}/");
        listener.Start();
        loop = Task.Run(ServeAsync);
    }

    /// <summary>Gets the API base URL a connector should be pointed at.</summary>
    public string ApiBaseUrl { get; }

    /// <summary>Gets the project path this host serves.</summary>
    public string ProjectPath { get; } = "techie/handbook";

    /// <summary>Gets or sets the branch reported as the project's default.</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>Gets every <c>Authorization</c> header value received, in order.</summary>
    /// <remarks>
    /// What proves the credential travelled from the OS credential store all the way onto the wire.
    /// Asserting only that a token was stored would pass on a resolver that never read it back.
    /// </remarks>
    public ConcurrentQueue<string?> AuthorizationHeaders { get; } = new();

    /// <summary>Gets how many times the tree was listed.</summary>
    public int ListCount => listCount;

    private int listCount;

    /// <summary>Adds or replaces a file, giving it a new content hash.</summary>
    /// <param name="path">The file's repository path.</param>
    /// <param name="text">The file's text.</param>
    /// <param name="sha">The blob hash the tree reports.</param>
    /// <returns>The same host, for chaining.</returns>
    public FakeGitHubHost SetFile(string path, string text, string sha)
    {
        files[path] = text;
        shas[path] = sha;
        return this;
    }

    /// <summary>Stops the host.</summary>
    public void Dispose()
    {
        stopping.Cancel();
        try
        {
            listener.Stop();
            listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        try
        {
            loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The serve loop ends by the listener being closed underneath it; that is the stop.
        }

        stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                Respond(context);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        AuthorizationHeaders.Enqueue(context.Request.Headers["Authorization"]);
        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (path == $"/repos/{ProjectPath}")
        {
            Write(context, 200, JsonSerializer.Serialize(new { default_branch = DefaultBranch }));
            return;
        }

        if (path.StartsWith($"/repos/{ProjectPath}/git/trees/", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref listCount);
            var tree = files.Keys.OrderBy(key => key, StringComparer.Ordinal).Select(key => new
            {
                type = "blob",
                path = key,
                sha = shas[key],
                size = Encoding.UTF8.GetByteCount(files[key]),
            });
            Write(context, 200, JsonSerializer.Serialize(new { tree, truncated = false }));
            return;
        }

        if (path.StartsWith($"/repos/{ProjectPath}/git/blobs/", StringComparison.Ordinal))
        {
            var sha = path[(path.LastIndexOf('/') + 1)..];
            var file = shas.FirstOrDefault(pair => pair.Value == sha);
            if (file.Key is null)
            {
                Write(context, 404, """{"message":"Not Found"}""");
                return;
            }

            Write(context, 200, JsonSerializer.Serialize(new
            {
                encoding = "base64",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(files[file.Key])),
            }));
            return;
        }

        Write(context, 404, """{"message":"Not Found"}""");
    }

    private static void Write(HttpListenerContext context, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }
}
