namespace TechieRag.Models;

/// <summary>
/// The one folder every locally stored model lives under: the embedding model, the reranker and
/// the local language model (REQ-RAG-053, BRD-89).
/// </summary>
/// <remarks>
/// <para><b>Why not the assembly folder any more.</b> Model files used to land next to
/// <c>TechieRag.Embedded.dll</c>. That folder is read-only inside an iOS or Android app bundle and
/// inside a signed Mac Catalyst <c>.app</c>, so a download there fails on three of the four
/// platforms a MAUI app targets. The per-user application data folder
/// (<see cref="Environment.SpecialFolder.LocalApplicationData"/>) is writable on all four.</para>
/// <para><b>Resolution order.</b> A path set through <see cref="Set"/> wins; then the
/// <see cref="EnvironmentVariable"/> environment variable; then <see cref="DefaultPath"/>, which is
/// <c>&lt;LocalApplicationData&gt;/TechieRag/models</c>. Each model gets its own sub-folder
/// (<see cref="GetModelDirectory"/>).</para>
/// <para><b>One root, shared.</b> <c>TechieRag.Embedded</c> (embedding model and reranker) and
/// <c>TechieRag.Local</c> (the language model) read this class, so a host that moves the root moves
/// every model at once.</para>
/// </remarks>
public static class ModelRoot
{
    /// <summary>
    /// Environment variable that overrides the default root when no host override is set.
    /// </summary>
    public const string EnvironmentVariable = "TECHIERAG_MODEL_ROOT";

    /// <summary>The folder created under the application data folder.</summary>
    public const string ProductFolderName = "TechieRag";

    /// <summary>The folder under <see cref="ProductFolderName"/> that holds one folder per model.</summary>
    public const string ModelsFolderName = "models";

    private static string? hostOverride;

    /// <summary>
    /// Gets the default root: <c>&lt;LocalApplicationData&gt;/TechieRag/models</c>, and on iOS and
    /// Mac Catalyst <c>&lt;home&gt;/Library/Application Support/TechieRag/models</c>.
    /// </summary>
    /// <remarks>
    /// <para>On iOS and Mac Catalyst .NET reports the <c>Documents</c> folder as
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> (seen 2026-09-25 on macOS 27: the
    /// probe's bge-m3 landed in <c>~/Documents/TechieRag/models</c>). <c>Documents</c> is the user's
    /// own files, shown in Finder and the Files app; Apple's per-user application data folder is
    /// <c>Library/Application Support</c> under the app's home, which is also where plain .NET on
    /// macOS puts <see cref="Environment.SpecialFolder.LocalApplicationData"/>.</para>
    /// <para>On a host where the application data folder is not defined (a Linux service account with
    /// no home), the user profile and then the temporary folder are used instead, so the path is
    /// never empty and never the read-only assembly folder.</para>
    /// </remarks>
    public static string DefaultPath => ComputeDefault(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        System.IO.Path.GetTempPath(),
        OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst());

    /// <summary>
    /// Gets the root in effect: the host override, else <see cref="EnvironmentVariable"/>, else
    /// <see cref="DefaultPath"/>.
    /// </summary>
    public static string Current
    {
        get
        {
            var configured = Volatile.Read(ref hostOverride);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? DefaultPath
                : System.IO.Path.GetFullPath(fromEnvironment);
        }
    }

    /// <summary>
    /// Gets whether a host has set the root through <see cref="Set"/>.
    /// </summary>
    public static bool IsOverridden => !string.IsNullOrWhiteSpace(Volatile.Read(ref hostOverride));

    /// <summary>
    /// Sets the root for every model in this process, or restores the default.
    /// </summary>
    /// <param name="path">An absolute or relative folder; <see langword="null"/> restores the default.</param>
    /// <remarks>
    /// Call it once at start-up, before the first model is loaded. A model already loaded keeps the
    /// folder it was loaded from.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty or whitespace.</exception>
    public static void Set(string? path)
    {
        if (path is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            path = System.IO.Path.GetFullPath(path);
        }

        Volatile.Write(ref hostOverride, path);
    }

    /// <summary>
    /// Gets the folder one model's files live in: <c>&lt;root&gt;/&lt;modelName&gt;</c>.
    /// </summary>
    /// <param name="modelName">The model's folder name, for example <c>bge-m3</c>.</param>
    /// <returns>The absolute folder path; it is not created.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="modelName"/> is empty or contains a path separator.</exception>
    public static string GetModelDirectory(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        if (modelName.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]) >= 0
            || modelName is "." or "..")
        {
            throw new ArgumentException("A model name is a single folder name, not a path.", nameof(modelName));
        }

        return System.IO.Path.Combine(Current, modelName);
    }

    /// <summary>
    /// Picks the default root from the folders the platform reports.
    /// </summary>
    /// <param name="localApplicationData">The per-user application data folder, possibly empty.</param>
    /// <param name="userProfile">The user profile folder, possibly empty.</param>
    /// <param name="tempPath">The temporary folder.</param>
    /// <param name="isAppleUiKit">Whether this is iOS or Mac Catalyst, where the application data
    /// folder is <c>&lt;home&gt;/Library/Application Support</c> rather than what .NET reports.</param>
    /// <returns><c>&lt;first non-empty&gt;/TechieRag/models</c>.</returns>
    internal static string ComputeDefault(string? localApplicationData, string? userProfile, string tempPath, bool isAppleUiKit = false)
    {
        var baseFolder = isAppleUiKit && !string.IsNullOrWhiteSpace(userProfile)
            ? System.IO.Path.Combine(userProfile, "Library", "Application Support")
            : !string.IsNullOrWhiteSpace(localApplicationData)
            ? localApplicationData
            : !string.IsNullOrWhiteSpace(userProfile)
                ? System.IO.Path.Combine(userProfile, ".local", "share")
                : tempPath;

        return System.IO.Path.Combine(baseFolder, ProductFolderName, ModelsFolderName);
    }
}
