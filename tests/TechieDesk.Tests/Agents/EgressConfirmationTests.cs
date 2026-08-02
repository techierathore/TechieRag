using System.Net;
using System.Text;
using TechieDesk.Services.Agents;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Web;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-NFR-013 / REQ-NFR-008 — <see cref="AgentDefinition.ConfirmEgress"/> is READ by the execution
/// path. With it on, a skill that leaves the machine blocks on an explicit confirmation before the
/// request is made; declining cancels that call without ending the turn; with it off the agent
/// proceeds silently.
/// </summary>
/// <remarks>
/// <para><b>What these tests are guarding against.</b> The switch existed, defaulted ON, was
/// persisted and was labelled "Ask before any skill that leaves this machine" — and nothing read it.
/// A test that asserted the FLAG'S VALUE would have passed against that defect and proven nothing.
/// So the load-bearing assertions here are counted at the
/// <see cref="HttpMessageHandler"/>: the real shipped <see cref="HttpWebContentFetcher"/> is wired
/// over a counting handler, and while a confirmation is pending the count is zero.</para>
/// <para><b>Exposure, not names.</b>
/// <see cref="EveryCatalogueSkillThatLeavesTheMachineIsGated"/> walks the catalogue rather than a
/// list written here, so a new egress-capable skill inherits the gate or fails this file.</para>
/// </remarks>
public sealed class EgressConfirmationTests
{
    private const string PageUrl = "https://contracts.example.com/renewals";

    private static readonly string ScrapeArguments = $$"""{"url":"{{PageUrl}}"}""";

    /// <summary>
    /// THE clause-5 proof. With confirmation required and the prompt still unanswered, the shipped
    /// fetcher has issued NO outbound request — asserted by counting at the message handler, not by
    /// reading a flag. Answering then releases exactly one request.
    /// </summary>
    [Fact]
    public async Task NoRequestIsIssuedWhileConfirmationIsPending()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation();
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        var execution = registry.ExecuteToolAsync(Scrape());
        await confirmation.Asked.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, handler.RequestCount);
        Assert.False(execution.IsCompleted);

        confirmation.Answer(isAllowed: true);
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, handler.RequestCount);
        Assert.False(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>Declining sends nothing at all — the request count never leaves zero.</summary>
    [Fact]
    public async Task DecliningIssuesNoRequest()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation(isAllowed: false);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        await registry.ExecuteToolAsync(Scrape());

        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(handler.Requested);
    }

    /// <summary>
    /// Clause 2: a declined call is reported to the model as unavailable rather than thrown, so the
    /// loop records it and carries on — the tool result is a success-shaped unavailability, not a
    /// fault that would end the turn.
    /// </summary>
    [Fact]
    public async Task DecliningReportsTheSkillUnavailableWithoutFaultingTheCall()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), new PendingConfirmation(isAllowed: false));

        var result = await registry.ExecuteToolAsync(Scrape());

        Assert.True(result.IsSuccess);
        Assert.True(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>
    /// Clause 2 continued: after a decline the turn is still usable. The local skill in the same
    /// registry runs normally, so refusing egress costs the outbound call and nothing else.
    /// </summary>
    [Fact]
    public async Task TheTurnContinuesAfterADecline()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), new PendingConfirmation(isAllowed: false));

        await registry.ExecuteToolAsync(Scrape());
        var local = await registry.ExecuteToolAsync(new ToolCall
        {
            Id = "2",
            Name = SkillCatalog.RagSearch,
            ArgumentsJson = """{"query":"renewal window"}"""
        });

        Assert.True(local.IsSuccess);
        Assert.Equal("hits", local.Content);
    }

    /// <summary>
    /// Clause 3: with the switch off the agent proceeds silently. The confirmer is never asked and
    /// the request goes out — which is the entire purpose of being able to turn the setting off.
    /// </summary>
    [Fact]
    public async Task ConfirmationOffProceedsSilently()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation();
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: false), confirmation);

        var result = await registry.ExecuteToolAsync(Scrape());

        Assert.Equal(0, confirmation.AskCount);
        Assert.Equal(1, handler.RequestCount);
        Assert.False(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>
    /// Confirmed once per TURN, not once per call. Two egress calls inside one turn raise one
    /// prompt; re-asking on every call is how a privacy prompt becomes something users click past.
    /// </summary>
    [Fact]
    public async Task TheUserIsAskedOnlyOncePerTurn()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation(isAllowed: true);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        await registry.ExecuteToolAsync(Scrape());
        await registry.ExecuteToolAsync(Scrape("https://contracts.example.com/terms"));

        Assert.Equal(1, confirmation.AskCount);
        Assert.Equal(2, handler.RequestCount);
    }

    /// <summary>A decline is remembered for the turn too — the second call is refused in silence.</summary>
    [Fact]
    public async Task ADeclineIsRememberedForTheRestOfTheTurn()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation(isAllowed: false);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        await registry.ExecuteToolAsync(Scrape());
        var second = await registry.ExecuteToolAsync(Scrape("https://contracts.example.com/terms"));

        Assert.Equal(1, confirmation.AskCount);
        Assert.Equal(0, handler.RequestCount);
        Assert.True(SkillUnavailable.IsUnavailable(second.Content), second.Content);
    }

    /// <summary>
    /// Fail closed. A host that registers no confirmer cannot ask, so egress is refused rather than
    /// waved through — a missing implementation must never read as consent.
    /// </summary>
    [Fact]
    public async Task AMissingConfirmerDeniesEgress()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation: null);

        var result = await registry.ExecuteToolAsync(Scrape());

        Assert.Equal(0, handler.RequestCount);
        Assert.True(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>
    /// Clause 4: the PER-AGENT value governs. A named agent whose editor left the switch on gates,
    /// and the built-in agent is not the only thing consulted.
    /// </summary>
    [Fact]
    public async Task ThePerAgentEditorValueIsHonoured()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation(isAllowed: false);
        var named = Analyst(confirmEgress: true);

        Assert.False(named.IsBuiltIn);

        var registry = ComposeTurn(httpClient, named, confirmation);
        await registry.ExecuteToolAsync(Scrape());

        Assert.Equal(1, confirmation.AskCount);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>The built-in agent's own value governs its turns, by the same single code path.</summary>
    [Fact]
    public void TheBuiltInAgentValueIsHonoured()
    {
        var builtIn = AgentDefinition.BuiltIn("ws-contracts", DateTime.UtcNow);

        Assert.True(new EgressGate(builtIn, new PendingConfirmation()).IsConfirmationRequired);

        builtIn.ConfirmEgress = false;
        Assert.False(new EgressGate(builtIn, new PendingConfirmation()).IsConfirmationRequired);
    }

    /// <summary>
    /// The gated skill stays REGISTERED and visible to the model. Gating at registry-build time
    /// would silently shrink the tool set and make "declined" indistinguishable from "never existed".
    /// </summary>
    [Fact]
    public void AGatedSkillIsStillRegistered()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), new PendingConfirmation());

        Assert.Contains(registry.ToolDefinitions, definition => definition.Name == SkillCatalog.WebScrape);
        Assert.Contains(registry.ToolDefinitions, definition => definition.Name == SkillCatalog.WebSearch);
    }

    /// <summary>
    /// The gate is keyed off <see cref="SkillExposure.LeavesMachine"/>, walking the catalogue rather
    /// than a list written here — so a new egress-capable skill inherits the gate automatically, and
    /// one added without the right exposure fails this test instead of shipping ungated.
    /// </summary>
    [Fact]
    public void EveryCatalogueSkillThatLeavesTheMachineIsGated()
    {
        var egressSkills = SkillCatalog.Skills
            .Where(skill => skill.Exposure == SkillExposure.LeavesMachine)
            .Select(skill => skill.Name)
            .ToArray();

        Assert.NotEmpty(egressSkills);
        Assert.All(egressSkills, name => Assert.True(EgressGate.LeavesMachine(name), name));
    }

    /// <summary>A local skill is never gated: nothing leaves the machine, so nothing is asked.</summary>
    [Fact]
    public async Task LocalSkillsAreNeverGated()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation(isAllowed: false);
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        var result = await registry.ExecuteToolAsync(new ToolCall
        {
            Id = "1",
            Name = SkillCatalog.RagSearch,
            ArgumentsJson = """{"query":"renewal window"}"""
        });

        Assert.Equal(0, confirmation.AskCount);
        Assert.Equal("hits", result.Content);
    }

    /// <summary>
    /// The gate leaves a local skill's implementation object untouched, so it cannot change what a
    /// local tool does — only egress skills are rewritten.
    /// </summary>
    [Fact]
    public void GuardRewritesOnlyTheEgressSkills()
    {
        var gate = new EgressGate(Analyst(confirmEgress: true), new PendingConfirmation());
        var original = WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None);

        var guarded = gate.Guard(original);

        Assert.Equal(original.Count, guarded.Count);
        for (var index = 0; index < original.Count; index++)
        {
            var isRewritten = !ReferenceEquals(original[index], guarded[index]);
            Assert.Equal(EgressGate.LeavesMachine(original[index].SkillName), isRewritten);
        }
    }

    /// <summary>
    /// A cancelled prompt — the turn's time limit expiring while the dialog is open — is a decline,
    /// and still nothing goes out.
    /// </summary>
    [Fact]
    public async Task ACancelledPromptDeniesEgress()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var confirmation = new PendingConfirmation();
        var registry = ComposeTurn(httpClient, Analyst(confirmEgress: true), confirmation);

        using var timeout = new CancellationTokenSource();
        var execution = registry.ExecuteToolAsync(Scrape(), timeout.Token);
        await confirmation.Asked.WaitAsync(TimeSpan.FromSeconds(10));
        await timeout.CancelAsync();

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, handler.RequestCount);
        Assert.True(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>Builds a web-scrape tool call.</summary>
    /// <param name="url">The page to ask for; the default is used when omitted.</param>
    /// <returns>The tool call the model would have produced.</returns>
    private static ToolCall Scrape(string? url = null) => new()
    {
        Id = "1",
        Name = SkillCatalog.WebScrape,
        ArgumentsJson = url is null ? ScrapeArguments : $$"""{"url":"{{url}}"}"""
    };

    /// <summary>A named (not built-in) agent, as the per-agent editor would have saved it.</summary>
    /// <param name="confirmEgress">The Guardrails-tab switch value.</param>
    /// <returns>The agent definition.</returns>
    private static AgentDefinition Analyst(bool confirmEgress) => new()
    {
        WorkspaceId = "ws-contracts",
        Handle = "analyst",
        DisplayName = "Contract Analyst",
        UsesEveryEnabledSkill = true,
        ConfirmEgress = confirmEgress
    };

    /// <summary>
    /// Composes one agent turn exactly as the chat page does — the catalogue narrowed to the
    /// permitted set, the gate wrapping the implementations, and the SHIPPED
    /// <see cref="HttpWebContentFetcher"/> over a counting handler so egress is measurable.
    /// </summary>
    /// <param name="httpClient">The client whose handler counts outbound requests.</param>
    /// <param name="agent">The agent whose <see cref="AgentDefinition.ConfirmEgress"/> governs.</param>
    /// <param name="confirmation">How the turn asks the user, or null for a host that cannot ask.</param>
    /// <returns>The registry the agent loop would be handed.</returns>
    private static ToolRegistry ComposeTurn(
        HttpClient httpClient, AgentDefinition agent, IEgressConfirmation? confirmation)
    {
        var options = new WorkspaceSkillOptions
        {
            // The SSRF guard is off only so the stub host needs no DNS; the egress path under test
            // is the real one.
            WebFetcher = new HttpWebContentFetcher(httpClient, logger: null, blockPrivateTargets: false),
            WebSearch = null,
            SqlTarget = null,
            Files = null
        };

        var gate = new EgressGate(agent, confirmation);
        var catalogue = SkillCatalog.Skills.ToDictionary(
            skill => skill.Name, _ => true, StringComparer.OrdinalIgnoreCase);

        return AgentToolPlanner.BuildRegistry(
            AgentSkillResolver.Permitted(catalogue, agent),
            gate.Guard(
            [
                WorkspaceSkillTools.RagSearch((_, _) => Task.FromResult("hits")),
                .. WorkspaceSkillTools.Standard(options)
            ]));
    }

    /// <summary>
    /// Counts every outbound request at the transport, which is the only place "nothing left the
    /// machine" can be asserted rather than assumed.
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private int requestCount;

        /// <summary>Gets how many requests reached the transport.</summary>
        public int RequestCount => Volatile.Read(ref requestCount);

        /// <summary>Gets the URLs that were actually requested.</summary>
        public List<string> Requested { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            lock (Requested)
            {
                Requested.Add(request.RequestUri!.ToString());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><title>Renewals</title></head><body><p>Renews annually.</p></body></html>",
                    Encoding.UTF8,
                    "text/html")
            });
        }
    }

    /// <summary>
    /// A confirmer the test drives: it reports when it was asked and answers only when told to, so
    /// "while a confirmation is pending" is a state the test can actually observe.
    /// </summary>
    /// <param name="isAllowed">
    /// The answer to give immediately, or null to leave the prompt pending until
    /// <see cref="Answer"/> is called.
    /// </param>
    private sealed class PendingConfirmation(bool? isAllowed = null) : IEgressConfirmation
    {
        private readonly TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets how many times the user was prompted.</summary>
        public int AskCount { get; private set; }

        /// <summary>Gets a task that completes when the prompt has been raised.</summary>
        public Task Asked => asked.Task;

        /// <inheritdoc />
        public Task<bool> ConfirmAsync(EgressConfirmationRequest request, CancellationToken cancellationToken)
        {
            AskCount++;
            asked.TrySetResult();

            if (isAllowed is not null)
            {
                answer.TrySetResult(isAllowed.Value);
            }

            return answer.Task.WaitAsync(cancellationToken);
        }

        /// <summary>Answers a pending prompt.</summary>
        /// <param name="isAllowed">True to allow the turn's outbound requests.</param>
        public void Answer(bool isAllowed) => answer.TrySetResult(isAllowed);
    }
}
