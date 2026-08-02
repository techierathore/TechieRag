using TechieDesk.Services.Support;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-032 / owner review 2026-07-26: Type and Priority carry their FULL sets, each priority
/// with a plain-language qualifier.
/// </summary>
/// <remarks>
/// The review's complaint was a dropdown offering one value, so these tests assert the counts and
/// the qualifiers directly — the shape of the defect, not a proxy for it.
/// </remarks>
public sealed class SupportIssueCatalogTests
{
    /// <summary>All six issue types the review listed are offered.</summary>
    [Fact]
    public void EverySixIssueTypesAreOffered()
    {
        using var resources = new ResourceHarness("en");

        var labels = SupportIssueCatalog.Types
            .Select(option => resources.Require(option.LabelKey))
            .ToArray();

        Assert.Equal(6, labels.Length);
        Assert.Contains("Bug", labels);
        Assert.Contains("Feature request", labels);
        Assert.Contains("Question", labels);
        Assert.Contains("Billing & licensing", labels);
        Assert.Contains("Data / ingestion problem", labels);
        Assert.Contains("Other", labels);
    }

    /// <summary>All four priorities are offered, lowest first, each with a qualifier.</summary>
    [Fact]
    public void AllFourPrioritiesCarryQualifiers()
    {
        using var resources = new ResourceHarness("en");
        var priorities = SupportIssueCatalog.Priorities;

        Assert.Equal(["Low", "Medium", "High", "Critical"], priorities.Select(option => option.Code));
        Assert.All(
            priorities,
            option => Assert.False(string.IsNullOrWhiteSpace(resources.Require(option.QualifierKey))));
        Assert.Equal(
            "High — blocks my work",
            SupportIssueCatalog.PriorityLabelWithQualifier("High", resources.Localize));
    }

    /// <summary>Wire codes round-trip to their labels.</summary>
    [Fact]
    public void CodesResolveToLabels()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal("Feature request", SupportIssueCatalog.TypeLabel("Feature", resources.Localize));
        Assert.Equal("In progress", SupportIssueCatalog.StatusLabel("InProgress", resources.Localize));
        Assert.Equal("Critical", SupportIssueCatalog.PriorityLabel("Critical", resources.Localize));
    }

    /// <summary>
    /// REQ-UI-051: the same wire codes resolve to Hindi on a Hindi install, and to something that is
    /// actually Hindi rather than the English falling through.
    /// </summary>
    [Fact]
    public void CodesResolveToHindiLabels()
    {
        using var resources = new ResourceHarness("hi");

        var type = SupportIssueCatalog.TypeLabel("Feature", resources.Localize);
        var status = SupportIssueCatalog.StatusLabel("InProgress", resources.Localize);
        var priority = SupportIssueCatalog.PriorityLabelWithQualifier("High", resources.Localize);

        Assert.NotEqual("Feature request", type);
        Assert.NotEqual("In progress", status);
        Assert.NotEqual("High — blocks my work", priority);
        Assert.All(
            new[] { type, status, priority },
            value => Assert.Contains(value, character => character is >= '\u0900' and <= '\u097F'));
    }

    /// <summary>
    /// A value the catalog does not know is shown exactly as the server sent it. Folding it into
    /// "Other" would be the screen misreporting what the issue actually is.
    /// </summary>
    [Fact]
    public void UnknownServerValueIsShownVerbatim()
    {
        using var resources = new ResourceHarness("hi");

        Assert.Equal("Escalation", SupportIssueCatalog.TypeLabel("Escalation", resources.Localize));
    }

    /// <summary>
    /// REQ-UI-051: the catalogue carries KEYS, not English. A label column that ever holds a
    /// sentence again is the defect this requirement closed, and it is invisible to every razor
    /// counter because a catalogue is not markup.
    /// </summary>
    [Fact]
    public void EveryColumnThatIsShownIsAKeyRatherThanASentence()
    {
        using var resources = new ResourceHarness("en");

        var options = SupportIssueCatalog.Types
            .Concat(SupportIssueCatalog.Priorities)
            .Concat(SupportIssueCatalog.Statuses);

        foreach (var option in options)
        {
            foreach (var key in new[] { option.LabelKey, option.QualifierKey })
            {
                Assert.DoesNotContain(' ', key);
                Assert.NotEqual(key, resources.Require(key));
            }
        }
    }

    /// <summary>
    /// Resolved is NOT closed. The screen offers "Comment &amp; close" precisely on resolved issues,
    /// so treating them as already closed would hide the button REQ-FN-027 exists for.
    /// </summary>
    [Fact]
    public void ResolvedIsNotTreatedAsClosed()
    {
        Assert.True(SupportIssueCatalog.IsClosed("Closed"));
        Assert.False(SupportIssueCatalog.IsClosed("Resolved"));
        Assert.False(SupportIssueCatalog.IsClosed(null));
    }
}
