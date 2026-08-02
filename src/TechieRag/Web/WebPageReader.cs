using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace TechieRag.Web;

/// <summary>
/// Turns raw HTML into readable text, a title, and the links a crawler may follow
/// (REQ-RAG-031 / BRD-112).
/// </summary>
/// <remarks>
/// Extraction is shared with <c>HtmlProcessor</c> in spirit but not in code, because that type
/// answers a different question: it converts a *file* into chunks and never needs links or a final
/// URL. Reusing it here would have meant giving a document processor a crawler's concerns.
/// </remarks>
public static class WebPageReader
{
    /// <summary>Elements whose content is never readable prose.</summary>
    private static readonly HashSet<string> NonContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "svg", "canvas", "iframe", "object", "embed", "head",
        // Site chrome. Dropping it is what stops every crawled page from ingesting the same
        // navigation menu and footer, which otherwise dominates a site's embeddings and makes every
        // query retrieve the nav bar.
        "nav", "header", "footer", "aside", "form", "template",
    };

    /// <summary>Schemes a crawler may follow. Everything else is a non-document link.</summary>
    private static readonly HashSet<string> FollowableSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps,
    };

    /// <summary>Reads a page from HTML.</summary>
    /// <param name="html">The raw HTML.</param>
    /// <param name="requestedUrl">The URL that was requested.</param>
    /// <param name="finalUrl">The URL after redirects; defaults to <paramref name="requestedUrl"/>.</param>
    /// <returns>The extracted page.</returns>
    public static WebPage Read(string html, string requestedUrl, string? finalUrl = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestedUrl);

        var resolved = finalUrl ?? requestedUrl;
        if (string.IsNullOrWhiteSpace(html))
        {
            return new WebPage(requestedUrl, resolved, TitleFallback(resolved), string.Empty, []);
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var title = ExtractTitle(document) ?? TitleFallback(resolved);
        var links = ExtractLinks(document, resolved);

        // Title is read BEFORE the <head> strip below, or every page would fall back to its host.
        RemoveNonContent(document);

        return new WebPage(requestedUrl, resolved, title, ExtractText(document), links);
    }

    private static string? ExtractTitle(HtmlDocument document)
    {
        var node = document.DocumentNode.SelectSingleNode("//title")
                   ?? document.DocumentNode.SelectSingleNode("//h1");
        if (node is null)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(node.InnerText)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : Collapse(text);
    }

    private static string TitleFallback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static void RemoveNonContent(HtmlDocument document)
    {
        var doomed = document.DocumentNode
            .Descendants()
            .Where(node => NonContentTags.Contains(node.Name))
            .ToList();

        foreach (var node in doomed)
        {
            node.Remove();
        }
    }

    private static string ExtractText(HtmlDocument document)
    {
        var builder = new StringBuilder();
        foreach (var node in document.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Text)
            {
                continue;
            }

            var text = WebUtility.HtmlDecode(node.InnerText);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            builder.Append(Collapse(text)).Append('\n');
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> ExtractLinks(HtmlDocument document, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return [];
        }

        var anchors = document.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
        {
            return [];
        }

        // Ordered set: a crawler that respects maxLinks must take them in document order, and a page
        // that links the same target twenty times must not consume twenty of the budget.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<string>();

        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", null);
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, href.Trim(), out var absolute)
                || !FollowableSchemes.Contains(absolute.Scheme))
            {
                continue;
            }

            // The fragment is dropped deliberately: /page and /page#section are one document, and
            // keeping both would ingest it twice and spend twice the crawl budget.
            var normalized = new UriBuilder(absolute) { Fragment = string.Empty }.Uri.ToString();
            if (seen.Add(normalized))
            {
                links.Add(normalized);
            }
        }

        return links;
    }

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }
}
