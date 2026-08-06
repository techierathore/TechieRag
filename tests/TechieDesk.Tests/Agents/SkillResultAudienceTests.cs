using TechieDesk.Services.Agents;
using TechieDesk.Services.Agents.Mcp;
using TechieDesk.Services.Flows;
using TechieDesk.Tests.Support;
using TechieRag.Abstractions;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-UI-055 (BRD-91) — the agent skill layer produces text for TWO audiences, and which audience a
/// string has decides whether it is translated.
/// </summary>
/// <remarks>
/// <para>
/// <b>The split, and where it comes from.</b> It is not a judgement call, it is a line in
/// <c>AgentLoopRunner</c>: <c>messages.Add(ChatMessage.Tool(result.ToolCallId, result.Content))</c>.
/// <see cref="ToolResult.Content"/> — and a skill's returned string, which becomes it — enters the
/// conversation the model reasons over. <see cref="ToolResult.ErrorMessage"/> does not; it reaches
/// <c>AgentStep.ErrorMessage</c>, which <c>AgentTracePanel</c> paints as the failed step's detail row
/// and nothing else reads. So Content is machine-facing and stays English, ErrorMessage is
/// user-facing and is translated.
/// </para>
/// <para>
/// <b>Why translating the model's half would be a defect, not a feature.</b> A Hindi tool
/// description or a Hindi refusal sits in an otherwise-English context window; it degrades tool
/// selection and it invites the model to answer the user in whichever language it last read. It
/// would also break the execution trace's one promise — the trace shows a tool result verbatim, so a
/// translated result would make the trace a record of something the model was never told.
/// </para>
/// <para>
/// <b>What these tests are NOT.</b> They do not count literals; <c>ServiceStringCoverage</c> does
/// that and its own remarks already say it cannot tell a tool description from a label. These pin
/// the AUDIENCE of each string by running the real code in two cultures through the real localizer,
/// which is the only way the distinction can be asserted rather than asserted about.
/// </para>
/// </remarks>
public sealed class SkillResultAudienceTests
{
    private const string GatedServer = "acme";

    private static readonly string GatedTool = GatedServer + "-lookup";

    /// <summary>
    /// The gate quotes the switch from the SAME resource entry the Guardrails tab binds, so the
    /// control's label and the sentence telling the reader to change it cannot drift apart.
    /// </summary>
    /// <remarks>
    /// The literal used to be typed into <c>EgressGate</c> a second time. REQ-NFR-013 exists because
    /// that control's promise and its behaviour disagreed once; two copies of its NAME is the same
    /// failure waiting to happen the first time somebody rewords the switch.
    /// </remarks>
    [Fact]
    public async Task TheEgressRefusalQuotesTheSwitchLabelFromTheResources()
    {
        using var resources = new ResourceHarness("en");
        var label = resources.Require(EgressWording.ConfirmEgressSettingKey);

        var refusal = await DeniedSkillResultAsync();

        Assert.Equal(label, EgressWording.InEnglish(EgressWording.ConfirmEgressSettingKey));
        Assert.Contains(label, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal the MODEL reads is byte-identical in every culture, quoted switch label and all.
    /// </summary>
    /// <remarks>
    /// The one assertion that would have caught a well-meaning "localize the egress refusal" change:
    /// the quote is resolved from the resources, so it would have started arriving in Devanagari on a
    /// Hindi install without a single literal changing.
    /// </remarks>
    [Fact]
    public async Task TheEgressRefusalIsTheSameBytesInEveryCulture()
    {
        string english;
        using (new ResourceHarness("en"))
        {
            english = await DeniedSkillResultAsync();
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, await DeniedSkillResultAsync());
        }

        Assert.True(SkillUnavailable.IsUnavailable(english), english);
    }

    /// <summary>
    /// A declined MCP call splits: the model's payload stays English, the trace row is translated.
    /// </summary>
    /// <param name="culture">The culture to run the declined call in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public async Task TheMcpTraceRowIsTranslatedAndTheModelPayloadIsNot(string culture)
    {
        string englishContent;
        using (new ResourceHarness("en"))
        {
            englishContent = (await DeclinedMcpCallAsync()).Content;
        }

        using var resources = new ResourceHarness(culture);
        var result = await DeclinedMcpCallAsync();

        // Content is what AgentLoopRunner hands the model. It must not move with the culture.
        Assert.Equal(englishContent, result.Content);

        // ErrorMessage is what the trace panel paints, and nothing else consumes it.
        Assert.Equal(resources.Require(EgressWording.McpCallNotApprovedKey), result.ErrorMessage);
    }

    /// <summary>
    /// The two trace rows a reader could see actually differ between the shipped languages, so the
    /// test above is comparing something real rather than an untranslated key in both.
    /// </summary>
    [Fact]
    public async Task TheMcpTraceRowIsActuallyTranslated()
    {
        string english;
        using (new ResourceHarness("en"))
        {
            english = (await DeclinedMcpCallAsync()).ErrorMessage ?? string.Empty;
        }

        using (new ResourceHarness("hi"))
        {
            var hindi = (await DeclinedMcpCallAsync()).ErrorMessage ?? string.Empty;

            Assert.NotEqual(english, hindi);
            Assert.Contains(hindi, character => character is >= '\u0900' and <= '\u097F');
        }
    }

    /// <summary>
    /// Every guardrail whose refusal reaches the flows screen publishes a key that resolves in both
    /// shipped languages, from the culture's OWN resource file.
    /// </summary>
    /// <param name="culture">The culture to resolve in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryGuardrailBlockReasonKeyResolvesInBothCultures(string culture)
    {
        using var resources = new ResourceHarness(culture);

        string[] ids =
        [
            FlowGuardrailCatalog.NonEmptyOutputId,
            FlowGuardrailCatalog.NoCredentialsInOutputId
        ];

        foreach (var id in ids)
        {
            var key = FlowGuardrailCatalog.BlockReasonKey(id);
            Assert.NotNull(key);

            // The culture's own key set, not merely "it resolved": a key present in English and
            // missing from Hindi resolves to the ENGLISH text with ResourceNotFound false.
            Assert.Contains(key, resources.OwnKeys);

            var value = resources.Require(key!);
            Assert.NotEqual(key, value);
            Assert.False(string.IsNullOrWhiteSpace(value));
        }
    }

    /// <summary>
    /// The tool-call-stage guardrail has NO translated reason, because its reason is model-facing.
    /// </summary>
    /// <remarks>
    /// <c>local-tools-only</c> refuses at <c>GuardrailStage.ToolCall</c>, which does not stop the run
    /// and therefore never becomes <c>FlowRunResult.BlockReason</c> — the value the flows screen
    /// paints. Its reason goes back to the model as an <c>unavailable:</c> tool result instead.
    /// Shipping a translation for it would be a string nothing can render.
    /// </remarks>
    [Fact]
    public void TheToolCallStageGuardrailHasNoTranslatedReason()
    {
        Assert.Null(FlowGuardrailCatalog.BlockReasonKey(FlowGuardrailCatalog.LocalToolsOnlyId));
        Assert.Null(FlowGuardrailCatalog.BlockReasonKey("something-a-host-registered"));
        Assert.Null(FlowGuardrailCatalog.BlockReasonKey(null));
    }

    /// <summary>
    /// Everything the model reads from the skill layer is byte-identical in every culture: the tool
    /// descriptions it selects on, the schemas it fills in, and the results it reasons over.
    /// </summary>
    /// <remarks>
    /// The broad guard, and the one that catches the NEXT cluster. It drives the shipped factories
    /// rather than a list written here, so a seventh skill is covered the day it is added. The calls
    /// are the unconfigured ones on purpose — a stock install is where every one of these tools
    /// answers with prose rather than data, which is exactly the prose somebody would think to
    /// translate.
    /// </remarks>
    [Fact]
    public async Task EveryModelFacingSkillStringIsTheSameInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = await ModelFacingSkillTextAsync();
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, await ModelFacingSkillTextAsync());
        }

        // A guard against the guard: an empty sweep would make the comparison vacuous.
        Assert.True(english.Length >= 20, $"Only {english.Length} model-facing strings were swept.");
    }

    /// <summary>
    /// The skill wire vocabulary the model is handed does not move with the culture either.
    /// </summary>
    /// <remarks>
    /// Precedent: <c>QdrantAdmin</c>'s daemon kind was a string that WAS its English label and was
    /// parsed back to build the endpoint. A tool name is the same shape of value — it is handed to
    /// the model, matched on the way back, and stored in the per-workspace toggle tables.
    /// </remarks>
    [Fact]
    public void SkillNamesAreTheSameInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = [.. WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None).Select(s => s.SkillName)];
        }

        using (new ResourceHarness("hi"))
        {
            Assert.Equal(english, WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None)
                .Select(skill => skill.SkillName));
        }

        Assert.Contains(SkillCatalog.WebSearch, english);
    }

    /// <summary>Collects every string the skill layer sends to the model on a stock install.</summary>
    /// <returns>Descriptions, schemas and unconfigured results, in a stable order.</returns>
    private static async Task<string[]> ModelFacingSkillTextAsync()
    {
        var collected = new List<string>();

        foreach (var skill in WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None))
        {
            collected.Add(skill.Description);
            collected.Add(skill.ParametersSchema);
            collected.Add(await skill.Invoke("{}", CancellationToken.None));
        }

        // The guard's refusals and the sandbox's, which are the other two things a model reads back.
        collected.Add(SqlQueryGuard.Refuse(null) ?? string.Empty);
        collected.Add(SqlQueryGuard.Refuse("SELECT 1; DROP TABLE Documents") ?? string.Empty);
        collected.Add(SqlQueryGuard.Refuse("SELECT 1 -- comment") ?? string.Empty);
        collected.Add(SqlQueryGuard.Refuse("DELETE FROM Documents") ?? string.Empty);

        var sandbox = new FileOperationsSandbox(Path.Combine(Path.GetTempPath(), "techiedesk-audience"));
        collected.Add(sandbox.Resolve("../escape.txt", mustBeAllowedFile: true, out _) ?? string.Empty);
        collected.Add(sandbox.Resolve("notes.exe", mustBeAllowedFile: true, out _) ?? string.Empty);
        collected.Add(sandbox.Resolve(string.Empty, mustBeAllowedFile: true, out _) ?? string.Empty);

        // The model-facing half of the flow guardrail that refuses at tool-call stage.
        var localToolsOnly = await new FlowGuardrailCatalog()
            .ResolveGuardrailAsync(FlowGuardrailCatalog.LocalToolsOnlyId);
        collected.Add(localToolsOnly!.Description);

        return [.. collected];
    }

    /// <summary>Runs an egress skill through a gate that cannot ask, and returns the refusal.</summary>
    /// <returns>The string the model would receive.</returns>
    private static async Task<string> DeniedSkillResultAsync()
    {
        var gate = new EgressGate(GuardedAgent(), confirmation: null);
        var guarded = gate.Guard(WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None));
        var search = guarded.Single(skill => skill.SkillName == SkillCatalog.WebSearch);

        return await search.Invoke("""{"query":"anything"}""", CancellationToken.None);
    }

    /// <summary>Runs an HTTP-server MCP tool through a gate that cannot ask.</summary>
    /// <returns>The tool result the loop would report.</returns>
    private static async Task<ToolResult> DeclinedMcpCallAsync()
    {
        var guard = new McpEgressGuard(
            new StubToolHandler(), [GatedServer], new EgressGate(GuardedAgent(), confirmation: null));

        return await guard.ExecuteToolAsync(new ToolCall
        {
            Id = "1",
            Name = GatedTool,
            ArgumentsJson = "{}"
        });
    }

    /// <summary>An agent whose Guardrails-tab switch is on.</summary>
    /// <returns>The agent definition.</returns>
    private static AgentDefinition GuardedAgent() => new()
    {
        WorkspaceId = "ws-audience",
        Handle = "analyst",
        DisplayName = "Contract Analyst",
        UsesEveryEnabledSkill = true,
        ConfirmEgress = true
    };

    /// <summary>A handler that would answer, so a refusal can only come from the guard.</summary>
    private sealed class StubToolHandler : IToolHandler
    {
        /// <inheritdoc />
        public IReadOnlyList<ToolDefinition> ToolDefinitions =>
        [
            new()
            {
                Name = GatedTool,
                Description = "Looks something up on the acme service.",
                ParametersSchema = """{"type":"object","properties":{}}"""
            }
        ];

        /// <inheritdoc />
        public Task<ToolResult> ExecuteToolAsync(
            ToolCall toolCall, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = "the server answered",
                IsSuccess = true
            });
    }
}
