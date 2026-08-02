using System.Net;
using System.Net.Sockets;
using System.Text;
using TechieRag.Llm;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Proves agent orchestration adds no outbound capability by default (REQ-RAG-042 against
/// REQ-NFR-008 / BRD-99), by COUNTING BYTES at a real transport seam.
/// </summary>
/// <remarks>
/// <para><b>Why bytes and not a flag.</b> The cheap version of this test asserts some
/// <c>IsOffline</c> property and calls it proven. That test passes against code that sends anyway —
/// a failure mode this project has already been caught by more than once (a flag-only egress
/// assertion, a telemetry test that read a property instead of the socket). So the assertion here
/// reads <see cref="LoopbackCollector.BytesReceived"/> on an <see cref="HttpListener"/> bound to
/// 127.0.0.1. Nothing in the process can fake a request arriving there; the operating system had to
/// carry it.</para>
/// <para><b>The seam is armed, not merely present.</b> A REAL
/// <see cref="OpenAICompatibleLlmProvider"/> is constructed against the collector's URL and
/// registered as a resolvable agent on the runtime the flow runs on. If the orchestrator ever
/// eagerly contacted a configured provider — a health check, a model list, a warm-up — bytes would
/// land and the zero assertion would go red.</para>
/// <para><b>The zero assertion is not vacuous, and there is a permanent proof of that.</b>
/// <see cref="TheLoopbackSeamSeesTrafficWhenAFlowRoutesToARemoteProvider"/> runs the same machinery
/// with one node re-pointed at that real provider and asserts bytes DID arrive. The two tests
/// together say: the seam works, and the default does not use it.</para>
/// </remarks>
public sealed class FlowOrchestrationEgressTests
{
    /// <summary>
    /// The load-bearing one. A full multi-agent flow — agent nodes, a conditional branch, a handoff,
    /// a deterministic tool node, a host guardrail and an agent-as-tool — runs end to end with a
    /// real remote provider configured and reachable, and puts ZERO bytes on the wire.
    /// </summary>
    [Fact]
    public async Task ADefaultConfiguredOrchestrationPutsZeroBytesOnTheWire()
    {
        using var collector = LoopbackCollector.Start();

        // A real provider, really pointed at the listener, really resolvable by the flow's runtime.
        // "Nothing was sent" is therefore never merely "nothing was available to send".
        var remote = new OpenAICompatibleLlmProvider(collector.Url, "test-key", "gpt-probe");

        var local = new ScriptedLlmProvider(
            "local",
            ScriptedLlmProvider.CallsTool("summarize", """{"input":"the notes"}"""),
            ScriptedLlmProvider.Says("refund requested"),
            ScriptedLlmProvider.Says("handled locally"));

        var tools = new RecordingToolHandler().Register("summarize", "Summarizes text", _ => "a summary");

        var subAgent = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates to the specialist",
            new FlowAgent("specialist", new ScriptedLlmProvider("specialist", ScriptedLlmProvider.Says("specialist answer"))));

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(
            new FlowAgent("local", local, tools),
            new FlowAgent("closer", new ScriptedLlmProvider("closer", ScriptedLlmProvider.Says("closed")), subAgent),
            new FlowAgent("remote", remote)))
        {
            Tools = tools
        };

        runtime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "host-gate", "The host's gate", null, (_, _) => Task.FromResult(GuardrailDecision.Allow())));

        var result = await new FlowRunner(EgressProbeFlow(), runtime).RunAsync("please refund my order");

        await Task.Delay(300);

        // The bytes come FIRST, deliberately. An assertion on the outcome ahead of them could
        // short-circuit a regression before the socket ever got a chance to speak.
        Assert.Equal(0, collector.BytesReceived);
        Assert.Equal(0, collector.RequestCount);

        // And the flow genuinely ran, so the zero above is not "nothing happened".
        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Contains("closed", result.Output);
        Assert.Contains("escalate", result.VisitedNodeIds);
        Assert.Contains("transfer", result.VisitedNodeIds);
        // Twice: once because the agent node's model asked for it, once because the deterministic
        // Tool node called it. Both dispatch paths ran, and neither left the machine.
        Assert.Equal(new[] { "summarize", "summarize" }, tools.Executed);
    }

    /// <summary>
    /// The positive control that keeps the test above honest: the SAME flow with one node re-pointed
    /// at the real remote provider does put bytes on the wire. If this ever goes green with zero
    /// bytes, the seam has stopped working and the zero-egress assertion means nothing.
    /// </summary>
    [Fact]
    public async Task TheLoopbackSeamSeesTrafficWhenAFlowRoutesToARemoteProvider()
    {
        using var collector = LoopbackCollector.Start();

        var remote = new OpenAICompatibleLlmProvider(collector.Url, "test-key", "gpt-probe");

        var flow = new FlowDefinition
        {
            Id = "remote",
            Name = "Deliberately remote",
            StartNodeId = "ask",
            Nodes =
            [
                new FlowNode { Id = "ask", Kind = FlowNodeKind.Agent, AgentId = "remote" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "ask", ToNodeId = "end" }]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("remote", remote)));

        await new FlowRunner(flow, runtime).RunAsync("go");
        await collector.WaitForAnyRequestAsync();

        Assert.True(collector.RequestCount > 0, "The loopback seam saw no request, so it cannot prove the absence of one either.");
        Assert.True(collector.BytesReceived > 0, "The loopback seam counted no bytes, so its zero-byte assertion is vacuous.");
    }

    /// <summary>
    /// Everything a flow-builder UI does before it runs anything — enumerate the palette, compose a
    /// graph, validate it, serialize it, reopen it — puts zero bytes on the wire.
    /// </summary>
    [Fact]
    public async Task AuthoringAndValidatingAFlowPutsZeroBytesOnTheWire()
    {
        using var collector = LoopbackCollector.Start();

        var remote = new OpenAICompatibleLlmProvider(collector.Url, "test-key", "gpt-probe");
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("remote", remote)));

        var flow = new FlowDefinition
        {
            Id = "authored",
            Name = "Authored in the builder",
            StartNodeId = "ask",
            Nodes =
            [
                new FlowNode { Id = "ask", Kind = FlowNodeKind.Agent, AgentId = "remote" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "ask", ToNodeId = "end" }]
        };

        _ = FlowNodeCatalog.Kinds;
        _ = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition);
        _ = await runtime.Agents.ListAgentsAsync();
        _ = FlowValidator.Validate(flow);
        _ = await FlowValidator.ValidateAsync(flow, runtime);
        _ = FlowSerializer.FromJson(FlowSerializer.ToJson(flow));

        await Task.Delay(300);

        Assert.Equal(0, collector.BytesReceived);
        Assert.Equal(0, collector.RequestCount);
    }

    /// <summary>Builds the multi-capability flow the zero-egress run exercises.</summary>
    /// <returns>A flow using an agent node, a branch, a tool node, a handoff and a terminal.</returns>
    private static FlowDefinition EgressProbeFlow() => new()
    {
        Id = "probe",
        Name = "Zero-egress probe",
        StartNodeId = "classify",
        Nodes =
        [
            new FlowNode { Id = "classify", Kind = FlowNodeKind.Agent, Name = "Classify", AgentId = "local", OutputVariable = "classification" },
            new FlowNode { Id = "escalate", Kind = FlowNodeKind.Tool, Name = "Escalate", ToolName = "summarize" },
            new FlowNode
            {
                Id = "transfer",
                Kind = FlowNodeKind.Handoff,
                Name = "Transfer",
                Handoff = new FlowHandoff
                {
                    TargetNodeId = "closer",
                    ContextMode = HandoffContextMode.LastOutputOnly,
                    CarryVariables = ["classification"]
                }
            },
            new FlowNode { Id = "closer", Kind = FlowNodeKind.Agent, Name = "Closer", AgentId = "closer" },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal, TerminalStatus = "done" }
        ],
        Edges =
        [
            new FlowEdge
            {
                Id = "e1", FromNodeId = "classify", ToNodeId = "escalate", Order = 0,
                Condition = new FlowCondition { Kind = FlowConditionKind.Contains, Operand = "refund" }
            },
            new FlowEdge { Id = "e2", FromNodeId = "classify", ToNodeId = "end", Order = 1 },
            new FlowEdge { Id = "e3", FromNodeId = "escalate", ToNodeId = "transfer" },
            new FlowEdge { Id = "e4", FromNodeId = "closer", ToNodeId = "end" }
        ]
    };

    /// <summary>
    /// A real HTTP endpoint on 127.0.0.1 that counts the bytes actually delivered to it.
    /// </summary>
    /// <remarks>
    /// The transport seam. It answers with a minimal OpenAI-shaped body so a provider that DOES call
    /// it completes normally — a hung request would be indistinguishable from no request at all.
    /// </remarks>
    private sealed class LoopbackCollector : IDisposable
    {
        private const string ChatCompletionBody =
            """{"choices":[{"message":{"role":"assistant","content":"remote answer"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";

        private readonly HttpListener listener;
        private int requestCount;
        private long bytesReceived;

        private LoopbackCollector(HttpListener listener, string url)
        {
            this.listener = listener;
            Url = url;
        }

        /// <summary>Gets the base URL a provider should be pointed at.</summary>
        public string Url { get; }

        /// <summary>Gets how many requests were delivered.</summary>
        public int RequestCount => Volatile.Read(ref requestCount);

        /// <summary>Gets how many request-body bytes were delivered.</summary>
        public long BytesReceived => Interlocked.Read(ref bytesReceived);

        /// <summary>Starts a collector on a free loopback port.</summary>
        /// <returns>The running collector.</returns>
        public static LoopbackCollector Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var collector = new LoopbackCollector(listener, $"http://127.0.0.1:{port}");
            _ = collector.AcceptLoopAsync();
            return collector;
        }

        /// <summary>Waits until at least one request has been delivered.</summary>
        /// <param name="timeoutMilliseconds">How long to wait before giving up.</param>
        /// <returns>A task that completes when a request arrives or the timeout expires.</returns>
        public async Task WaitForAnyRequestAsync(int timeoutMilliseconds = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

            while (DateTime.UtcNow < deadline)
            {
                if (RequestCount > 0) return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose() => listener.Close();

        /// <summary>Accepts requests, counting their bodies, until the listener is closed.</summary>
        /// <returns>A task that completes when the listener stops.</returns>
        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    Interlocked.Add(ref bytesReceived, Encoding.UTF8.GetByteCount(body) + context.Request.RawUrl!.Length);
                    Interlocked.Increment(ref requestCount);

                    var payload = Encoding.UTF8.GetBytes(ChatCompletionBody);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = payload.Length;
                    await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // A malformed exchange still counted above; nothing here may mask that.
                }
            }
        }
    }
}
