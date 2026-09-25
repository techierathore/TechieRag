using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>The check before a model loads (REQ-RAG-060 / BRD-99).</summary>
public sealed class MemoryGateTests
{
    /// <summary>Less free memory than required throws with required, available and shortfall.</summary>
    [Fact(DisplayName = "REQ-RAG-060 ShortfallIsNamed")]
    public void ShortfallIsNamed()
    {
        var gate = new MemoryGate(() => 1_500_000_000);

        var error = Assert.Throws<LocalModelMemoryException>(() => gate.Ensure("phi-3-mini-4k-instruct", 4_200_000_000));

        Assert.Equal(2_700_000_000, error.ShortfallBytes);
        Assert.Contains("4.2 GB", error.Message);
        Assert.Contains("1.5 GB", error.Message);
        Assert.Contains("2.7 GB short", error.Message);
    }

    /// <summary>Enough free memory passes and returns the reading.</summary>
    [Fact]
    public void EnoughMemoryPasses() =>
        Assert.Equal(8_000_000_000, new MemoryGate(() => 8_000_000_000).Ensure("m", 1_000));

    /// <summary>The Linux and Android reading is MemAvailable in bytes.</summary>
    [Fact]
    public void MemAvailableIsParsed() =>
        Assert.Equal(5_038_080L * 1024, AvailableMemory.ParseMemAvailable("MemTotal:  8019536 kB\nMemFree:  1331240 kB\nMemAvailable:  5038080 kB\n"));

    /// <summary>This machine's reading is either unknown or a positive number of bytes.</summary>
    [Fact]
    public void ThisDeviceReadsMemory()
    {
        var reading = AvailableMemory.Read();

        Assert.True(reading is null or > 0);
    }
}
