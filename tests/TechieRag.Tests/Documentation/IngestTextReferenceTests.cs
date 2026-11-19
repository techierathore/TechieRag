using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Tests.Documentation;

/// <summary>
/// REQ-RAG-171 (TrStudio TR-RAG-002): the AI reference shows <c>IngestTextAsync</c> with the metadata type it ships
/// with, <c>Dictionary&lt;string, object&gt;</c>.
/// </summary>
/// <remarks>
/// TrStudio followed the reference's <c>IngestTextAsync(text, documentId, Dictionary&lt;string, string&gt;? metadata)</c>
/// and got a compile error: the shipped method takes <c>Dictionary&lt;string, object&gt;?</c>, returns the document id as
/// <c>Task&lt;string&gt;</c>, and its second argument is the document's name. The same block also had
/// <c>IngestAsync</c> and <c>IngestDirectoryAsync</c> returning <c>IngestionStats</c>.
/// </remarks>
public sealed class IngestTextReferenceTests
{
    /// <summary>
    /// Every <c>IngestTextAsync</c> declaration in both copies of the reference takes
    /// <c>Dictionary&lt;string, object&gt;? metadata</c> and returns <c>Task&lt;string&gt;</c>, which is what the shipped
    /// interface declares; none shows the string-valued dictionary.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-171 ReferenceShowsTheShippedIngestTextSignature")]
    public void ReferenceShowsTheShippedIngestTextSignature()
    {
        var shipped = typeof(ITechieRag).GetMethods().Where(method => method.Name == nameof(ITechieRag.IngestTextAsync)).ToList();
        Assert.All(shipped, method =>
        {
            Assert.Equal(typeof(Task<string>), method.ReturnType);
            Assert.Equal(typeof(Dictionary<string, object>), method.GetParameters().Single(parameter => parameter.Name == "metadata").ParameterType);
        });

        foreach (var file in new[] { "docs/TechieRag-AI-Reference.md", "src/TechieRag/build/content/TechieRag-AI-Reference.md" })
        {
            var text = LibraryRepoFiles.Read(file);
            // A declaration that elides its arguments as "(...)" (the workspace manager's summary line) states no type.
            var declarations = Regex.Matches(text, @"Task<[^\n]*> IngestTextAsync\([^;]*;")
                .Select(match => match.Value)
                .Where(declaration => !declaration.Contains("IngestTextAsync(...)", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(declarations);
            Assert.All(declarations, declaration =>
            {
                Assert.StartsWith("Task<string> IngestTextAsync(", declaration);
                Assert.Contains("Dictionary<string, object>? metadata", declaration);
            });
            Assert.DoesNotContain("Dictionary<string, string>? metadata", text);
            Assert.DoesNotContain("Task<IngestionStats> IngestAsync(", text);
            Assert.DoesNotContain("Task<IngestionStats> IngestDirectoryAsync(", text);
        }
    }
}
