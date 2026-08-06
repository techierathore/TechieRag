using TechieDeskDb;

namespace TechieDesk.Services.Scheduling;

/// <summary>Finds the background scheduler helper executable (BRD-139).</summary>
public interface ISchedulerHelperLocator
{
    /// <summary>Locates the helper executable.</summary>
    /// <returns>The full path, or <see langword="null"/> when this build does not carry one.</returns>
    string? Locate();

    /// <summary>Gets the executable name the locator looks for.</summary>
    string ExecutableName { get; }
}

/// <summary>
/// Locates the helper executable next to the running application (BRD-139 / REQ-FN-042).
/// </summary>
/// <remarks>
/// <para><b>Returning null is a first-class answer.</b> The helper is a separate small host that a
/// packaging step places beside the app; a development build or a package built before that step
/// exists does not have one. The installer must then refuse and say so, because a launchd agent
/// pointing at a path that does not exist is worse than no agent: launchd would happily register it,
/// the UI would read "Installed", and nothing would ever run.</para>
/// <para>An explicitly configured path wins, so a developer can point at a
/// <c>dotnet build</c> output without repackaging.</para>
/// </remarks>
public sealed class SchedulerHelperLocator : ISchedulerHelperLocator
{
    /// <summary>Configuration key holding an explicit helper path.</summary>
    public const string ConfigKey = "Scheduler:HelperPath";

    private readonly string? configuredPath;
    private readonly string baseDirectory;

    /// <summary>Initializes the locator.</summary>
    /// <param name="configuration">Application configuration; read for <see cref="ConfigKey"/>.</param>
    public SchedulerHelperLocator(IConfiguration configuration)
        : this(configuration[ConfigKey], AppContext.BaseDirectory)
    {
    }

    /// <summary>Initializes the locator with explicit values, for tests.</summary>
    /// <param name="configuredPath">An explicit helper path, or <see langword="null"/>.</param>
    /// <param name="baseDirectory">The directory to search beside.</param>
    public SchedulerHelperLocator(string? configuredPath, string baseDirectory)
    {
        this.configuredPath = configuredPath;
        this.baseDirectory = baseDirectory;
    }

    /// <inheritdoc />
    public string ExecutableName =>
        DataDirectory.CurrentPlatform == DataDirectoryPlatform.Windows
            ? "TechieDeskScheduler.exe"
            : "TechieDeskScheduler";

    /// <inheritdoc />
    public string? Locate()
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private IEnumerable<string> CandidatePaths()
    {
        var name = ExecutableName;
        yield return Path.Combine(baseDirectory, name);
        yield return Path.Combine(baseDirectory, "Helpers", name);

        // Inside a Mac Catalyst bundle the managed assemblies live in Contents/MonoBundle; a helper
        // shipped with the app is a sibling under Contents/Helpers.
        var parent = Directory.GetParent(baseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is not null)
        {
            yield return Path.Combine(parent.FullName, "Helpers", name);
            yield return Path.Combine(parent.FullName, "MacOS", name);
        }
    }
}
