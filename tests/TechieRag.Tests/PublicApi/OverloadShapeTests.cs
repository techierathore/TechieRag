using TechieRag.Embedded;
using TechieRag.Telemetry;
using Xunit;

namespace TechieRag.Tests.PublicApi;

/// <summary>
/// The release check for overloads that capture an existing call (Sevak TR-RAG-049, REQ-RAG-122).
/// </summary>
public sealed class OverloadShapeTests
{
    /// <summary>
    /// No two public overloads in TechieRag, TechieRag.Embedded or TechieRag.Telemetry take the same positional
    /// argument types under different parameter names, so adding an overload can never silently re-bind a
    /// call that compiled against the previous release.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 PublicOverloadsNeverGiveOneCallTwoMeanings")]
    public void PublicOverloadsNeverGiveOneCallTwoMeanings()
    {
        var conflicts = new[] { typeof(TechieRagBuilder).Assembly, typeof(ModelDownloadService).Assembly, typeof(TechieRagTelemetryServiceCollectionExtensions).Assembly }
            .SelectMany(OverloadShapeCheck.FindConflicts)
            .ToList();

        Assert.True(conflicts.Count == 0, string.Join(Environment.NewLine, conflicts));
    }

    /// <summary>
    /// The check catches the 1.1.2 defect itself: a <c>(provider, defaultUserId)</c> overload beside
    /// <c>(provider, connectionString, defaultUserId = "default")</c> is reported.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 OverloadCheckCatchesTheCapturingOverload")]
    public void OverloadCheckCatchesTheCapturingOverload()
    {
        var conflicts = OverloadShapeCheck.FindConflicts(typeof(CapturingOverloadSample).Assembly)
            .Where(c => c.Contains(nameof(CapturingOverloadSample), StringComparison.Ordinal))
            .ToList();

        var conflict = Assert.Single(conflicts);
        Assert.Contains("2 positional argument(s)", conflict, StringComparison.Ordinal);
    }
}

/// <summary>The 1.1.2 overload pair, kept as a sample the overload check must report.</summary>
public sealed class CapturingOverloadSample
{
    /// <summary>The 1.0.8 shape.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="defaultUserId">The user.</param>
    /// <returns>The connection string.</returns>
    public string WithPersistence(StoreProvider provider, string connectionString, string defaultUserId = "default") => connectionString;

    /// <summary>The 1.1.2 overload that captured the two-argument call.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="defaultUserId">The user.</param>
    /// <returns>The user.</returns>
    public string WithPersistence(StoreProvider provider, string defaultUserId) => defaultUserId;
}
