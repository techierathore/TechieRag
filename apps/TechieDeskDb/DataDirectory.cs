namespace TechieDeskDb;

/// <summary>
/// Single authority for where TechieDesk keeps its on-disk state (REQ-FN-034, extended by REQ-FN-037).
/// </summary>
/// <remarks>
/// <para>
/// Every persistent artefact — the app database, the TechieRag conversation/workspace store, the
/// SqliteVec vector database, the saved provider configuration, the rolling log files and the Data
/// Protection key ring — lives under ONE directory resolved here. Before this existed each of those
/// resolved its own path against a different root (current working directory,
/// <see cref="AppContext.BaseDirectory"/>, and the content root), so a plain <c>dotnet run</c>
/// migrated one database while the application opened another and reported success either way.
/// </para>
/// <para>
/// <b>REQ-FN-037.</b> That one directory is now the per-user OS location —
/// <c>~/Library/Application Support/TechieDesk</c> on macOS, <c>%LOCALAPPDATA%\TechieDesk</c> on
/// Windows — rather than an app-relative <c>data/</c> folder. The desktop head (REQ-FN-035) ships as
/// a signed <c>.app</c> bundle whose contents are read-only, so the previous root resolved inside
/// <c>TechieDesk.app/Contents/MonoBundle/data</c>: it worked from <c>bin/</c> and would have failed
/// on any real install. An install carrying the old app-relative directory has it relocated once by
/// <see cref="RelocateLegacyDataDirectory"/>, losing nothing.
/// </para>
/// <para>
/// <b>Never resolve a data path against the current working directory, the content root, or the base
/// directory.</b> Those depend on how the process was launched (<c>dotnet run</c> from the project
/// folder, <c>dotnet exec</c> from <c>bin/</c>, an IDE, a double-clicked bundle) and are the root
/// cause this type exists to remove. <see cref="Resolve"/> deliberately takes no root argument: with
/// no input that can vary, no two callers can resolve different files, which closes the REQ-FN-034
/// defect class by construction rather than by convention.
/// </para>
/// </remarks>
public static class DataDirectory
{
    /// <summary>Configuration key holding an explicit data-directory override.</summary>
    /// <remarks>
    /// Settable as the environment variable <c>AppDb__DataDirectory</c>. This is the ONLY input that
    /// changes the resolved root, and exists for tests and for an operator relocating state onto
    /// another volume.
    /// </remarks>
    public const string ConfigKey = "AppDb:DataDirectory";

    /// <summary>Per-user folder name TechieDesk owns inside the OS application-data location.</summary>
    public const string ApplicationFolderName = "TechieDesk";

    /// <summary>Name of the app-relative folder used before REQ-FN-037, relocated on first launch.</summary>
    public const string LegacyDirectoryName = "data";

    /// <summary>File name of the app-owned database whose schema the DbUp migrator owns.</summary>
    public const string AppDbFileName = "techiedesk.db";

    /// <summary>File name of the SqliteVec vector database owned by the TechieRag library.</summary>
    public const string VectorDbFileName = "techierag.db";

    /// <summary>File name of the TechieRag conversation/workspace persistence store.</summary>
    public const string RagStoreFileName = "techiedesk-rag-store.db";

    /// <summary>File name of the saved runtime provider configuration.</summary>
    public const string ConfigFileName = "techierag-config.json";

    /// <summary>Sub-directory holding the persisted Data Protection key ring (REQ-NFR-004).</summary>
    public const string KeyRingDirectoryName = "keys";

    /// <summary>Sub-directory holding the daily rolling Serilog files (REQ-NFR-009).</summary>
    /// <remarks>
    /// Logs move with the data directory under REQ-FN-037. Written beside the executable they landed
    /// inside the read-only <c>.app</c> bundle, so a signed install would have produced no diagnostics
    /// at all — the one artefact whose absence hides every other failure.
    /// </remarks>
    public const string LogDirectoryName = "logs";

    /// <summary>Sub-directory holding downloaded update packages (REQ-FN-038b).</summary>
    /// <remarks>
    /// Declared here rather than inside the update service on purpose. REQ-FN-034 was a defect where
    /// a second component held its own opinion about where state lived, and REQ-FN-037 found the same
    /// shape again in the logger and in <c>AppDbConnectionFactory</c>. A downloaded installer is state,
    /// so its location belongs to the one authority like every other artefact. It also has to be here
    /// to be right: an update package written beside the executable would land inside the read-only
    /// <c>.app</c> bundle, which is the exact failure the Serilog fix removed.
    /// </remarks>
    public const string DownloadDirectoryName = "downloads";

    /// <summary>Sub-directory holding support-issue attachments staged for upload (REQ-UI-047).</summary>
    /// <remarks>
    /// Same rule, third time of asking. A screenshot dropped onto the Support screen is state, so it
    /// belongs to this one authority like the database and the logs. Staged beside the executable it
    /// would land inside the read-only <c>.app</c> bundle — the exact failure REQ-FN-037 removed from
    /// the logger, from <c>AppDbConnectionFactory</c>, and again from the update downloader. Nothing
    /// in the support screen may compute an attachment path any other way.
    /// </remarks>
    public const string SupportAttachmentsDirectoryName = "support-attachments";

    /// <summary>Gets the storage convention of the host this process is running on.</summary>
    public static DataDirectoryPlatform CurrentPlatform =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? DataDirectoryPlatform.MacOS
            : OperatingSystem.IsWindows() ? DataDirectoryPlatform.Windows
            : DataDirectoryPlatform.Unix;

    /// <summary>
    /// Builds the per-user data root for an explicit platform and explicit OS folders (REQ-FN-037).
    /// </summary>
    /// <param name="platform">The storage convention to apply.</param>
    /// <param name="homeDirectory">The user's home directory; used by macOS and Unix.</param>
    /// <param name="localApplicationDataDirectory">The Windows <c>%LOCALAPPDATA%</c> folder.</param>
    /// <returns>An absolute path to the per-user data root. The directory is not created.</returns>
    /// <remarks>
    /// Pure and total: it reads no ambient state, which is what lets one test assert BOTH the macOS
    /// and the Windows shape from a single host. <see cref="UserDataRoot()"/> is the thin ambient
    /// wrapper the application uses.
    /// </remarks>
    public static string UserDataRoot(
        DataDirectoryPlatform platform, string homeDirectory, string localApplicationDataDirectory)
    {
        var path = platform switch
        {
            DataDirectoryPlatform.MacOS =>
                Path.Combine(homeDirectory, "Library", "Application Support", ApplicationFolderName),
            DataDirectoryPlatform.Windows =>
                Path.Combine(localApplicationDataDirectory, ApplicationFolderName),
            _ => Path.Combine(homeDirectory, ".local", "share", ApplicationFolderName)
        };

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Builds the per-user data root for the host this process is running on.
    /// </summary>
    /// <returns>An absolute path to the per-user data root. The directory is not created.</returns>
    public static string UserDataRoot() =>
        UserDataRoot(CurrentPlatform, HomeDirectory(), LocalApplicationDataDirectory());

    /// <summary>
    /// Resolves the absolute data directory, preferring an explicit configured override.
    /// </summary>
    /// <param name="configuredDirectory">Value of <see cref="ConfigKey"/>, or null/blank when unset.</param>
    /// <returns>An absolute path to the data directory. The directory is not created.</returns>
    /// <remarks>
    /// There is intentionally no content-root or base-directory parameter. REQ-FN-034 required every
    /// caller to pass the same root and trusted them to; REQ-FN-037 removes the parameter, so passing
    /// a different one is no longer expressible.
    /// </remarks>
    public static string Resolve(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        return UserDataRoot();
    }

    /// <summary>
    /// Resolves the data directory and guarantees it exists.
    /// </summary>
    /// <param name="configuredDirectory">Value of <see cref="ConfigKey"/>, or null/blank when unset.</param>
    /// <returns>An absolute path to an existing data directory.</returns>
    public static string ResolveAndCreate(string? configuredDirectory)
    {
        var directory = Resolve(configuredDirectory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Gets the app-relative directory used before REQ-FN-037, for one-time relocation.
    /// </summary>
    /// <param name="contentRootPath">The content root the previous builds resolved against.</param>
    /// <returns>An absolute path to the legacy <c>data/</c> directory. It may not exist.</returns>
    public static string LegacyDataDirectory(string contentRootPath) =>
        Path.GetFullPath(Path.Combine(contentRootPath, LegacyDirectoryName));

    /// <summary>
    /// Builds the SQLite connection string for the app-owned database inside a data directory.
    /// </summary>
    /// <param name="dataDirectory">An absolute data directory, typically from <see cref="ResolveAndCreate"/>.</param>
    /// <returns>A SQLite connection string pointing at the app database.</returns>
    public static string AppDbConnectionString(string dataDirectory) =>
        $"Data Source={Path.Combine(dataDirectory, AppDbFileName)}";

    /// <summary>
    /// Builds the absolute path of the saved runtime provider configuration inside a data directory
    /// (REQ-FN-052).
    /// </summary>
    /// <param name="dataDirectory">An absolute data directory, typically from <see cref="ResolveAndCreate"/>.</param>
    /// <returns>An absolute path to <c>techierag-config.json</c>.</returns>
    /// <remarks>
    /// <para>
    /// The exact counterpart of <see cref="AppDbConnectionString"/> and
    /// <see cref="VectorDbConnectionString"/>, and it exists for the same reason they do. The writer
    /// (<c>TechieRagConfigService</c>, driven by the LLM Settings screen) and the reader
    /// (<c>TechieRagManager.CreateInstanceFromConfigAsync</c>, which builds the running RAG instance)
    /// each used to spell <c>Path.Combine(&lt;their own root&gt;, ConfigFileName)</c> for themselves.
    /// Both happened to agree, but nothing made them agree — which is precisely the REQ-FN-034 defect
    /// class, and REQ-FN-052 required it to be closed by construction rather than by coincidence.
    /// </para>
    /// <para>
    /// Nothing outside this method may compute the configuration file path. A test asserts the writer
    /// and the reader return the same string from this one helper.
    /// </para>
    /// </remarks>
    public static string ConfigFilePath(string dataDirectory) =>
        Path.Combine(dataDirectory, ConfigFileName);

    /// <summary>
    /// Builds the SQLite connection string for the vector database inside a data directory
    /// (REQ-FN-048).
    /// </summary>
    /// <param name="dataDirectory">An absolute data directory, typically from <see cref="ResolveAndCreate"/>.</param>
    /// <returns>A SQLite connection string pointing at the vector database.</returns>
    /// <remarks>
    /// The exact counterpart of <see cref="AppDbConnectionString"/>. REQ-FN-034 pinned the app
    /// database to this one directory and left the OTHER database — the SqliteVec store — carrying
    /// the relative literal <c>Data Source=techierag.db</c>, which resolves against the process
    /// working directory. On the desktop head that directory is the <c>.app</c> bundle root, so user
    /// embeddings were written INSIDE a signed application bundle and <c>codesign</c> then rejected
    /// it outright ("unsealed contents present in the bundle root").
    /// </remarks>
    public static string VectorDbConnectionString(string dataDirectory) =>
        $"Data Source={Path.Combine(dataDirectory, VectorDbFileName)}";

    /// <summary>
    /// Rewrites a SQLite connection string so its database file is absolute and inside the data
    /// directory (REQ-FN-048).
    /// </summary>
    /// <param name="connectionString">
    /// A saved connection string, a bare file path, or null/blank for the default.
    /// </param>
    /// <param name="dataDirectory">An absolute data directory, typically from <see cref="ResolveAndCreate"/>.</param>
    /// <param name="defaultFileName">
    /// The artefact's file name, used when the connection string names no file — normally
    /// <see cref="VectorDbFileName"/>.
    /// </param>
    /// <returns>
    /// A connection string whose data source is an absolute path. Returned unchanged when it already
    /// is absolute, and when it names a non-file source (<c>:memory:</c> or a <c>file:</c> URI).
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the migration seam for acceptance clause (2): a configuration written by an earlier
    /// build carries the relative default, and it is corrected on load rather than trusted. Compare
    /// the result against the input to detect that a rewrite happened and needs persisting.
    /// </para>
    /// <para>
    /// A relative path that begins with the pre-REQ-FN-037 <see cref="LegacyDirectoryName"/> segment
    /// has that segment dropped, because that folder's whole contents were relocated INTO the data
    /// directory by <see cref="RelocateLegacyDataDirectory"/>; keeping it would resolve to a
    /// <c>data/data/</c> nesting that holds nothing.
    /// </para>
    /// </remarks>
    public static string ResolveSqliteConnectionString(
        string? connectionString, string dataDirectory, string defaultFileName)
    {
        var defaultPath = Path.Combine(dataDirectory, defaultFileName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return $"Data Source={defaultPath}";
        }

        // A bare path with no keyword at all — what the Settings screen's "Database File Path" field
        // produces if it is ever handed through unwrapped.
        if (!connectionString.Contains('=', StringComparison.Ordinal))
        {
            var bare = AbsoluteSqlitePath(connectionString, dataDirectory, defaultPath);
            return bare is null ? connectionString : bare;
        }

        var segments = connectionString.Split(';');
        var rewritten = false;
        for (var index = 0; index < segments.Length; index++)
        {
            var separator = segments[index].IndexOf('=', StringComparison.Ordinal);
            if (separator < 0 || !IsDataSourceKeyword(segments[index][..separator]))
            {
                continue;
            }

            var absolute = AbsoluteSqlitePath(
                segments[index][(separator + 1)..], dataDirectory, defaultPath);
            if (absolute is null)
            {
                continue;
            }

            segments[index] = $"{segments[index][..separator].Trim()}={absolute}";
            rewritten = true;
        }

        return rewritten ? string.Join(';', segments) : connectionString;
    }

    /// <summary>
    /// Resolves the directory the rolling log files live in, and guarantees it exists (REQ-NFR-009).
    /// </summary>
    /// <param name="dataDirectory">An absolute data directory, typically from <see cref="ResolveAndCreate"/>.</param>
    /// <returns>An absolute path to an existing log directory.</returns>
    /// <remarks>
    /// Exists so no host can spell the log location itself. REQ-FN-034 claimed no path resolves
    /// against the working directory while the migration console still passed Serilog the bare
    /// relative <c>logs/techiedeskdb-.log</c>, which dropped log files wherever the console happened
    /// to be invoked from — the repository root, in practice (REQ-FN-048).
    /// </remarks>
    public static string ResolveAndCreateLogDirectory(string dataDirectory)
    {
        var logDirectory = Path.Combine(dataDirectory, LogDirectoryName);
        Directory.CreateDirectory(logDirectory);
        return logDirectory;
    }

    /// <summary>Determines whether a connection-string keyword names the SQLite database file.</summary>
    /// <param name="keyword">The raw keyword text, possibly padded with spaces.</param>
    /// <returns>True when the keyword selects the database file.</returns>
    private static bool IsDataSourceKeyword(string keyword)
    {
        var trimmed = keyword.Trim();
        return trimmed.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Filename", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Makes one SQLite data-source value absolute inside the data directory.</summary>
    /// <param name="value">The raw data-source value, possibly padded with spaces.</param>
    /// <param name="dataDirectory">The absolute data directory.</param>
    /// <param name="defaultPath">The absolute path used when the value names no file.</param>
    /// <returns>The absolute path, or null when the value must be left exactly as it is.</returns>
    private static string? AbsoluteSqlitePath(string value, string dataDirectory, string defaultPath)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return defaultPath;
        }

        // Not a file: an in-memory database, or a URI SQLite parses itself. Rewriting either would
        // change what the caller asked for.
        if (trimmed.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Path.IsPathRooted(trimmed))
        {
            return null;
        }

        var relative = trimmed.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var legacyPrefix = LegacyDirectoryName + Path.DirectorySeparatorChar;
        if (relative.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[legacyPrefix.Length..];
        }

        return Path.GetFullPath(Path.Combine(dataDirectory, relative));
    }

    /// <summary>
    /// Moves a legacy artefact that predates this type into the data directory, once.
    /// </summary>
    /// <remarks>
    /// Earlier builds wrote the vector database and the saved provider configuration beside the
    /// executable rather than under the data directory. Relocating them preserves an existing
    /// install's embeddings and provider settings instead of silently orphaning them. A destination
    /// that already exists always wins and the legacy file is left untouched for the operator to
    /// remove.
    /// </remarks>
    /// <param name="legacyPath">Absolute path the artefact used to occupy.</param>
    /// <param name="currentPath">Absolute path it should occupy now.</param>
    /// <returns>True when a file was relocated; otherwise false.</returns>
    public static bool RelocateLegacyArtefact(string legacyPath, string currentPath)
    {
        if (!File.Exists(legacyPath) || File.Exists(currentPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(legacyPath, currentPath);
        return true;
    }

    /// <summary>
    /// Moves an entire app-relative <c>data/</c> directory into the per-user data directory, once
    /// (REQ-FN-037).
    /// </summary>
    /// <param name="legacyDirectory">Absolute path of the old app-relative directory.</param>
    /// <param name="currentDirectory">Absolute path of the per-user data directory.</param>
    /// <returns>
    /// The relative paths actually moved, in enumeration order; empty when there was nothing to move.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Losing nothing is the acceptance criterion, so every file at any depth is carried across —
    /// the app database, the RAG store, the vector database, the saved provider configuration AND
    /// the <c>keys/</c> key ring, without which every encrypted API key becomes unreadable.
    /// </para>
    /// <para>
    /// Per-file semantics are exactly <see cref="RelocateLegacyArtefact"/>: an existing destination
    /// always wins and its legacy counterpart is left on disk for the operator rather than
    /// overwriting live data. Emptied directories are removed so nothing is orphaned; a directory
    /// still holding a skipped file is deliberately left behind as the evidence of that skip.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> RelocateLegacyDataDirectory(
        string legacyDirectory, string currentDirectory)
    {
        var moved = new List<string>();
        var legacyRoot = Path.GetFullPath(legacyDirectory);
        var currentRoot = Path.GetFullPath(currentDirectory);
        if (!Directory.Exists(legacyRoot) || PathsMatch(legacyRoot, currentRoot))
        {
            return moved;
        }

        foreach (var legacyFile in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(legacyRoot, legacyFile);
            if (RelocateLegacyArtefact(legacyFile, Path.Combine(currentRoot, relativePath)))
            {
                moved.Add(relativePath);
            }
        }

        RemoveEmptyDirectories(legacyRoot);
        return moved;
    }

    /// <summary>Gets the user's home directory, never returning blank.</summary>
    /// <returns>An absolute home directory path.</returns>
    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }

        return string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home;
    }

    /// <summary>Gets the Windows local application-data directory, never returning blank.</summary>
    /// <returns>An absolute local application-data path.</returns>
    private static string LocalApplicationDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData) ? HomeDirectory() : localAppData;
    }

    /// <summary>Compares two absolute paths using the host file system's case sensitivity.</summary>
    /// <param name="left">First absolute path.</param>
    /// <param name="right">Second absolute path.</param>
    /// <returns>True when both denote the same directory.</returns>
    private static bool PathsMatch(string left, string right)
    {
        var comparison = CurrentPlatform == DataDirectoryPlatform.Unix
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar),
            comparison);
    }

    /// <summary>Deletes a directory tree's empty directories, bottom-up, including the root.</summary>
    /// <param name="directory">Absolute path of the directory to prune.</param>
    private static void RemoveEmptyDirectories(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory).ToList())
        {
            RemoveEmptyDirectories(child);
        }

        if (Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return;
        }

        try
        {
            Directory.Delete(directory);
        }
        catch (IOException)
        {
            // A locked or in-use legacy directory is not worth failing a launch over; the files it
            // held have already moved, so the only cost is an empty folder left behind.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale: a read-only install location (the .app bundle) cannot be pruned.
        }
    }
}
