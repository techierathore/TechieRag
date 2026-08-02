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
    /// <summary>How many ephemeral ports to try before giving up (REQ-UI-057).</summary>
    /// <remarks>
    /// A lost race is independent of the next attempt, so twenty-five of them make a spurious failure
    /// arithmetically impossible rather than merely unlikely. Losing every one of them would mean the
    /// machine has no free loopback port at all, which is worth failing over.
    /// </remarks>
    private const int BindAttempts = 25;

    private readonly HttpListener listener;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> shas = new(StringComparer.Ordinal);
    private readonly Task loop;

    /// <summary>Starts the host on a free loopback port.</summary>
    public FakeGitHubHost()
    {
        (listener, ApiBaseUrl) = StartOnFreeLoopbackPort();
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

    /// <summary>Stops the host and hands its port straight back.</summary>
    /// <remarks>
    /// <para>REQ-UI-057. This used to call <see cref="HttpListener.Stop"/> and THEN
    /// <see cref="HttpListener.Close"/>, which is what made the suite non-deterministic. On the
    /// managed (Unix) listener both of those unregister the prefix, and the second unregistration
    /// runs through <c>HttpEndPointManager.GetEPListener</c> — which re-BINDS the port when the
    /// endpoint is no longer in its static map. So a host that had already given its port up tried to
    /// take it back purely in order to let go of it again, and threw
    /// <c>HttpListenerException: Address already in use</c> out of teardown whenever another fixture
    /// had been handed that port in the gap. That is why the failure landed on an arbitrary test and
    /// passed on re-run.</para>
    /// <para>Closing once releases the port without ever letting go of it first, so there is no gap
    /// for anything to be handed it in.</para>
    /// </remarks>
    public void Dispose()
    {
        stopping.Cancel();
        try
        {
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

    /// <summary>
    /// Takes a loopback port the kernel reports free, and keeps trying fresh ones until one of them
    /// is still free at the moment <see cref="HttpListener"/> binds it.
    /// </summary>
    /// <remarks>
    /// <para>REQ-UI-057, the SECOND of the two windows this fixture had. The flake that actually
    /// reddened the suite was in <see cref="Dispose"/>, not here; this one has never been seen in a
    /// real run and is closed because it is the same defect wearing a different hat.</para>
    /// <para>Asking for an ephemeral port means opening a probe socket on port 0, reading what the
    /// kernel assigned and then RELEASING it, because the same port cannot be held by the probe and
    /// by the listener at once. Between the release and the bind the port is genuinely free, so the
    /// kernel may hand it to anything else that asks — and this assembly is full of other loopback
    /// fixtures asking, each with a probe of its own. The loser's <see cref="HttpListener.Start"/>
    /// throws <see cref="HttpListenerException"/> "Address already in use" (errno 48).</para>
    /// <para>The window cannot be closed, so it is made harmless instead: nothing whatsoever runs
    /// between the release and the bind, and a lost race simply starts over on a different port.
    /// Sharing an xUnit collection would not have helped either window — <see cref="FakeGitHubHost"/>
    /// has a single caller whose tests already run one at a time, so the rival is never another
    /// <see cref="FakeGitHubHost"/>; it is some other fixture's plain socket.</para>
    /// </remarks>
    /// <returns>The listener, already started, and the base URL it serves.</returns>
    /// <exception cref="HttpListenerException">Every attempt lost the race.</exception>
    private static (HttpListener Listener, string BaseUrl) StartOnFreeLoopbackPort()
    {
        for (var attempt = 1; ; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
            var candidate = new HttpListener();
            candidate.Prefixes.Add($"{baseUrl}/");

            probe.Stop();
            try
            {
                candidate.Start();
                return (candidate, baseUrl);
            }
            catch (HttpListenerException)
            {
                candidate.Close();
                if (attempt >= BindAttempts)
                {
                    throw;
                }
            }
        }
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
