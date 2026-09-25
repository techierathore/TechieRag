using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// How the iOS memory reading is interpreted (found by the probe's second button on the iOS
/// simulator, REQ-FN-059; the gate itself is REQ-RAG-060).
/// </summary>
public sealed class AvailableMemoryIosTests
{
    /// <summary>
    /// <c>os_proc_available_memory()</c> returns 0 when the process has no memory limit (the iOS
    /// simulator); that means "not reported", so the fallback estimate is used instead of refusing
    /// every load with "0 B available".
    /// </summary>
    [Fact(DisplayName = "REQ-FN-059 IosZeroMeansNoLimitReported")]
    public void IosZeroMeansNoLimitReported() =>
        Assert.Equal(6_000_000_000, AvailableMemory.FromIosLimit(0, () => 6_000_000_000));

    /// <summary>A real limit from <c>os_proc_available_memory()</c> is used as it is.</summary>
    [Fact(DisplayName = "REQ-FN-059 IosLimitIsUsedAsReported")]
    public void IosLimitIsUsedAsReported() =>
        Assert.Equal(1_200_000_000, AvailableMemory.FromIosLimit(1_200_000_000, () => 6_000_000_000));
}
