using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Models;

/// <summary>
/// Where an app's SQLite database goes when the app names no folder for it
/// (REQ-RAG-122 / BRD-182, Sevak feedback TR-RAG-040).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The old default, <c>Data Source=techierag.db</c>, is relative to the folder the app
/// runs from. That folder moves with every publish, is read-only inside an app bundle, and is shared by
/// any two apps launched from one folder, so their databases mixed.</para>
/// <para><b>Resolution order (the models' order, <see cref="ModelRoot"/>):</b> a folder set in code with
/// <see cref="Set"/>; otherwise the <c>data</c> folder beside the model root, so whatever moved the
/// models — <see cref="ModelRoot.Set"/> or <see cref="ModelRoot.EnvironmentVariable"/> — moves the
/// data with them; otherwise the per-user default <c>&lt;LocalApplicationData&gt;/TechieRag/data</c>
/// (iOS and Mac Catalyst: <c>&lt;home&gt;/Library/Application Support/TechieRag/data</c>). No extra
/// environment variable is read: the model root's already covers the deployment case.</para>
/// <para><b>One folder per app:</b> the database lives in <c>&lt;root&gt;/&lt;app name&gt;/techierag.db</c>.
/// The app name is the entry assembly's name unless <see cref="SetAppName"/> sets one.</para>
/// <para><b>Existing databases are kept (owner decision 2026-10-06).</b> When a <c>techierag.db</c>
/// already exists in the folder the app runs from, that file is used and a warning naming the folder
/// is logged. Nothing is ever moved: moving would make an upgraded app look empty.</para>
/// <para><b>Only defaults.</b> A connection string, a full path or a relative path the caller set
/// explicitly is used as given.</para>
/// </remarks>
public static class DataRoot
{
    /// <summary>The folder under <see cref="ModelRoot.ProductFolderName"/> that holds one folder per app.</summary>
    public const string DataFolderName = "data";

    /// <summary>The database file name used when the app names none.</summary>
    public const string DefaultDatabaseFileName = "techierag.db";

    private static string? hostOverride;
    private static string? appNameOverride;

    /// <summary>
    /// Gets the data root in effect: the folder set through <see cref="Set"/>, else the <c>data</c>
    /// folder beside <see cref="ModelRoot.Current"/>.
    /// </summary>
    public static string Current
    {
        get
        {
            var configured = Volatile.Read(ref hostOverride);
            return !string.IsNullOrWhiteSpace(configured) ? configured : BesideModelRoot(ModelRoot.Current);
        }
    }

    /// <summary>
    /// Gets the per-user default data root, ignoring every override:
    /// <c>&lt;LocalApplicationData&gt;/TechieRag/data</c>.
    /// </summary>
    public static string DefaultPath => BesideModelRoot(ModelRoot.DefaultPath);

    /// <summary>
    /// Gets the app name the database folder is named after: the name set through
    /// <see cref="SetAppName"/>, else the entry assembly's name, else <c>app</c>.
    /// </summary>
    public static string AppName
    {
        get
        {
            var configured = Volatile.Read(ref appNameOverride);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var entry = Assembly.GetEntryAssembly()?.GetName().Name;
            return string.IsNullOrWhiteSpace(entry) ? "app" : ToFolderName(entry);
        }
    }

    /// <summary>
    /// Gets this app's data folder: <c>&lt;<see cref="Current"/>&gt;/&lt;<see cref="AppName"/>&gt;</c>. It is not created.
    /// </summary>
    public static string AppDirectory => Path.Combine(Current, AppName);

    /// <summary>
    /// Gets the database file the library uses when the app names no folder: an existing
    /// <c>techierag.db</c> in the folder the app runs from, otherwise
    /// <c>&lt;<see cref="AppDirectory"/>&gt;/techierag.db</c>.
    /// </summary>
    /// <remarks>Reading it creates nothing and logs nothing; building the library does both.</remarks>
    public static string DefaultDatabasePath => ResolveDatabasePath(Environment.CurrentDirectory, null, createDirectory: false);

    /// <summary>
    /// Gets the SQLite connection string for <see cref="DefaultDatabasePath"/>.
    /// </summary>
    public static string DefaultConnectionString => "Data Source=" + DefaultDatabasePath;

    /// <summary>
    /// Sets the data root for every database in this process, or restores the default.
    /// </summary>
    /// <param name="path">An absolute or relative folder; <see langword="null"/> restores the default.
    /// Each app still gets its own sub-folder under it.</param>
    /// <remarks>Call it once at start-up, before the library is built.</remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty or whitespace.</exception>
    public static void Set(string? path)
    {
        if (path is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            path = Path.GetFullPath(path);
        }

        Volatile.Write(ref hostOverride, path);
    }

    /// <summary>
    /// Sets the app name the database folder is named after, or restores the entry assembly's name.
    /// </summary>
    /// <param name="appName">A single folder name; <see langword="null"/> restores the default.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="appName"/> is empty, whitespace,
    /// <c>.</c>, <c>..</c> or contains a path separator.</exception>
    public static void SetAppName(string? appName)
    {
        if (appName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appName);
            if (appName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
                || appName is "." or "..")
            {
                throw new ArgumentException("An app name is a single folder name, not a path.", nameof(appName));
            }
        }

        Volatile.Write(ref appNameOverride, appName);
    }

    /// <summary>
    /// Resolves the default database file, creating this app's folder and logging the legacy case.
    /// </summary>
    /// <param name="logger">Receives the warning when an existing database in the run folder is kept; null for none.</param>
    /// <returns>The absolute database file path.</returns>
    internal static string ResolveDefaultDatabasePath(ILogger? logger) =>
        ResolveDatabasePath(Environment.CurrentDirectory, logger, createDirectory: true);

    /// <summary>
    /// The resolution itself, with the run folder passed in so tests do not change the process's
    /// current directory.
    /// </summary>
    /// <param name="runFolder">The folder the app runs from.</param>
    /// <param name="logger">Receives the legacy-file warning; null for none.</param>
    /// <param name="createDirectory">Whether to create <see cref="AppDirectory"/> when it is used.</param>
    /// <returns>The absolute database file path.</returns>
    internal static string ResolveDatabasePath(string runFolder, ILogger? logger, bool createDirectory)
    {
        var legacy = Path.GetFullPath(Path.Combine(runFolder, DefaultDatabaseFileName));
        if (File.Exists(legacy))
        {
            (logger ?? NullLogger.Instance).LogWarning(
                "Using the existing database {DatabaseFile} in the folder the app runs from ({Folder}). New apps keep their database in {AppDirectory}; this file is kept where it is and never moved.",
                DefaultDatabaseFileName, runFolder, AppDirectory);
            return legacy;
        }

        var directory = AppDirectory;
        if (createDirectory)
        {
            Directory.CreateDirectory(directory);
        }

        return Path.Combine(directory, DefaultDatabaseFileName);
    }

    /// <summary>
    /// Builds a SQLite connection string for the default database, resolving and creating it.
    /// </summary>
    /// <param name="logger">Receives the legacy-file warning; null for none.</param>
    /// <returns><c>Data Source=&lt;path&gt;</c>.</returns>
    internal static string ResolveDefaultConnectionString(ILogger? logger) =>
        "Data Source=" + ResolveDefaultDatabasePath(logger);

    /// <summary>The <c>data</c> folder beside a model root.</summary>
    /// <param name="modelRoot">The model root, normally <c>…/TechieRag/models</c>.</param>
    /// <returns><c>…/TechieRag/data</c>.</returns>
    internal static string BesideModelRoot(string modelRoot)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(modelRoot);
        var parent = Path.GetDirectoryName(trimmed);
        return Path.Combine(string.IsNullOrEmpty(parent) ? trimmed : parent, DataFolderName);
    }

    /// <summary>Replaces characters a folder name cannot hold.</summary>
    /// <param name="name">An assembly name.</param>
    /// <returns>A single safe folder name.</returns>
    private static string ToFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }
}
