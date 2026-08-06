using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Localization;
using TechieRag.Web;

namespace TechieDesk.Services.Web;

/// <summary>
/// Registers web ingestion — single page, site crawl and YouTube transcript
/// (REQ-RAG-016/017/018, BRD-60/61/62).
/// </summary>
public static class WebIngestionServiceCollectionExtensions
{
    /// <summary>How long a single page fetch may take before it is treated as unreachable.</summary>
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Adds the page fetcher, the transcript reader and the workspace-scoped ingestion service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The clients are registered through <c>IHttpClientFactory</c> rather than built with
    /// <see cref="HttpWebContentFetcher.CreateDefaultClient"/>, which allocates a client (and a
    /// socket handler) the caller then owns. A crawl creates a fetcher per run, so that shape would
    /// leak a handler per crawl; the factory pools them. The headers below mirror
    /// <c>CreateDefaultClient</c> deliberately — a crawler that does not identify itself is one a
    /// site operator cannot block, and not being blockable is not a feature.
    /// </remarks>
    public static IServiceCollection AddTechieDeskWebIngestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Two clients, not one, and the difference between them is the SSRF guard. It is enforced by
        // the message handler at connect time rather than by inspecting URLs, because the transport
        // follows redirects itself: a check that only sees the first and last URL has already let the
        // request to the internal address go out by the time it can object. A handler is fixed at
        // registration, so the per-run intranet opt-in has to be a second registration.
        AddGuardedClient(services, HttpWebContentFetcherFactory.HttpClientName, blockPrivateTargets: true);
        AddGuardedClient(
            services, HttpWebContentFetcherFactory.PrivateAllowedHttpClientName, blockPrivateTargets: false);

        // The transcript reader loads a normal watch page, so it wants a browser-shaped Accept and a
        // longer leash than a crawl page: the watch document is large and the caption track is a
        // second request behind it.
        services.AddHttpClient<YouTubeTranscriptReader>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "TechieDesk/1.0 (+https://github.com/techierathore/TechieRag)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        // REQ-UI-055: the ingestion service composes the progress lines, the skip reasons and the
        // run summary a person reads, so it resolves resource keys through this delegate. TryAdd, and
        // idempotent AddLocalization, so hosting order against AddTechieDeskScheduling does not
        // matter — but registering it HERE is what stops web ingestion depending on the scheduler
        // having been added first.
        services.AddLocalization();
        services.TryAddSingleton<LocalizeText>(provider =>
        {
            var localizer = provider.GetRequiredService<IStringLocalizer<AppStrings>>();
            return (key, arguments) =>
                arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
        });

        services.TryAddSingleton<IWebContentFetcherFactory, HttpWebContentFetcherFactory>();
        services.TryAddScoped<IWorkspaceDocumentLinker, WorkspaceDocumentLinker>();
        services.TryAddScoped<IWebIngestionService, WebIngestionService>();

        return services;
    }

    /// <summary>Registers one pooled crawl client with the given private-network policy.</summary>
    private static void AddGuardedClient(
        IServiceCollection services,
        string name,
        bool blockPrivateTargets) =>
        services
            .AddHttpClient(name, client =>
            {
                client.Timeout = PageTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "TechieDesk/1.0 (+https://github.com/techierathore/TechieRag)");
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(
                () => HttpWebContentFetcher.CreateGuardedHandler(blockPrivateTargets));
}
