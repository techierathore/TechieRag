using TechieRag.Mcp;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Unit tests for per-workspace MCP server registration (REQ-RAG-023): what an administrator
/// registers, what the trust policy refuses, and what the agent is actually offered.
/// </summary>
public class McpServerRegistryTests
{
    /// <summary>A registered server is listed for its workspace.</summary>
    [Fact]
    public async Task RegisteredServerIsListedForItsWorkspace()
    {
        var registry = new InMemoryMcpServerRegistry();
        await registry.RegisterAsync(Registration("ws1", "docs"));

        var listed = await registry.ListAsync("ws1");

        Assert.Equal("docs", Assert.Single(listed).Server.Name);
    }

    /// <summary>Registrations do not leak between workspaces.</summary>
    [Fact]
    public async Task RegistrationsAreScopedToTheirWorkspace()
    {
        var registry = new InMemoryMcpServerRegistry();
        await registry.RegisterAsync(Registration("ws1", "docs"));

        Assert.Empty(await registry.ListAsync("ws2"));
    }

    /// <summary>A configuration the trust policy forbids is refused at registration, not at use.</summary>
    [Fact]
    public async Task PolicyViolatingConfigurationIsRefusedAtRegistration()
    {
        var registry = new InMemoryMcpServerRegistry();
        var registration = new McpServerRegistration
        {
            WorkspaceId = "ws1",
            Server = new McpServerConfig
            {
                Name = "local",
                Transport = McpTransportKind.Stdio,
                Command = Path.Combine(Path.GetTempPath(), "server")
            }
        };

        await Assert.ThrowsAsync<McpConfigurationException>(() => registry.RegisterAsync(registration));
        Assert.Empty(await registry.ListAsync("ws1"));
    }

    /// <summary>Disabling keeps the registration but removes it from what the agent is offered.</summary>
    [Fact]
    public async Task DisabledServerRemainsRegisteredButIsNotStarted()
    {
        var registry = new InMemoryMcpServerRegistry();
        await registry.RegisterAsync(Registration("ws1", "docs"));

        Assert.True(await registry.SetEnabledAsync("ws1", "docs", false));

        var listed = await registry.ListAsync("ws1");
        Assert.False(Assert.Single(listed).IsEnabled);

        await using var tools = await registry.BuildWorkspaceToolsAsync("ws1", McpTrustPolicy.Strict);
        Assert.Empty(tools.StartedServers);
        Assert.Empty(tools.Failures);
        Assert.Empty(tools.ToolHandler.ToolDefinitions);
    }

    /// <summary>Unregistering removes the server; unregistering nothing reports false.</summary>
    [Fact]
    public async Task UnregisterRemovesTheServer()
    {
        var registry = new InMemoryMcpServerRegistry();
        await registry.RegisterAsync(Registration("ws1", "docs"));

        Assert.True(await registry.UnregisterAsync("ws1", "docs"));
        Assert.False(await registry.UnregisterAsync("ws1", "docs"));
        Assert.Empty(await registry.ListAsync("ws1"));
    }

    /// <summary>Re-registering the same name replaces the previous configuration rather than duplicating it.</summary>
    [Fact]
    public async Task ReRegisteringSameNameReplacesConfiguration()
    {
        var registry = new InMemoryMcpServerRegistry();
        await registry.RegisterAsync(Registration("ws1", "docs", "https://first.example.com"));
        await registry.RegisterAsync(Registration("ws1", "docs", "https://second.example.com"));

        var listed = await registry.ListAsync("ws1");

        Assert.Equal("https://second.example.com", Assert.Single(listed).Server.Endpoint);
    }

    /// <summary>
    /// A server the build-time policy refuses is recorded as a failure rather than throwing, and the
    /// workspace keeps every other tool it has.
    /// </summary>
    /// <remarks>
    /// Registration happens under a permissive policy and the build under the strict one, which
    /// reproduces a real tightening of host policy — and exercises the partial-failure path without
    /// needing a network or a server.
    /// </remarks>
    [Fact]
    public async Task ServerRefusedAtBuildTimeIsReportedWithoutCostingOtherTools()
    {
        var permissive = new McpTrustPolicy { AllowPlaintextHttp = true };
        var registry = new InMemoryMcpServerRegistry(permissive);
        await registry.RegisterAsync(Registration("ws1", "alpha", "http://alpha.example.com"));
        await registry.RegisterAsync(Registration("ws1", "beta", "http://beta.example.com"));

        var local = new TechieRag.Services.ToolRegistry();
        local.Register("localSkill", "a local skill", """{"type":"object"}""", _ => "hi");

        await using var tools = await registry.BuildWorkspaceToolsAsync("ws1", McpTrustPolicy.Strict, local);

        Assert.Equal(2, tools.Failures.Count);
        Assert.Empty(tools.StartedServers);
        Assert.Equal("localSkill", Assert.Single(tools.ToolHandler.ToolDefinitions).Name);
    }

    /// <summary>With no MCP servers, the caller's local tools are still the handler that comes back.</summary>
    [Fact]
    public async Task LocalToolsSurviveWhenNoMcpServersAreRegistered()
    {
        var registry = new InMemoryMcpServerRegistry();
        var local = new TechieRag.Services.ToolRegistry();
        local.Register("localSkill", "a local skill", """{"type":"object"}""", _ => "hi");

        await using var tools = await registry.BuildWorkspaceToolsAsync("ws1", McpTrustPolicy.Strict, local);

        Assert.Equal("localSkill", Assert.Single(tools.ToolHandler.ToolDefinitions).Name);
    }

    /// <summary>The log-safe description of a registration never contains a secret value.</summary>
    [Fact]
    public void RegistrationDescriptionRedactsSecrets()
    {
        var registration = new McpServerRegistration
        {
            WorkspaceId = "ws1",
            Server = new McpServerConfig
            {
                Name = "docs",
                Transport = McpTransportKind.Http,
                Endpoint = "https://mcp.example.com",
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer hunter2" }
            }
        };

        Assert.DoesNotContain("hunter2", registration.Describe());
    }

    private static McpServerRegistration Registration(string workspaceId, string name, string endpoint = "https://mcp.example.com") => new()
    {
        WorkspaceId = workspaceId,
        Server = new McpServerConfig
        {
            Name = name,
            Transport = McpTransportKind.Http,
            Endpoint = endpoint,
            TimeoutSeconds = 2
        }
    };
}
