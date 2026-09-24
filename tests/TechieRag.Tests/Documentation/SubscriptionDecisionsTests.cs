using Xunit;

namespace TechieRag.Tests.Documentation;

/// <summary>
/// REQ-FN-062 / BRD-114: the per-vendor subscription sign-in policy and flow, checked against live
/// documentation, is recorded in <c>DECISIONS.md</c> before the sign-in providers are built.
/// </summary>
public sealed class SubscriptionDecisionsTests
{
    private static readonly string[] Vendors = ["OpenAI", "Anthropic", "Google", "Groq", "xAI", "Meta"];

    /// <summary>
    /// Acceptance of REQ-FN-062: <c>DECISIONS.md</c> holds a dated "checked live" entry for subscription
    /// sign-in with one row per vendor (OpenAI, Anthropic, Google, Groq, xAI, Meta), each stating whether a
    /// third-party app may use the subscription, plus a sources list naming every vendor.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-062 DecisionsRecordEveryVendorsSignInPolicy")]
    public void DecisionsRecordEveryVendorsSignInPolicy()
    {
        var decisions = LocalModelDocsTests.ReadRepoFile("DECISIONS.md");

        var start = decisions.IndexOf("Subscription sign-in: vendor policy and flow, checked live", StringComparison.Ordinal);
        Assert.True(start >= 0, "DECISIONS.md has no 'Subscription sign-in: vendor policy and flow, checked live' entry.");

        var headingStart = decisions.LastIndexOf("\n## ", start, StringComparison.Ordinal);
        var next = decisions.IndexOf("\n## ", start, StringComparison.Ordinal);
        var entry = decisions[(headingStart < 0 ? 0 : headingStart)..(next < 0 ? decisions.Length : next)];

        Assert.Matches(@"## \d{4}-\d{2}-\d{2}", entry);
        Assert.Contains("REQ-FN-062", entry, StringComparison.Ordinal);

        var rows = entry.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).ToList();
        foreach (var vendor in Vendors)
        {
            var row = rows.FirstOrDefault(line => line.StartsWith($"| {vendor}", StringComparison.Ordinal));
            Assert.True(row is not null, $"No policy row for {vendor}.");
            Assert.Matches(@"\*\*(Yes|No|Not confirmed|No consumer subscription exists|No flow exists)", row);
        }

        var sources = entry[entry.IndexOf("**Sources", StringComparison.Ordinal)..];
        Assert.All(Vendors, vendor => Assert.Contains($"- {vendor}:", sources, StringComparison.Ordinal));
        Assert.Contains("https://", sources, StringComparison.Ordinal);
    }
}
