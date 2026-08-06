using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-UI-045 / REQ-RAG-022 — the agent services the Agents screen and the workspace chat
/// <c>@@inject</c> really resolve out of the app's own registration.
/// </summary>
/// <remarks>
/// A missing DI registration compiles perfectly and only fails when someone navigates to the page,
/// which is exactly the class of defect a build and a unit suite both miss. Resolving them from the
/// real <c>AddTechieDeskData</c> — not a hand-built container — is what makes this worth having.
/// </remarks>
public class AgentServiceRegistrationTests
{
    /// <summary>
    /// Every agent service a Razor page injects resolves from the shipped registration, so
    /// navigating to the Agents screen cannot fail with "no service for type".
    /// </summary>
    [Fact]
    public void AgentServicesResolveFromTheShippedRegistration()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetService<IAgentRegistry>());
        Assert.NotNull(provider.GetService<IAgentRepository>());
        Assert.NotNull(provider.GetService<IWorkspaceSkillRepository>());
    }

    /// <summary>
    /// The registry resolves as the concrete <see cref="AgentRegistry"/> with both repositories
    /// injected, rather than as some stand-in that would silently not persist.
    /// </summary>
    [Fact]
    public void RegistryResolvesToTheRealImplementation()
    {
        using var provider = BuildProvider();

        Assert.IsType<AgentRegistry>(provider.GetRequiredService<IAgentRegistry>());
        Assert.IsType<AgentRepository>(provider.GetRequiredService<IAgentRepository>());
        Assert.IsType<WorkspaceSkillRepository>(provider.GetRequiredService<IWorkspaceSkillRepository>());
    }

    /// <summary>Builds the app's data-layer container over an in-memory configuration.</summary>
    /// <returns>The built provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppDb:Provider"] = "Sqlite",
                ["AppDb:ConnectionString"] = "Data Source=:memory:"
            })
            .Build();

        return new ServiceCollection()
            .AddTechieDeskData(configuration)
            .BuildServiceProvider();
    }
}
