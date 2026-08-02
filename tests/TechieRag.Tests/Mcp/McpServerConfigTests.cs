using TechieRag.Mcp;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Unit tests for the MCP trust model (REQ-RAG-038): an MCP server configuration must be refused
/// before anything is launched or contacted unless the host's policy actually permits it.
/// </summary>
public class McpServerConfigTests
{
    /// <summary>The closed default refuses to launch a local process, however well-formed the command.</summary>
    [Fact]
    public void StrictPolicyRefusesProcessLaunch()
    {
        var config = StdioConfig(AbsolutePath("server"));

        var problems = config.FindProblems(McpTrustPolicy.Strict);

        Assert.Contains(problems, problem => problem.Contains("AllowLocalProcessLaunch"));
    }

    /// <summary>A bare executable name is refused: PATH lookup would decide what actually runs.</summary>
    [Fact]
    public void RelativeCommandIsRefused()
    {
        var config = StdioConfig("npx");

        var problems = config.FindProblems(new McpTrustPolicy { AllowLocalProcessLaunch = true });

        Assert.Contains(problems, problem => problem.Contains("fully-qualified"));
    }

    /// <summary>A command outside the host's allow-listed directories is refused.</summary>
    [Fact]
    public void CommandOutsideAllowedDirectoryIsRefused()
    {
        var config = StdioConfig(AbsolutePath("elsewhere", "server"));
        var policy = new McpTrustPolicy
        {
            AllowLocalProcessLaunch = true,
            AllowedCommandDirectories = [AbsolutePath("approved")]
        };

        var problems = config.FindProblems(policy);

        Assert.Contains(problems, problem => problem.Contains("AllowedCommandDirectories"));
    }

    /// <summary>A command inside an allow-listed directory passes when launching is permitted.</summary>
    [Fact]
    public void CommandInsideAllowedDirectoryIsAccepted()
    {
        var config = StdioConfig(AbsolutePath("approved", "server"));
        var policy = new McpTrustPolicy
        {
            AllowLocalProcessLaunch = true,
            AllowedCommandDirectories = [AbsolutePath("approved")]
        };

        Assert.Empty(config.FindProblems(policy));
    }

    /// <summary>Plaintext http to a remote host is refused, because its bearer token would be in the clear.</summary>
    [Fact]
    public void PlaintextHttpToRemoteHostIsRefused()
    {
        var config = HttpConfig("http://mcp.example.com/rpc");

        var problems = config.FindProblems(McpTrustPolicy.Strict);

        Assert.Contains(problems, problem => problem.Contains("plaintext"));
    }

    /// <summary>Plaintext http to loopback is accepted, because it never leaves the machine.</summary>
    [Fact]
    public void PlaintextHttpToLoopbackIsAccepted()
    {
        var config = HttpConfig("http://localhost:3000/mcp");

        Assert.Empty(config.FindProblems(McpTrustPolicy.Strict));
    }

    /// <summary>An https endpoint is accepted under the closed default policy.</summary>
    [Fact]
    public void HttpsEndpointIsAccepted()
    {
        Assert.Empty(HttpConfig("https://mcp.example.com/rpc").FindProblems(McpTrustPolicy.Strict));
    }

    /// <summary>A name that would not survive as an LLM tool name is refused.</summary>
    [Fact]
    public void InvalidServerNameIsRefused()
    {
        var config = new McpServerConfig
        {
            Name = "bad name!",
            Transport = McpTransportKind.Http,
            Endpoint = "https://mcp.example.com"
        };

        Assert.Contains(config.FindProblems(McpTrustPolicy.Strict), problem => problem.Contains("Name must be"));
    }

    /// <summary>Validate throws and reports every problem at once, not just the first.</summary>
    [Fact]
    public void ValidateReportsAllProblems()
    {
        var config = new McpServerConfig
        {
            Name = "bad name!",
            Transport = McpTransportKind.Stdio,
            Command = "relative"
        };

        var error = Assert.Throws<McpConfigurationException>(() => config.Validate(McpTrustPolicy.Strict));

        Assert.True(error.Problems.Count >= 2);
    }

    /// <summary>The log-safe description names secret keys but never their values.</summary>
    [Fact]
    public void DescribeRedactsSecretValues()
    {
        var config = new McpServerConfig
        {
            Name = "docs",
            Transport = McpTransportKind.Http,
            Endpoint = "https://mcp.example.com/rpc",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer super-secret-token" }
        };

        var description = config.Describe();

        Assert.Contains("Authorization", description);
        Assert.DoesNotContain("super-secret-token", description);
    }

    private static McpServerConfig StdioConfig(string command) => new()
    {
        Name = "local",
        Transport = McpTransportKind.Stdio,
        Command = command
    };

    private static McpServerConfig HttpConfig(string endpoint) => new()
    {
        Name = "remote",
        Transport = McpTransportKind.Http,
        Endpoint = endpoint
    };

    private static string AbsolutePath(params string[] segments) =>
        Path.Combine([Path.GetTempPath(), .. segments]);
}
