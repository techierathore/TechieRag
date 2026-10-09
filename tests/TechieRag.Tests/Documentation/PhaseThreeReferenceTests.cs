using Xunit;

namespace TechieRag.Tests.Documentation;

/// <summary>
/// The shipped AI reference describes every public member added for phase-3 cluster A
/// (REQ-RAG-114 to REQ-RAG-124), each section with a C# call example.
/// </summary>
public sealed class PhaseThreeReferenceTests
{
    private const string ShippedReference = "src/TechieRag/build/content/TechieRag-AI-Reference.md";

    /// <summary>The new public members, keyed by the row that added them.</summary>
    public static TheoryData<string, string> Members => new()
    {
        { "REQ-RAG-114", "DocumentFilters" },
        { "REQ-RAG-114", "SearchDocumentsAsync" },
        { "REQ-RAG-115", "Rerank rule" },
        { "REQ-RAG-116", "IWorkspaceManager" },
        { "REQ-RAG-117", "ConnectorRunCanceledException" },
        { "REQ-RAG-117", "PartialResult" },
        { "REQ-RAG-117", "PartialIngestion" },
        { "REQ-RAG-118", "ConnectorItemOutcome.Skipped" },
        { "REQ-RAG-118", "ConnectorIngestionExtensions.BuildMetadata" },
        { "REQ-RAG-119", "ParsedMailMessage.Notes" },
        { "REQ-RAG-119", "MailParseCodes" },
        { "REQ-RAG-119", "MimeParser.MaxNestingDepth" },
        { "REQ-RAG-120", "sourceKey" },
        { "REQ-RAG-120", "DocumentMetadataKeys.SourceKey" },
        { "REQ-RAG-120", "ConnectorIngestionExtensions.ConnectorDocumentKey" },
        { "REQ-RAG-121", "ToolRegistry.Register" },
        { "REQ-RAG-122", "DataRoot.DefaultDatabasePath" },
        { "REQ-RAG-122", "DataRoot.SetAppName" },
        { "REQ-RAG-122", "DataRoot.Set" },
        { "REQ-RAG-122", "UseSqliteVec()" },
        { "REQ-RAG-122", "WithPersistence(StoreProvider.Sqlite)" },
        { "REQ-RAG-122", "IsConnectionStringSet" },
        { "REQ-RAG-122", "defaultUserId: \"alice\"" },
        { "REQ-RAG-123", "stepName" },
        { "REQ-RAG-124", "UsesLanguageModel()" },
        { "REQ-RAG-124", "GetLanguageModelSteps()" },
        { "REQ-RAG-125", "LocalLlmProvider.IsRuntimeAvailable" },
        { "REQ-RAG-125", "EstimateRequiredMemoryBytes" },
        { "REQ-RAG-125", "AvailableMemory.Read()" },
        { "REQ-RAG-125", "DownloadProgress" },
        { "REQ-RAG-126", "ListModelsAsync" },
        { "REQ-RAG-127", "endpoint: \"http://192.168.1.20:1234\"" },
        { "REQ-RAG-128", "ModelChooser" },
        { "REQ-RAG-128", "ModelChoiceSource.SmallModel" },
        { "REQ-RAG-128", "IModelChooser" },
        { "REQ-RAG-128", "FallbackModelId" },
    };

    /// <summary>
    /// Every new member is named in the phase-3 section of the shipped reference, so a consumer's
    /// agent can find it.
    /// </summary>
    /// <param name="row">The requirement row that added the member.</param>
    /// <param name="member">The member's name as the reference spells it.</param>
    [Theory(DisplayName = "ShippedReferenceNamesPhaseThreeMember")]
    [MemberData(nameof(Members))]
    public void ShippedReferenceNamesPhaseThreeMember(string row, string member)
    {
        var section = PhaseThreeSection();

        Assert.True(section.Contains(member, StringComparison.Ordinal), $"{row}: the AI reference's phase-3 section does not mention {member}.");
    }

    /// <summary>Each phase-3 subsection carries a C# call example.</summary>
    [Fact(DisplayName = "EveryPhaseThreeSectionHasAnExample")]
    public void EveryPhaseThreeSectionHasAnExample()
    {
        var sections = PhaseThreeSection().Split("\n### ").Skip(1).ToList();

        Assert.Equal(10, sections.Count);  // 7 from the phase-3 build + REQ-RAG-125 (Sevak TR-RAG-047) + REQ-RAG-126/127 (Lekhak TR-RAG-004/005) + REQ-RAG-128 (Chatur TR-RAG-006)
        Assert.All(sections, section => Assert.Contains("```csharp", section, StringComparison.Ordinal));
    }

    /// <summary>
    /// REQ-RAG-115: the API documentation states the rule — pinned results follow the workspace's own
    /// rerank switch and the library default never overrides it inside a workspace.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-115 ReferenceStatesWorkspaceRerankRule")]
    public void ReferenceStatesWorkspaceRerankRule()
    {
        var section = PhaseThreeSection();

        Assert.Contains("**Rerank rule:** every workspace retrieval, pinned results included, follows `Workspace.RerankEnabled`", section, StringComparison.Ordinal);
        Assert.Contains("never overrides it inside a workspace", section, StringComparison.Ordinal);
    }

    private static string PhaseThreeSection()
    {
        var reference = LibraryRepoFiles.Read(ShippedReference).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = reference.IndexOf("## Phase-3 Additions", StringComparison.Ordinal);
        Assert.True(start >= 0, "The AI reference has no phase-3 section.");
        var end = reference.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        return reference[start..end];
    }
}
