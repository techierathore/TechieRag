using System.Globalization;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// Plain-language rendering of a schedule (BRD-140 / REQ-UI-046, REQ-UI-055). Two load-bearing
/// properties: no output ever contains cron syntax, because this is the only text the user is shown;
/// and the sentence is built from resource keys, so a Hindi window reads Hindi.
/// </summary>
public sealed class CronDescriberTests
{
    /// <summary>
    /// Cron shapes that exercise every branch of the describer, used by the whole-corpus tests below.
    /// </summary>
    /// <remarks>
    /// Kept as one list so a branch added to the describer without a resource key behind it fails in
    /// both languages at once, rather than only where somebody remembered to add a case.
    /// </remarks>
    public static readonly string[] Corpus =
    [
        "0 7 * * 1-5",
        "*/30 * * * *",
        "0 3 * * *",
        "0 6 * * 1",
        "0 9 1 * *",
        "0 0 13 * 5",
        "0 0 1,15 1,7 *",
        "15,45 */6 * * *",
        "*/7 */3 5-9 2-4 MON-WED",
        "* * * * *",
        "* * * * 0,6",
        "30 * * * *",
        "0 */4 * * *",
        "0 12 1-10 * *",
        "0 8 * 1-5 *",
        "0 8 * * SUN,TUE,THU",
        "0 0 21,22,23 * *",
        "0 5 3 3 *"
    ];

    /// <summary>The canonical weekday-morning schedule reads as the sentence BRD-140 quotes.</summary>
    [Fact]
    public void WeekdayMorningReadsAsPlainEnglish()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal("Every weekday at 07:00", CronDescriber.TryDescribe("0 7 * * 1-5", resources.Localize));
    }

    /// <summary>A step expression reads as an interval, not as a slash.</summary>
    [Fact]
    public void HalfHourlyReadsAsAnInterval()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal("Every 30 minutes", CronDescriber.TryDescribe("*/30 * * * *", resources.Localize));
    }

    /// <summary>A daily schedule names the time of day.</summary>
    [Fact]
    public void NightlyReadsAsEveryDay()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal("Every day at 03:00", CronDescriber.TryDescribe("0 3 * * *", resources.Localize));
    }

    /// <summary>A single weekday is named in full.</summary>
    [Fact]
    public void WeeklyNamesTheDay()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal("Every Monday at 06:00", CronDescriber.TryDescribe("0 6 * * 1", resources.Localize));
    }

    /// <summary>A day of the month is rendered as an ordinal.</summary>
    [Fact]
    public void MonthlyReadsAsAnOrdinal()
    {
        using var resources = new ResourceHarness("en");

        Assert.Equal(
            "On the 1st of the month at 09:00", CronDescriber.TryDescribe("0 9 1 * *", resources.Localize));
    }

    /// <summary>Both day fields restricted is rendered as the "or" it actually means.</summary>
    [Fact]
    public void BothDayFieldsRestrictedSaysOr()
    {
        using var resources = new ResourceHarness("en");

        var described = CronDescriber.TryDescribe("0 0 13 * 5", resources.Localize);

        Assert.Contains(" or ", described);
        Assert.Contains("Friday", described);
        Assert.Contains("13th", described);
    }

    /// <summary>
    /// The same expressions read as Hindi sentences, in Hindi word order, on a Hindi install.
    /// </summary>
    /// <param name="expression">The cron expression.</param>
    /// <param name="expected">What the reader sees.</param>
    /// <remarks>
    /// <para>
    /// Asserted as EXACT sentences rather than "contains Devanagari", because the thing REQ-UI-055 is
    /// about is not whether the words were translated — it is whether they were assembled in an order
    /// a Hindi speaker uses. Every one of these differs structurally from its English counterpart: the
    /// time takes a trailing <i>बजे</i> where English leads with "at"; the months come BEFORE the day
    /// phrase where English puts them after; and a date takes no ordinal ending at all. A
    /// fragment-by-fragment translation passes a coverage count and fails every line below.
    /// </para>
    /// <para>
    /// ⚠ The Hindi is agent-produced and has not been reviewed by a native speaker. The known
    /// infelicity is visible on the fourth row: a multi-date list repeats <i>तारीख़</i> once per date,
    /// where a native speaker would say it once.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("0 7 * * 1-5", "हर कार्यदिवस 07:00 बजे")]
    [InlineData("*/30 * * * *", "हर 30 मिनट में")]
    [InlineData("0 3 * * *", "हर दिन 03:00 बजे")]
    [InlineData("0 6 * * 1", "हर सोमवार 06:00 बजे")]
    [InlineData("0 9 1 * *", "हर महीने की 1 तारीख़ को 09:00 बजे")]
    [InlineData("0 0 13 * 5", "हर शुक्रवार या हर महीने की 13 तारीख़ को 00:00 बजे")]
    [InlineData("0 0 1,15 1,7 *", "जनवरी और जुलाई में हर महीने की 1 तारीख़ और 15 तारीख़ को 00:00 बजे")]
    [InlineData("* * * * 0,6", "सप्ताहांत के हर दिन हर मिनट")]
    [InlineData("30 * * * *", "हर घंटे 30 मिनट पर")]
    [InlineData("0 8 * * SUN,TUE,THU", "हर रवि, मंगल और गुरु 08:00 बजे")]
    public void RendersTheScheduleInHindiWordOrder(string expression, string expected)
    {
        using var resources = new ResourceHarness("hi");

        Assert.Equal(expected, CronDescriber.TryDescribe(expression, resources.Localize));
    }

    /// <summary>
    /// Every branch of the describer resolves to real text in both shipped languages.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// <c>ResourceManagerStringLocalizer</c> returns the KEY NAME when a lookup misses, so a sentence
    /// containing "Cron" or "Schedule" in a Hindi window is a key that never landed. That is the exact
    /// shape of the REQ-UI-051 defect — a service handing a screen something it cannot render — and it
    /// is what this catches for the shapes the exact-sentence theory above does not enumerate.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryShapeResolvesThroughTheRealResources(string culture)
    {
        using var resources = new ResourceHarness(culture);

        foreach (var expression in Corpus)
        {
            var described = CronDescriber.TryDescribe(expression, resources.Localize);

            Assert.False(string.IsNullOrWhiteSpace(described), $"'{expression}' described as nothing.");
            Assert.DoesNotContain("Cron", described, StringComparison.Ordinal);
            Assert.DoesNotContain("ScheduleDraft", described, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", described, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A Hindi window gets a genuinely different sentence, written in Devanagari, for every shape.
    /// </summary>
    /// <remarks>
    /// The counterpart of the check above: that one proves the keys resolved, this proves they
    /// resolved to something other than the English. A resource set that fell back to the neutral file
    /// — the half-translated failure REQ-UI-051 was raised for — satisfies the first and fails this.
    /// </remarks>
    [Fact]
    public void TheHindiSentenceIsNeverTheEnglishOne()
    {
        string[] english;
        using (var resources = new ResourceHarness("en"))
        {
            english = [.. Corpus.Select(expression => CronDescriber.TryDescribe(expression, resources.Localize)!)];
        }

        using var hindi = new ResourceHarness("hi");

        for (var index = 0; index < Corpus.Length; index++)
        {
            var described = CronDescriber.TryDescribe(Corpus[index], hindi.Localize)!;

            Assert.NotEqual(english[index], described);
            Assert.Contains(described, character => character is >= 'ऀ' and <= 'ॿ');
        }
    }

    /// <summary>
    /// Numbers stay Latin digits in Hindi, so a time in the sentence matches a time from the clock.
    /// </summary>
    /// <remarks>
    /// A decision, not an accident. .NET's <c>hi</c> culture does not substitute Devanagari digits, so
    /// every date and count elsewhere in TechieDesk is already Latin; a describer that printed ०७:०० would
    /// be the only place in the app that did, sitting in the same table as a next-run time that did
    /// not. The describer formats with <see cref="CultureInfo.InvariantCulture"/> before the number
    /// reaches a resource string, so this cannot drift with the ambient culture.
    /// </remarks>
    [Fact]
    public void NumbersStayLatinDigitsInHindi()
    {
        using var resources = new ResourceHarness("hi");

        Assert.Contains("07:00", CronDescriber.TryDescribe("0 7 * * 1-5", resources.Localize));
        Assert.Contains("30", CronDescriber.TryDescribe("*/30 * * * *", resources.Localize));

        foreach (var expression in Corpus)
        {
            // U+0966-U+096F are the Devanagari digits. None of them may appear.
            Assert.DoesNotContain(
                CronDescriber.TryDescribe(expression, resources.Localize)!,
                character => character is >= '०' and <= '९');
        }
    }

    /// <summary>
    /// The cron expression itself is untouched by describing it, in either language.
    /// </summary>
    /// <remarks>
    /// The trap REQ-UI-055 names: a value that is BOTH the stored/wire form and the label, translated
    /// once and then parsed back. <c>CronExpression.Expression</c> is what is written to the schedule
    /// row and re-parsed on every poll; the describer only ever READS the parsed fields, and this
    /// pins that it stays byte-identical while the sentence around it changes language.
    /// </remarks>
    [Fact]
    public void DescribingNeverTouchesTheExpressionItself()
    {
        foreach (var expression in Corpus)
        {
            var parsed = CronExpression.Parse(expression);
            var normalized = parsed.Expression;

            using (var english = new ResourceHarness("en"))
            {
                CronDescriber.Describe(parsed, english.Localize);
            }

            using (var hindi = new ResourceHarness("hi"))
            {
                CronDescriber.Describe(parsed, hindi.Localize);
            }

            Assert.Equal(normalized, parsed.Expression);
            Assert.Equal(normalized, CronExpression.Parse(parsed.Expression).Expression);
        }
    }

    /// <summary>
    /// No description contains cron syntax, whatever the expression or the language — the rule this
    /// class exists to keep (BRD-140).
    /// </summary>
    /// <param name="expression">The expression to describe.</param>
    /// <param name="culture">The culture to render in.</param>
    [Theory]
    [InlineData("0 7 * * 1-5", "en")]
    [InlineData("*/30 * * * *", "en")]
    [InlineData("0 3 * * *", "en")]
    [InlineData("15,45 */6 * * *", "en")]
    [InlineData("0 0 1,15 1,7 *", "en")]
    [InlineData("*/7 */3 5-9 2-4 MON-WED", "en")]
    [InlineData("0 7 * * 1-5", "hi")]
    [InlineData("*/30 * * * *", "hi")]
    [InlineData("0 3 * * *", "hi")]
    [InlineData("15,45 */6 * * *", "hi")]
    [InlineData("0 0 1,15 1,7 *", "hi")]
    [InlineData("*/7 */3 5-9 2-4 MON-WED", "hi")]
    public void DescriptionNeverContainsCronSyntax(string expression, string culture)
    {
        using var resources = new ResourceHarness(culture);

        var described = CronDescriber.TryDescribe(expression, resources.Localize);

        Assert.NotNull(described);
        Assert.DoesNotContain('*', described);
        Assert.DoesNotContain('/', described);
        Assert.NotEqual(expression, described);
    }

    /// <summary>An unparseable expression describes as nothing rather than as itself.</summary>
    [Fact]
    public void UnparseableExpressionDescribesAsNull()
    {
        using var resources = new ResourceHarness("en");

        Assert.Null(CronDescriber.TryDescribe("not a schedule", resources.Localize));
    }
}
