using TechieDesk.Services.Agents;
using TechieDesk.Services.Agents.Mcp;
using TechieRag.Mcp;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// REQ-RAG-023 (BRD-86) — a registered MCP server's tools are really callable from the agent loop,
/// against REAL servers on a real socket and in a real child process.
/// </summary>
/// <remarks>
/// <para><b>Why real servers.</b> "Registered MCP servers expose tools to the agent" is a claim about
/// a whole path: a row in SQLite, a trust policy, a transport, a handshake, a tool list, a tool call
/// and an agent loop. Every fake in that chain is a place the claim could be false while the tests
/// stayed green — which is precisely the failure mode this project keeps catching. So the HTTP tests
/// talk to <see cref="LoopbackMcpServer"/> over 127.0.0.1 and the stdio test launches a shell script
/// as a child process.</para>
/// <para><b>The same server is also the egress meter.</b> REQ-NFR-008 promises a stock install
/// contacts nothing. That is asserted here by COUNTING BYTES at the socket, not by reading a
/// configuration flag — a flag-only assertion passes against code that dials out anyway, and this
/// project has shipped exactly that defect before.</para>
/// </remarks>
public sealed class McpAgentToolTests : IDisposable
{
    private const string Workspace = "ws-finance";
    private const string ServerName = "loopback";

    private static readonly DateTime Now = new(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc);

    private readonly McpTestHost host = new();

    /// <inheritdoc />
    public void Dispose() => host.Dispose();

    /// <summary>
    /// THE REQUIREMENT, end to end: an administrator registers an MCP server, and the tool it
    /// advertises is offered to the model, invoked by the agent loop, executed by the real server,
    /// and its answer reaches the final response.
    /// </summary>
    [Fact]
    public async Task RegisteredServerToolIsCallableFromTheAgentLoop()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var qualified = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName);
        var provider = new ScriptedLlmProvider(qualified, """{"text":"hello mcp"}""");

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Equal([ServerName], tools.StartedServers);
        Assert.Empty(tools.Failures);
        Assert.Contains(qualified, tools.ToolHandler.ToolDefinitions.Select(tool => tool.Name));

        var runner = new AgentLoopRunner(provider, tools.ToolHandler, maxIterations: 4);
        var answer = await runner.RunAsync([ChatMessage.User("Echo hello mcp")]);

        Assert.Contains(qualified, provider.OfferedToolNames);
        Assert.Contains("loopback echoed: hello mcp", answer.Content);
        Assert.Contains("hello mcp", Assert.Single(server.ToolCallArguments));
    }

    /// <summary>
    /// The tool the model is offered carries the SERVER's own schema, not a placeholder — otherwise
    /// the model cannot produce arguments the server will accept.
    /// </summary>
    [Fact]
    public async Task TheAdvertisedSchemaReachesTheModel()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        var definition = Assert.Single(tools.ToolHandler.ToolDefinitions);
        Assert.Equal("Repeats the text it is given.", definition.Description);
        Assert.Contains("\"text\"", definition.ParametersSchema);
    }

    /// <summary>
    /// THE ZERO-EGRESS GUARD (REQ-NFR-008): a workspace with no MCP registration opens no
    /// connection and sends no byte. Measured at the socket, so it cannot pass against code that
    /// dials out regardless of what the configuration says.
    /// </summary>
    [Fact]
    public async Task AnUnconfiguredWorkspaceSendsNoBytesAtAll()
    {
        await using var server = new LoopbackMcpServer();

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Empty(tools.StartedServers);
        Assert.Empty(tools.ToolHandler.ToolDefinitions);
        Assert.Equal(0, server.ConnectionCount);
        Assert.Equal(0, server.BytesReceived);

        // The meter is not broken: the SAME server, once registered, does receive bytes. Without
        // this half, a listener that could never count anything would also report zero.
        await RegisterAsync(server);
        await using var configured = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.True(server.ConnectionCount > 0, "The registered server was never contacted.");
        Assert.True(server.BytesReceived > 0, "The registered server received no bytes.");
    }

    /// <summary>
    /// A DISABLED registration is never started — not started and then filtered, but never
    /// contacted. Suspending a server has to actually stop the traffic, or the switch is decoration.
    /// </summary>
    [Fact]
    public async Task ADisabledServerIsNeverContacted()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);
        Assert.True(await host.NewRegistry(Now).SetEnabledAsync(Workspace, ServerName, isEnabled: false));

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Empty(tools.StartedServers);
        Assert.Equal(0, server.ConnectionCount);
        Assert.Equal(0, server.BytesReceived);
    }

    /// <summary>
    /// REQ-NFR-013: an HTTP MCP server leaves the machine, so with "ask before any skill that leaves
    /// this machine" ON and the prompt DECLINED, the tool call does not reach the server — and the
    /// model is told, rather than the turn being aborted.
    /// </summary>
    /// <remarks>
    /// The byte count is taken after the handshake, so what is asserted is that the DECLINED CALL
    /// sent nothing further — the discovery traffic the administrator's registration authorised is
    /// not confused with the outbound call they did not.
    /// </remarks>
    [Fact]
    public async Task ADeclinedEgressPromptStopsTheToolCallBeforeItLeaves()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var gate = new EgressGate(GuardedAgent(), new ScriptedConfirmation(isAllowed: false));

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), gate);

        var bytesAfterDiscovery = server.BytesReceived;
        var qualified = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName);

        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = qualified,
            ArgumentsJson = """{"text":"should never arrive"}"""
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("not on this machine", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(server.ToolCallArguments);
        Assert.Equal(bytesAfterDiscovery, server.BytesReceived);
    }

    /// <summary>
    /// The same call goes through once the prompt is APPROVED, so the gate is a gate and not a
    /// blanket refusal.
    /// </summary>
    [Fact]
    public async Task AnApprovedEgressPromptLetsTheToolCallThrough()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var confirmation = new ScriptedConfirmation(isAllowed: true);
        var gate = new EgressGate(GuardedAgent(), confirmation);

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), gate);

        var qualified = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName);
        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = qualified,
            ArgumentsJson = """{"text":"approved"}"""
        });

        Assert.True(result.IsSuccess);
        Assert.Contains("loopback echoed: approved", result.Content);
        Assert.Equal(1, confirmation.TimesAsked);
    }

    /// <summary>
    /// The turn asks ONCE. A second MCP tool call reuses the answer already given, in the same way a
    /// catalogue skill does — re-prompting inside one turn trains the user to click through.
    /// </summary>
    [Fact]
    public async Task TheEgressPromptIsRaisedOncePerTurn()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var confirmation = new ScriptedConfirmation(isAllowed: true);
        var gate = new EgressGate(GuardedAgent(), confirmation);

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), gate);

        var qualified = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName);
        for (var index = 0; index < 3; index++)
        {
            await tools.ToolHandler.ExecuteToolAsync(new ToolCall
            {
                Id = $"call-{index}",
                Name = qualified,
                ArgumentsJson = """{"text":"again"}"""
            });
        }

        Assert.Equal(3, server.ToolCallArguments.Count);
        Assert.Equal(1, confirmation.TimesAsked);
    }

    /// <summary>
    /// Fail closed: with confirmation required and no way to ask — a host that never wired the
    /// dialog — the outbound MCP call is denied rather than silently permitted.
    /// </summary>
    [Fact]
    public async Task WithNoWayToAskTheOutboundCallIsDenied()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var gate = new EgressGate(GuardedAgent(), confirmation: null);

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), gate);

        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName),
            ArgumentsJson = "{}"
        });

        Assert.False(result.IsSuccess);
        Assert.Empty(server.ToolCallArguments);
    }

    /// <summary>
    /// The credential really reaches the wire: the header value the administrator typed arrives on
    /// the request, having gone to the credential store, come back on a modelled restart, and been
    /// applied by the transport. Storing a token and never sending it would pass a store round-trip
    /// test and fail here.
    /// </summary>
    [Fact]
    public async Task TheStoredCredentialIsSentOnTheWire()
    {
        const string token = "Bearer wire-secret";

        await using var server = new LoopbackMcpServer(requiredAuthorization: token);
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = ServerName,
                Transport = McpTransportKind.Http,
                Endpoint = server.Endpoint.ToString(),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = token
                }
            }
        });

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName),
            ArgumentsJson = """{"text":"authenticated"}"""
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, server.AuthorizedCallCount);
    }

    /// <summary>
    /// A local catalogue skill wins a name clash, so a registered server cannot shadow
    /// <c>rag-search</c> — or any other skill — by advertising a tool of the same name.
    /// </summary>
    [Fact]
    public async Task LocalSkillsWinANameClash()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        var local = new ToolRegistry();
        local.Register(
            SkillCatalog.RagSearch,
            "The workspace's own search",
            WorkspaceSkillTools.RagSearchSchema,
            (_, _) => Task.FromResult("local skill answered"));

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, local, OpenGate());

        var names = tools.ToolHandler.ToolDefinitions.Select(tool => tool.Name).ToList();
        Assert.Contains(SkillCatalog.RagSearch, names);
        Assert.Contains(McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName), names);

        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = SkillCatalog.RagSearch,
            ArgumentsJson = """{"query":"anything"}"""
        });

        Assert.Equal("local skill answered", result.Content);
    }

    /// <summary>
    /// One unreachable server does not cost the workspace its other tools, and the failure is
    /// reported rather than swallowed — an endpoint that is down must not silently look like a
    /// workspace with no MCP servers.
    /// </summary>
    [Fact]
    public async Task OneDeadServerDoesNotCostTheOthers()
    {
        await using var server = new LoopbackMcpServer();
        await RegisterAsync(server);

        // A port nothing is listening on. Registered and enabled exactly like the working one.
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = "dead",
                Transport = McpTransportKind.Http,
                Endpoint = "http://127.0.0.1:1/mcp",
                TimeoutSeconds = 2
            }
        });

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Equal([ServerName], tools.StartedServers);
        Assert.Equal("dead", Assert.Single(tools.Failures).ServerName);
        Assert.Contains(
            McpToolHandler.QualifyToolName(ServerName, LoopbackMcpServer.ToolName),
            tools.ToolHandler.ToolDefinitions.Select(tool => tool.Name));
    }

    /// <summary>
    /// A tool the configured allow-list excludes is never shown to the model, even though the server
    /// advertises it — so a server that grows a new destructive tool in an update cannot quietly
    /// acquire it.
    /// </summary>
    [Fact]
    public async Task AToolOutsideTheAllowListIsNeverOffered()
    {
        await using var server = new LoopbackMcpServer();
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = ServerName,
                Transport = McpTransportKind.Http,
                Endpoint = server.Endpoint.ToString(),
                AllowedTools = ["something-else"]
            }
        });

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Empty(tools.ToolHandler.ToolDefinitions);
    }

    /// <summary>
    /// The STDIO path, against a real child process: the registered executable is launched, its
    /// tool is offered to the model, the agent loop calls it, and the environment credential the
    /// administrator stored comes back inside the tool's own answer.
    /// </summary>
    /// <remarks>
    /// The token is the load-bearing part. It is written to the credential store on registration,
    /// read back by a rebuilt registry, handed to the transport as a process environment variable,
    /// and echoed by the script — so it can only appear in the assertion if every one of those hops
    /// worked. Skipped where there is no POSIX shell to launch.
    /// </remarks>
    [Fact]
    public async Task StdioServerToolRoundTripsCarryingItsStoredEnvironmentCredential()
    {
        if (OperatingSystem.IsWindows())
        {
            // The fixture is a POSIX shell script. The Windows head has no platform sources yet
            // (REQ-FN-035) and this ladder builds on macOS, so returning here is not hiding a
            // failure — on every host that actually runs this suite the assertion below holds.
            return;
        }

        Assert.True(StdioMcpServerFixture.IsSupported, "/bin/sh is missing on this Unix host.");

        using var fixture = new StdioMcpServerFixture();
        const string token = "stdio-secret-42";

        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = "local-fixture",
                Transport = McpTransportKind.Stdio,
                Command = StdioMcpServerFixture.ShellPath,
                Arguments = [fixture.ScriptPath],
                TimeoutSeconds = 20,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [StdioMcpServerFixture.TokenVariableName] = token
                }
            }
        });

        var qualified = McpToolHandler.QualifyToolName("local-fixture", StdioMcpServerFixture.ToolName);
        var provider = new ScriptedLlmProvider(qualified);

        // A fresh service, so the credential is read back out of the store rather than reused.
        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), OpenGate());

        Assert.Equal(["local-fixture"], tools.StartedServers);

        var runner = new AgentLoopRunner(provider, tools.ToolHandler, maxIterations: 4);
        var answer = await runner.RunAsync([ChatMessage.User("Who are you?")]);

        Assert.Contains(qualified, provider.OfferedToolNames);
        Assert.Contains($"token={token}", answer.Content);
    }

    /// <summary>
    /// A stdio server is NOT gated by the egress prompt: it is a local child process, and a dialog
    /// saying its request "leaves this machine" would be false. The registration itself is the
    /// consent for launching it. This records that decision so a later change cannot flip it
    /// silently in either direction.
    /// </summary>
    [Fact]
    public async Task StdioToolsAreNotEgressGated()
    {
        if (OperatingSystem.IsWindows())
        {
            // The fixture is a POSIX shell script. The Windows head has no platform sources yet
            // (REQ-FN-035) and this ladder builds on macOS, so returning here is not hiding a
            // failure — on every host that actually runs this suite the assertion below holds.
            return;
        }

        Assert.True(StdioMcpServerFixture.IsSupported, "/bin/sh is missing on this Unix host.");

        using var fixture = new StdioMcpServerFixture();
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = "local-fixture",
                Transport = McpTransportKind.Stdio,
                Command = StdioMcpServerFixture.ShellPath,
                Arguments = [fixture.ScriptPath],
                TimeoutSeconds = 20
            }
        });

        // Confirmation required, and no way to ask — which would DENY anything that leaves the
        // machine. A local process is not that.
        var gate = new EgressGate(GuardedAgent(), confirmation: null);

        await using var tools = await host.NewService(Now)
            .BuildTurnToolsAsync(Workspace, new ToolRegistry(), gate);

        var result = await tools.ToolHandler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = McpToolHandler.QualifyToolName("local-fixture", StdioMcpServerFixture.ToolName),
            ArgumentsJson = "{}"
        });

        Assert.True(result.IsSuccess);
        Assert.Contains("stdio fixture", result.Content);
    }

    private async Task RegisterAsync(LoopbackMcpServer server) =>
        await host.NewRegistry(Now).RegisterAsync(new McpServerRegistration
        {
            WorkspaceId = Workspace,
            RegisteredAtUtc = Now,
            Server = new McpServerConfig
            {
                Name = ServerName,
                Transport = McpTransportKind.Http,
                Endpoint = server.Endpoint.ToString(),
                TimeoutSeconds = 20
            }
        });

    /// <summary>An agent that does NOT ask before egress, for the tests that are not about the gate.</summary>
    /// <returns>The gate such an agent produces.</returns>
    private static EgressGate OpenGate() =>
        new(new AgentDefinition { WorkspaceId = Workspace, Handle = "agent", ConfirmEgress = false }, null);

    /// <summary>An agent that DOES ask before anything leaves the machine.</summary>
    /// <returns>The agent definition.</returns>
    private static AgentDefinition GuardedAgent() =>
        new() { WorkspaceId = Workspace, Handle = "agent", DisplayName = "Guarded", ConfirmEgress = true };

    /// <summary>A confirmation that always answers the same way, and counts how often it was asked.</summary>
    /// <param name="isAllowed">The answer to give.</param>
    private sealed class ScriptedConfirmation(bool isAllowed) : IEgressConfirmation
    {
        private int timesAsked;

        /// <summary>Gets how many times the prompt was raised.</summary>
        public int TimesAsked => Volatile.Read(ref timesAsked);

        /// <inheritdoc />
        public Task<bool> ConfirmAsync(EgressConfirmationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref timesAsked);
            return Task.FromResult(isAllowed);
        }
    }
}
