using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-021 — <c>@agentname</c> typed in the workspace composer routes the turn through that
/// agent. These pin the parser that decides whether a turn is rerouted at all, which is the part
/// that must never fire by accident: a silently rerouted turn is invisible to the person who typed
/// it.
/// </summary>
public class AgentMentionTests
{
    /// <summary>
    /// A bare handle with no following text is still an invocation — "@analyst" on its own asks the
    /// agent to take the conversation from here.
    /// </summary>
    [Fact]
    public void BareHandleIsAnInvocation()
    {
        var mention = AgentMentionParser.Parse("@analyst");

        Assert.NotNull(mention);
        Assert.Equal("analyst", mention!.Handle);
        Assert.Equal(string.Empty, mention.Message);
    }

    /// <summary>
    /// The handle is stripped from the message that reaches the model, so the agent is asked the
    /// question rather than being asked about its own name.
    /// </summary>
    [Fact]
    public void HandleIsRemovedFromTheMessage()
    {
        var mention = AgentMentionParser.Parse("@analyst compare the Acme and Globex liability caps");

        Assert.Equal("analyst", mention!.Handle);
        Assert.Equal("compare the Acme and Globex liability caps", mention.Message);
    }

    /// <summary>
    /// Handles are matched case-insensitively and normalized to lowercase, so @Analyst and @analyst
    /// reach the same agent instead of one of them silently missing.
    /// </summary>
    [Fact]
    public void HandleIsNormalizedToLowercase()
    {
        var mention = AgentMentionParser.Parse("  @Analyst  summarise this  ");

        Assert.Equal("analyst", mention!.Handle);
        Assert.Equal("summarise this", mention.Message);
    }

    /// <summary>
    /// An email address is not an invocation. This is the case that decides whether the feature is
    /// safe to leave on: "email sales@acme.com about the renewal" must reach the normal chat path.
    /// </summary>
    [Fact]
    public void EmailAddressIsNotAMention()
    {
        Assert.Null(AgentMentionParser.Parse("email sales@acme.com about the renewal"));
    }

    /// <summary>
    /// A mention is only recognised as the FIRST token. An "@" later in the sentence describes
    /// something; it does not reroute the turn.
    /// </summary>
    [Fact]
    public void MentionMustLeadTheMessage()
    {
        Assert.Null(AgentMentionParser.Parse("ask @analyst about this"));
    }

    /// <summary>
    /// The handle token must end at whitespace, so a hostname or a dotted identifier is not
    /// mistaken for an agent whose name happens to be its first label.
    /// </summary>
    [Fact]
    public void HandleMustEndAtWhitespace()
    {
        Assert.Null(AgentMentionParser.Parse("@analyst.com is the vendor site"));
    }

    /// <summary>A lone '@' or empty text carries no handle and must not throw.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    [InlineData("@ analyst")]
    public void DegenerateInputIsNotAMention(string? text)
    {
        Assert.Null(AgentMentionParser.Parse(text));
    }

    /// <summary>Normalizing accepts the handle with or without its leading '@'.</summary>
    [Theory]
    [InlineData("@Analyst", "analyst")]
    [InlineData(" analyst ", "analyst")]
    [InlineData("renewal-watcher", "renewal-watcher")]
    [InlineData(null, "")]
    public void NormalizeStripsTheAtSignAndCase(string? input, string expected)
    {
        Assert.Equal(expected, AgentMentionParser.Normalize(input));
    }

    /// <summary>
    /// Handle validation rejects what the storage and the parser could not round-trip — spaces,
    /// punctuation and over-length names — so the editor refuses them before the database does.
    /// </summary>
    [Theory]
    [InlineData("analyst", true)]
    [InlineData("renewal-watcher", true)]
    [InlineData("agent2", true)]
    [InlineData("contract analyst", false)]
    [InlineData("analyst!", false)]
    [InlineData("", false)]
    public void HandleValidationMatchesWhatTheParserCanRead(string handle, bool expected)
    {
        Assert.Equal(expected, AgentMentionParser.IsValidHandle(handle));
    }

    /// <summary>
    /// A handle over the length ceiling is rejected rather than silently truncated into a different
    /// agent's handle.
    /// </summary>
    [Fact]
    public void OverlongHandleIsRejected()
    {
        Assert.False(AgentMentionParser.IsValidHandle(new string('a', AgentMentionParser.MaxHandleLength + 1)));
        Assert.True(AgentMentionParser.IsValidHandle(new string('a', AgentMentionParser.MaxHandleLength)));
    }

    /// <summary>
    /// Creating "Contract Analyst" suggests a handle that actually parses, so the new-agent dialog
    /// does not hand the user a name chat cannot invoke.
    /// </summary>
    [Fact]
    public void SuggestedHandleRoundTripsThroughTheParser()
    {
        var suggested = AgentMentionParser.SuggestHandle("Contract Analyst (2026)");

        Assert.Equal("contract-analyst-2026", suggested);
        Assert.True(AgentMentionParser.IsValidHandle(suggested));
        Assert.Equal(suggested, AgentMentionParser.Parse($"@{suggested} hello")!.Handle);
    }
}
