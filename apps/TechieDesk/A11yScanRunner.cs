#if DEBUG
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace TechieDesk;

/// <summary>
/// Development-only accessibility sweep: runs axe-core inside the live <c>BlazorWebView</c> and
/// writes the violations of every shipped route to a JSON file (REQ-NFR-005, BRD-96).
/// </summary>
/// <remarks>
/// <para>
/// This exists because REQ-FN-035 turned TechieDesk from a Blazor Server head into a MAUI Blazor
/// Hybrid one. The 2026-07-20 accessibility evidence came from
/// <c>tests/verify/req-nfr-005-007-a11y-browsers.spec.ts</c>, which drives Playwright at
/// <c>http://localhost:5131</c>. There is no longer a Kestrel listener, no <c>/_framework</c> to
/// fetch and no CDP endpoint, so that harness cannot reach this app at all — and the Appium
/// <c>mac2</c> driver exposes only the native XCUIElement tree, never the web view's DOM or a
/// JavaScript context. Neither route can run axe-core.
/// </para>
/// <para>
/// So the scan is brought INSIDE the process: the same axe-core build the Playwright harness uses
/// (<c>node_modules/axe-core/axe.min.js</c>, 4.12.1) is evaluated in the web view, then
/// <c>axe.run</c> is executed against the real, laid-out, styled document of each route in turn.
/// The measurement is therefore made on the shipping renderer (WKWebView under Mac Catalyst) rather
/// than on Chromium, which makes it stronger evidence than the run it replaces, not weaker.
/// </para>
/// <para>
/// Compiled only in Debug and inert unless <c>TECHIEDESK_A11Y_SCAN=1</c> is set, so a normal
/// Debug run is completely unaffected. Nothing here ships in Release.
/// </para>
/// </remarks>
internal static class A11yScanRunner
{
    /// <summary>Environment variable that arms the sweep.</summary>
    private const string EnableVariable = "TECHIEDESK_A11Y_SCAN";

    /// <summary>Environment variable holding the absolute path of <c>axe.min.js</c>.</summary>
    private const string AxePathVariable = "TECHIEDESK_AXE_PATH";

    /// <summary>Environment variable holding the absolute path of the JSON report to write.</summary>
    private const string OutputVariable = "TECHIEDESK_A11Y_OUT";

    /// <summary>
    /// Environment variable holding a comma-separated route list that replaces <see cref="Routes"/>.
    /// </summary>
    /// <remarks>
    /// Lets a sweep be resumed or narrowed without a rebuild — a full pass takes long enough that a
    /// single unlucky route should not cost the other twenty-nine.
    /// </remarks>
    private const string RoutesVariable = "TECHIEDESK_A11Y_ROUTES";

    /// <summary>Milliseconds allowed for a route to settle before it is scanned.</summary>
    private const int SettleDelayMs = 4000;

    /// <summary>Milliseconds a single route may take before the sweep gives up on it.</summary>
    private const int RouteTimeoutMs = 40000;

    /// <summary>1 once a sweep has been started, so a second handler change cannot start another.</summary>
    private static int started;

    /// <summary>Every concrete route the router serves, in the order they are scanned.</summary>
    /// <remarks>
    /// Parameterized routes are instantiated with the <c>default</c> workspace slug, matching the
    /// route set the retired Playwright sweep used. <c>/workspace/{Slug}/connectors/{ConnectorId}/edit</c>
    /// is represented by its sibling <c>/connectors/new</c> — same component, no live connector id
    /// needed.
    /// </remarks>
    private static readonly string[] Routes =
    [
        "/",
        "/admin/events",
        "/admin/settings",
        "/automations",
        "/billing",
        "/chat",
        "/forgot-password",
        "/ingestion",
        "/llm-playground",
        "/llm-settings",
        "/login",
        "/pricing",
        "/profile",
        "/qdrant-admin",
        "/rag-config",
        "/register",
        "/reset-password",
        "/settings/data",
        "/settings/updates",
        "/setup",
        "/support",
        "/text-ingestion",
        "/token-usage",
        "/workspace/default",
        "/workspace/default/agents",
        "/workspace/default/connectors",
        "/workspace/default/connectors/new",
        "/workspace/default/documents",
        "/workspace/default/documents/web",
        "/workspace/default/settings",
    ];

    /// <summary>
    /// Runs the sweep if it is armed, then quits the app; otherwise returns immediately.
    /// </summary>
    /// <param name="webView">The hosting web view, used for its component dispatcher and scope.</param>
    /// <returns>A task that completes once the report has been written.</returns>
    public static Task MaybeRunAsync(BlazorWebView webView)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return Task.CompletedTask;
        }

        // OnHandlerChanged fires more than once per page. Without this guard two sweeps run at the
        // same time, interleave their NavigateTo calls — so a route gets scanned while the DOM
        // still belongs to whatever the OTHER sweep just navigated to — and the first one to
        // finish calls Environment.Exit, truncating the report. Both were observed.
        if (Interlocked.Exchange(ref started, 1) == 1)
        {
            return Task.CompletedTask;
        }

        return RunAsync(webView);
    }

    /// <summary>Navigates every route, scans it with axe-core, and writes the JSON report.</summary>
    /// <param name="webView">The hosting web view.</param>
    /// <returns>A task that completes once the report has been written.</returns>
    private static async Task RunAsync(BlazorWebView webView)
    {
        var axePath = Environment.GetEnvironmentVariable(AxePathVariable)
                      ?? "node_modules/axe-core/axe.min.js";
        var outputPath = Environment.GetEnvironmentVariable(OutputVariable)
                         ?? Path.Combine(Path.GetTempPath(), "techiedesk-a11y.json");

        var report = new StringBuilder();
        report.Append("{\"axe\":\"").Append(axePath.Replace("\\", "/")).Append("\",\"routes\":[");

        var first = true;
        try
        {
            var axeSource = await File.ReadAllTextAsync(axePath);

            // The web view needs a moment after the handler appears before its Blazor runtime can
            // accept interop; a fixed wait is enough here and keeps this file free of lifecycle
            // plumbing that only a diagnostic would need.
            await Task.Delay(4000);

            var routes = Environment.GetEnvironmentVariable(RoutesVariable) is { Length: > 0 } list
                ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Routes;

            foreach (var route in routes)
            {
                var routeJson = await ScanRouteAsync(webView, route, axeSource);
                if (!first)
                {
                    report.Append(',');
                }

                report.Append(routeJson);
                first = false;

                // Flushed per route rather than once at the end: a sweep that dies on route 12
                // must still yield the eleven routes it did measure, otherwise a crash costs the
                // whole run and tells you nothing about where it failed.
                await File.WriteAllTextAsync(outputPath, report + "]}");
            }
        }
        catch (Exception exception)
        {
            report.Append(first ? string.Empty : ",")
                  .Append("{\"route\":\"<runner>\",\"error\":")
                  .Append(System.Text.Json.JsonSerializer.Serialize(exception.ToString()))
                  .Append('}');
        }

        report.Append("]}");
        await File.WriteAllTextAsync(outputPath, report.ToString());

        // A diagnostic run must not leave a window sitting on screen waiting to be closed by hand.
        Environment.Exit(0);
    }

    /// <summary>Navigates to one route and returns its axe result as a JSON object literal.</summary>
    /// <param name="webView">The hosting web view.</param>
    /// <param name="route">The route to scan.</param>
    /// <param name="axeSource">The axe-core bundle source.</param>
    /// <returns>A JSON object describing the route's violations, or its failure.</returns>
    private static async Task<string> ScanRouteAsync(BlazorWebView webView, string route, string axeSource)
    {
        // TryDispatchAsync takes an Action, NOT a Func<..., Task>: an async lambda passed to it binds
        // as async void, so the call returns the moment the callback hits its first await and every
        // exception inside it is lost. The completion source is what makes the dispatched work
        // awaitable and its failures observable.
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = await webView.TryDispatchAsync(services =>
        {
            _ = ScanOnDispatcherAsync(services, route, axeSource, completion);
        });

        if (!dispatched)
        {
            return $"{{\"route\":\"{route}\",\"error\":\"dispatch refused\"}}";
        }

        // A route whose render never settles must cost its own entry, not the rest of the sweep.
        var finished = await Task.WhenAny(completion.Task, Task.Delay(RouteTimeoutMs));
        if (finished != completion.Task)
        {
            return $"{{\"route\":\"{route}\",\"error\":\"timed out\"}}";
        }

        var result = await completion.Task;
        return $"{{\"route\":\"{route}\",\"result\":{result}}}";
    }

    /// <summary>Navigates and scans on the component dispatcher, completing the supplied source.</summary>
    /// <param name="services">The web view's service scope.</param>
    /// <param name="route">The route to scan.</param>
    /// <param name="axeSource">The axe-core bundle source.</param>
    /// <param name="completion">Completed with the route's JSON result, or with a JSON error object.</param>
    /// <returns>A task that completes once <paramref name="completion"/> has been set.</returns>
    private static async Task ScanOnDispatcherAsync(
        IServiceProvider services,
        string route,
        string axeSource,
        TaskCompletionSource<string> completion)
    {
        try
        {
            var navigation = services.GetRequiredService<NavigationManager>();
            var js = services.GetRequiredService<IJSRuntime>();

            navigation.NavigateTo(route);
            await Task.Delay(SettleDelayMs);

            // Re-evaluated per route: a full-document navigation would drop window.axe, and the
            // guard makes the re-injection free when it would not.
            await js.InvokeVoidAsync("eval", $"if(!window.axe){{{axeSource}}}");
            completion.TrySetResult(await js.InvokeAsync<string>("eval", ScanScript));
        }
        catch (Exception exception)
        {
            completion.TrySetResult(
                "{\"error\":" + System.Text.Json.JsonSerializer.Serialize(exception.ToString()) + "}");
        }
    }

    /// <summary>
    /// The axe invocation. Returns a promise of a compact JSON string — the full axe result is
    /// megabytes of rule metadata that would be slow to marshal and useless to read.
    /// </summary>
    private const string ScanScript = """
        (async () => {
          const r = await axe.run(document, {
            runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa','best-practice'] },
            resultTypes: ['violations']
          });
          return JSON.stringify({
            title: (document.querySelector('h1') || {}).innerText || '',
            violations: r.violations.map(v => ({
              id: v.id,
              impact: v.impact,
              tags: v.tags,
              nodes: v.nodes.map(n => ({
                target: (n.target || []).join(' '),
                html: (n.html || '').slice(0, 260),
                why: (n.failureSummary || '').replace(/\s+/g, ' ').slice(0, 220)
              }))
            }))
          });
        })()
        """;
}
#endif
