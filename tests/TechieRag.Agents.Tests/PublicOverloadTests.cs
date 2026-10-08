using TechieRag.Tests.PublicApi;
using Xunit;

namespace TechieRag.Agents.Tests;

/// <summary>
/// The release check for overloads that capture an existing call, run on TechieRag.Agents (Sevak TR-RAG-049).
/// </summary>
public sealed class PublicOverloadTests
{
    /// <summary>
    /// No two public overloads in TechieRag.Agents take the same positional argument types under different parameter
    /// names, so adding an overload can never silently re-bind a call that compiled against the previous release.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-122 PublicOverloadsNeverGiveOneCallTwoMeanings")]
    public void PublicOverloadsNeverGiveOneCallTwoMeanings()
    {
        var conflicts = OverloadShapeCheck.FindConflicts(typeof(TechieRagAgent).Assembly);

        Assert.True(conflicts.Count == 0, string.Join(Environment.NewLine, conflicts));
    }
}
