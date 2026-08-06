using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechieDesk.Services.Agents.Mcp;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieRag.Mcp;
using Xunit;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// REQ-RAG-023 — the registry the application resolves is the DURABLE one, and the editor's text
/// fields become the validated configuration in one tested place.
/// </summary>
public sealed class McpRegistrationCompositionTests
{
    /// <summary>
    /// <see cref="IMcpServerRegistry"/> resolves to the SQLite registry. The library's
    /// <c>InMemoryMcpServerRegistry</c> is process-lifetime storage; resolving it in a desktop app
    /// would mean re-typing every MCP server on every launch, so it must not be reachable through DI
    /// at all.
    /// </summary>
    [Fact]
    public void TheResolvedRegistryIsDurableAndNotTheInMemoryOne()
    {
        using var provider = BuildContainer();

        var registry = provider.GetRequiredService<IMcpServerRegistry>();

        Assert.IsType<SqliteMcpServerRegistry>(registry);
        Assert.IsNotType<InMemoryMcpServerRegistry>(registry);
    }

    /// <summary>
    /// The library contract and the app's administration contract are answered by ONE object, so
    /// the agent loop and the Agents screen cannot end up reading two different stores.
    /// </summary>
    [Fact]
    public void TheLibraryAndAdministrationContractsShareOneInstance()
    {
        using var provider = BuildContainer();

        var registry = provider.GetRequiredService<IMcpServerRegistry>();
        var administration = provider.GetRequiredService<IMcpServerAdministration>();

        Assert.Same(registry, administration);
    }

    /// <summary>The workspace MCP service is resolvable, so the chat page can inject it.</summary>
    [Fact]
    public void TheWorkspaceMcpServiceIsResolvable()
    {
        using var provider = BuildContainer();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkspaceMcpService>());
    }

    /// <summary>
    /// Arguments are split on NEWLINES, so a path containing a space stays one argument. Splitting
    /// on spaces is what the library's list-shaped configuration exists to make impossible.
    /// </summary>
    [Fact]
    public void ArgumentsAreSplitOnLinesNotOnSpaces()
    {
        var draft = new McpServerDraft
        {
            Name = "local",
            Transport = McpTransportKind.Stdio,
            Command = "/usr/local/bin/server",
            ArgumentsText = "--root\n/Users/Some One/My Documents\n\n--verbose\n"
        };

        var config = draft.ToConfig();

        Assert.Equal(["--root", "/Users/Some One/My Documents", "--verbose"], config.Arguments);
    }

    /// <summary>
    /// A credential row left blank keeps the value already stored, so editing an endpoint does not
    /// silently erase a token the operator cannot see and would not think to re-type.
    /// </summary>
    [Fact]
    public void ABlankCredentialValueKeepsTheStoredOne()
    {
        var draft = new McpServerDraft { Name = "ledger", Transport = McpTransportKind.Http, Endpoint = "https://a/b" };
        draft.Credentials.Add(new McpCredentialEntry { Name = "Authorization", Value = string.Empty });
        draft.Credentials.Add(new McpCredentialEntry { Name = "X-Tenant", Value = "typed-now" });

        var config = draft.ToConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer stored-earlier"
        });

        Assert.Equal("Bearer stored-earlier", config.Headers["Authorization"]);
        Assert.Equal("typed-now", config.Headers["X-Tenant"]);
    }

    /// <summary>
    /// Switching transport drops the other transport's fields, because the library rejects a
    /// configuration carrying both — the form must not need clearing by hand to become saveable.
    /// </summary>
    [Fact]
    public void SwitchingTransportDropsTheOtherTransportsFields()
    {
        var draft = new McpServerDraft
        {
            Name = "ledger",
            Transport = McpTransportKind.Stdio,
            Command = "/usr/local/bin/server",
            Endpoint = "https://left-over.example.com/rpc"
        };

        var stdio = draft.ToConfig();
        Assert.Null(stdio.Endpoint);

        draft.Transport = McpTransportKind.Http;
        var http = draft.ToConfig();
        Assert.Null(http.Command);
        Assert.Empty(http.Arguments);
    }

    /// <summary>
    /// An existing registration opens in the editor with its credential NAMES and no values, so a
    /// bearer token is never repainted into a text box, the DOM, or a screenshot.
    /// </summary>
    [Fact]
    public void EditingAServerDoesNotRepaintItsStoredCredentialValues()
    {
        var record = new McpServerRecord(
            new McpServerRegistration
            {
                WorkspaceId = "ws",
                Server = new McpServerConfig
                {
                    Name = "ledger",
                    Transport = McpTransportKind.Http,
                    Endpoint = "https://mcp.example.com/rpc",
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Authorization"] = "Bearer visible-secret"
                    }
                }
            },
            SecretKeyNames: ["Authorization"],
            AdvertisedTools: [],
            LastCheckedUtc: null);

        var draft = McpServerDraft.From(record);

        var credential = Assert.Single(draft.Credentials);
        Assert.Equal("Authorization", credential.Name);
        Assert.Equal(string.Empty, credential.Value);
        Assert.True(draft.IsExisting);
    }

    /// <summary>
    /// The trust policy this application registers under is stated in code and is closed where it
    /// matters: plaintext HTTP beyond loopback stays refused.
    /// </summary>
    [Fact]
    public void TheDesktopTrustPolicyRefusesPlaintextHttpBeyondLoopback()
    {
        Assert.False(McpTrustPolicyFactory.Desktop.AllowPlaintextHttp);
        Assert.True(McpTrustPolicyFactory.Desktop.AllowLocalProcessLaunch);
        Assert.Empty(McpTrustPolicyFactory.Desktop.AllowedCommandDirectories);
    }

    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppDb:Provider"] = "Sqlite",
                ["AppDb:ConnectionString"] = "Data Source=:memory:"
            })
            .Build());
        services.AddSingleton<ISecretStore, EphemeralSecretStore>();
        services.AddTechieDeskData(services.BuildServiceProvider().GetRequiredService<IConfiguration>());
        services.AddTechieDeskMcp();

        return services.BuildServiceProvider();
    }
}
