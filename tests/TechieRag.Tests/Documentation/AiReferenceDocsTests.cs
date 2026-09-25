using Xunit;

namespace TechieRag.Tests.Documentation;

/// <summary>
/// REQ-FN-070 / F-AUTODIST: the AI reference and the two persona command files that ship inside the
/// NuGet package (copied into every consumer's repository by <c>build/TechieRag.targets</c>) describe
/// every phase-2 package and feature.
/// </summary>
/// <remarks>
/// Reads the shipped files under <c>src/TechieRag/build/content/</c> so a later edit that drops a
/// feature, or lets the <c>docs/</c> copy drift from the shipped one, fails the default run.
/// </remarks>
public sealed class AiReferenceDocsTests
{
    private const string ShippedReference = "src/TechieRag/build/content/TechieRag-AI-Reference.md";
    private const string DocsReference = "docs/TechieRag-AI-Reference.md";
    private const string ClaudeCommand = "src/TechieRag/build/content/techierag-claude-command.md";
    private const string OpenCodeCommand = "src/TechieRag/build/content/techierag-opencode-command.md";

    private static readonly string[] Packages = ["TechieRag.Local", "TechieRag.Agents", "TechieRag.Telemetry"];

    /// <summary>
    /// The phase-2 builder methods, entry points and members the row names; each must appear in the
    /// shipped reference with its call example.
    /// </summary>
    private static readonly string[] PhaseTwoMembers =
    [
        "UseLocalLlm",
        "LocalModel.FromHuggingFace",
        "LocalLlm.Register",
        "ConfirmTermsAsync",
        "TermsAccepted",
        "LocalModelTermsNotAcceptedException",
        "LocalModelMemoryException",
        "LocalPromptTooLongException",
        "ChatStreamEventsAsync",
        "LlmStreamEvent",
        "RunStreamAsync",
        "AgentStreamEvent",
        "UseEmbedded",
        "EmbeddedModel",
        "UseModelRoot",
        "TECHIERAG_MODEL_ROOT",
        "DownloadSizeKnown",
        "ProgressChanged",
        "TechieRagAgentBuilder",
        "UseConfiguredLlm",
        "AskStreamAsync",
        "AgentRagResponse",
        "AddTechieRagAgent",
        "LlmProviderChatClient",
        "ToolHandlerFunctions.From",
        "AIToolHandler.FromAgent",
        "RegisterKnowledgeBase",
        "TechieRagRetrievalSource",
        "AgenticInstructions.Default",
        "UseChatGptSubscriptionLlm",
        "ChatGptSubscriptionOptions",
        "ISubscriptionSessionStore",
        "SubscriptionSignInException",
        "LlmSource.Subscription",
        "UseLlmForModel",
        "ModelRouter",
        "LlmProviderFactory.CreateForModel",
        "ConnectorRunner",
        "RepositoryConnector",
        "EmailConnector",
        "ConnectorErrorCodes",
        "LimitCode",
        "ReachedLimit",
        "IngestUrlAsync",
        "IngestSiteAsync",
        "HttpWebContentFetcher",
        "WebCrawlOptions",
        "WithReranker",
        "RerankSource.LocalOnnx",
        "Rerank = true",
        "WithPersistence",
        "StoreProvider.Sqlite",
        "GetWorkspaceManager",
        "McpServerConfig",
        "McpToolHandler",
        "FlowSerializer",
        "FlowRunner",
        "AddTechieRagTelemetry",
        "EnableTracing"
    ];

    /// <summary>
    /// Acceptance of REQ-FN-070, first half: when a consumer opens the installed AI reference, the
    /// three phase-2 packages are in its packages table with an install command, and the title says v3.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-070 ShippedReferenceNamesEveryPhaseTwoPackage")]
    public void ShippedReferenceNamesEveryPhaseTwoPackage()
    {
        var reference = LibraryRepoFiles.Read(ShippedReference);

        Assert.StartsWith("# TechieRag v3 - AI Agent Reference Guide", reference, StringComparison.Ordinal);
        foreach (var package in Packages)
        {
            Assert.Contains($"| `{package}` |", reference, StringComparison.Ordinal);
            Assert.Contains($"dotnet add package {package}", reference, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Acceptance of REQ-FN-070, second half: every phase-2 builder method and entry point the row lists
    /// is described in the shipped reference, and each numbered feature carries a C# call example.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-070 ShippedReferenceDescribesEveryPhaseTwoMemberWithAnExample")]
    public void ShippedReferenceDescribesEveryPhaseTwoMemberWithAnExample()
    {
        var reference = LibraryRepoFiles.Read(ShippedReference);

        var missing = PhaseTwoMembers.Where(member => !reference.Contains(member, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "The shipped AI reference does not mention: " + string.Join(", ", missing));

        var features = reference.Split("\n### ").Where(section => section.Length > 0 && char.IsDigit(section[0])
            && reference.IndexOf("## Phase-2 Features", StringComparison.Ordinal) < reference.IndexOf("\n### " + section, StringComparison.Ordinal)).ToList();
        Assert.Equal(12, features.Count);
        var withoutExample = features.Where(section => !section.Contains("```csharp", StringComparison.Ordinal)).Select(section => section[..section.IndexOf('\n')]).ToList();
        Assert.True(withoutExample.Count == 0, "These phase-2 features have no call example: " + string.Join(", ", withoutExample));
    }

    /// <summary>
    /// REQ-FN-070: the repository copy under <c>docs/</c> is byte-identical to the file packed into
    /// the NuGet package, so a reader of either sees the same reference.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-070 DocsCopyIsIdenticalToShippedReference")]
    public void DocsCopyIsIdenticalToShippedReference()
    {
        var root = LibraryRepoFiles.Root();

        var shipped = File.ReadAllBytes(Path.Combine(root, ShippedReference));
        var docs = File.ReadAllBytes(Path.Combine(root, DocsReference));

        Assert.Equal(shipped, docs);
    }

    /// <summary>
    /// REQ-FN-070: both persona command files (the <c>/techierag</c> persona for Claude Code and for
    /// OpenCode) name the three new packages and the local model, agents and subscription sign-in
    /// features, with a command for each, so a consumer's agent can route a request to them.
    /// </summary>
    [Theory(DisplayName = "REQ-FN-070 CommandFilesNameThePackagesAndFeatures")]
    [InlineData(ClaudeCommand)]
    [InlineData(OpenCodeCommand)]
    public void CommandFilesNameThePackagesAndFeatures(string path)
    {
        var command = LibraryRepoFiles.Read(path);

        foreach (var package in Packages)
        {
            Assert.Contains(package, command, StringComparison.Ordinal);
        }

        foreach (var feature in new[] { "add-local-model", "add-agents", "add-streaming-events", "add-subscription-signin", "add-connectors" })
        {
            Assert.Contains(feature, command, StringComparison.Ordinal);
        }

        foreach (var member in new[] { "UseLocalLlm", "TechieRagAgentBuilder", "AddTechieRagAgent", "UseChatGptSubscriptionLlm", "ChatStreamEventsAsync", "RunStreamAsync", "IngestSiteAsync" })
        {
            Assert.Contains(member, command, StringComparison.Ordinal);
        }
    }
}
