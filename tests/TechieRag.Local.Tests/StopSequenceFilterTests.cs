using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>Cutting streamed text at stop sequences (REQ-RAG-059 / BRD-98).</summary>
public sealed class StopSequenceFilterTests
{
    /// <summary>A stop sequence split across pieces is caught and never partly emitted.</summary>
    [Fact]
    public void SplitStopSequenceIsCaught()
    {
        var filter = new StopSequenceFilter(["<|end|>"]);

        var emitted = string.Concat(new[] { "Hi <|", "en", "d|> more" }.Select(filter.Push)) + filter.Flush();

        Assert.Equal("Hi ", emitted);
        Assert.True(filter.Stopped);
    }

    /// <summary>Text that only looked like the start of a stop sequence is released at the end.</summary>
    [Fact]
    public void FalseStartIsReleased()
    {
        var filter = new StopSequenceFilter(["STOP"]);

        var emitted = string.Concat(new[] { "A ST", "ILL day" }.Select(filter.Push)) + filter.Flush();

        Assert.Equal("A STILL day", emitted);
        Assert.False(filter.Stopped);
    }

    /// <summary>With no stop sequences every piece passes straight through.</summary>
    [Fact]
    public void NoStopSequencesPassEverything()
    {
        var filter = new StopSequenceFilter([]);

        Assert.Equal("abc", filter.Push("abc"));
        Assert.Equal(string.Empty, filter.Flush());
    }
}
