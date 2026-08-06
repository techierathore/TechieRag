using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Data;

/// <summary>
/// Guards the single-data-directory invariant introduced by REQ-FN-034 and relocated to the
/// per-user OS directory by REQ-FN-037.
/// </summary>
[Collection(WorkingDirectoryCollection.Name)]
public class DataDirectoryTests
{
    /// <summary>
    /// The migrator's default and the resolver agree on one file. Before REQ-FN-034 the migrator
    /// resolved a CWD-relative path while the app resolved a BaseDirectory-relative one, so DbUp
    /// migrated a database the app never opened and both reported success.
    /// </summary>
    [Fact]
    public void MigratorDefaultMatchesTheResolvedAppDatabase()
    {
        var resolved = DataDirectory.AppDbConnectionString(DataDirectory.Resolve(configuredDirectory: null));

        Assert.Equal(resolved, MigrationRunner.DefaultSqliteConnectionString);
    }

    /// <summary>
    /// No default path is ever resolved against the current working directory, which is what
    /// varies between `dotnet run`, `dotnet exec` from bin/, an IDE, and a double-clicked bundle.
    /// </summary>
    [Fact]
    public void DefaultResolutionIsAbsoluteAndIndependentOfWorkingDirectory()
    {
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var fromTemp = DataDirectory.Resolve(configuredDirectory: null);

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var fromBaseDirectory = DataDirectory.Resolve(configuredDirectory: null);

            Assert.True(Path.IsPathRooted(fromTemp));
            Assert.Equal(fromBaseDirectory, fromTemp);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    /// <summary>
    /// REQ-FN-037: the default lands in the per-user OS directory and NOT inside the application's
    /// own folder. The desktop head's content root is inside a signed, read-only .app bundle, so an
    /// app-relative root worked from bin/ and would have failed on every real install.
    /// </summary>
    [Fact]
    public void DefaultResolutionIsThePerUserDirectoryAndNotTheApplicationFolder()
    {
        var resolved = DataDirectory.Resolve(configuredDirectory: null);

        Assert.Equal(DataDirectory.UserDataRoot(), resolved);
        Assert.False(
            resolved.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase),
            $"The data directory must not sit inside the application folder, but resolved to {resolved}.");
        Assert.EndsWith(DataDirectory.ApplicationFolderName, resolved, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-037 acceptance, macOS half: every artefact resolves under
    /// ~/Library/Application Support/TechieDesk. Asserted through the explicit-platform overload so
    /// it holds on any build host rather than only when the tests happen to run on a Mac.
    /// </summary>
    [Fact]
    public void MacOsResolvesUnderApplicationSupport()
    {
        var root = DataDirectory.UserDataRoot(
            DataDirectoryPlatform.MacOS,
            homeDirectory: "/Users/tester",
            localApplicationDataDirectory: @"C:\Users\tester\AppData\Local");

        Assert.Equal("/Users/tester/Library/Application Support/TechieDesk", root);
    }

    /// <summary>
    /// REQ-FN-037 acceptance, Windows half: every artefact resolves under %LOCALAPPDATA%\TechieDesk.
    /// The home directory is deliberately supplied too and must be ignored on Windows.
    /// </summary>
    [Fact]
    public void WindowsResolvesUnderLocalApplicationData()
    {
        var localAppData = Path.Combine("Z:", "profiles", "tester", "AppData", "Local");

        var root = DataDirectory.UserDataRoot(
            DataDirectoryPlatform.Windows,
            homeDirectory: Path.Combine("Z:", "profiles", "tester"),
            localApplicationDataDirectory: localAppData);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localAppData, DataDirectory.ApplicationFolderName)), root);
    }

    /// <summary>
    /// Linux and other Unix hosts follow the XDG convention rather than borrowing either of the
    /// two named platforms' shapes.
    /// </summary>
    [Fact]
    public void UnixResolvesUnderXdgDataHome()
    {
        var root = DataDirectory.UserDataRoot(
            DataDirectoryPlatform.Unix,
            homeDirectory: "/home/tester",
            localApplicationDataDirectory: "/ignored");

        Assert.Equal("/home/tester/.local/share/TechieDesk", root);
    }

    /// <summary>
    /// An explicit override wins over the per-user directory, which is how a test or an operator
    /// relocating state onto another volume points every artefact elsewhere via AppDb__DataDirectory.
    /// </summary>
    [Fact]
    public void ConfiguredDirectoryOverridesThePerUserDirectory()
    {
        var overridePath = Path.Combine(Path.GetTempPath(), "techiedesk-override");

        var resolved = DataDirectory.Resolve(overridePath);

        Assert.Equal(Path.GetFullPath(overridePath), resolved);
        Assert.NotEqual(DataDirectory.UserDataRoot(), resolved);
    }

    /// <summary>
    /// Every persistent artefact resolves into the same directory — the whole point of REQ-FN-034,
    /// now including the log files REQ-FN-037 moved out of the read-only application bundle.
    /// </summary>
    [Fact]
    public void EveryArtefactSharesOneDirectory()
    {
        var dataDirectory = DataDirectory.Resolve(configuredDirectory: "/root/techiedesk");

        var paths = new[]
        {
            Path.Combine(dataDirectory, DataDirectory.AppDbFileName),
            Path.Combine(dataDirectory, DataDirectory.VectorDbFileName),
            Path.Combine(dataDirectory, DataDirectory.RagStoreFileName),
            Path.Combine(dataDirectory, DataDirectory.ConfigFileName),
            Path.Combine(dataDirectory, DataDirectory.KeyRingDirectoryName),
            Path.Combine(dataDirectory, DataDirectory.LogDirectoryName)
        };

        Assert.All(paths, path => Assert.Equal(dataDirectory, Path.GetDirectoryName(path)));
    }

    /// <summary>
    /// A legacy artefact left beside the executable is moved into the data directory once, so an
    /// existing install keeps its embeddings and provider settings instead of silently losing them.
    /// </summary>
    [Fact]
    public void LegacyArtefactIsRelocatedOnce()
    {
        var root = CreateSandbox();
        try
        {
            var legacy = Path.Combine(root, DataDirectory.VectorDbFileName);
            var current = Path.Combine(root, "current", DataDirectory.VectorDbFileName);
            File.WriteAllText(legacy, "vectors");

            Assert.True(DataDirectory.RelocateLegacyArtefact(legacy, current));
            Assert.False(File.Exists(legacy));
            Assert.Equal("vectors", File.ReadAllText(current));

            Assert.False(DataDirectory.RelocateLegacyArtefact(legacy, current));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// An existing destination always wins; the legacy file is left for the operator rather than
    /// overwriting live data.
    /// </summary>
    [Fact]
    public void RelocationNeverOverwritesAnExistingArtefact()
    {
        var root = CreateSandbox();
        try
        {
            var legacy = Path.Combine(root, DataDirectory.VectorDbFileName);
            var current = Path.Combine(root, "current", DataDirectory.VectorDbFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(legacy, "stale");
            File.WriteAllText(current, "live");

            Assert.False(DataDirectory.RelocateLegacyArtefact(legacy, current));
            Assert.Equal("live", File.ReadAllText(current));
            Assert.True(File.Exists(legacy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The legacy root is the app-relative <c>data/</c> folder every pre-REQ-FN-037 install carries.
    /// </summary>
    [Fact]
    public void LegacyRootIsTheAppRelativeDataFolder()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "TechieDesk.app", "Contents", "MonoBundle");

        var legacy = DataDirectory.LegacyDataDirectory(contentRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "data")), legacy);
    }

    /// <summary>
    /// REQ-FN-037 acceptance: an install carrying the old app-relative data/ directory has it moved
    /// once, LOSING NOTHING. Every file present before is present after with byte-identical content,
    /// at any depth — the keys/ key ring included, without which every encrypted API key would become
    /// unreadable — and the emptied legacy directory is removed so nothing is orphaned.
    /// </summary>
    [Fact]
    public void LegacyDataDirectoryRelocatesLosingNothing()
    {
        var root = CreateSandbox();
        try
        {
            var legacy = Path.Combine(root, "MonoBundle", "data");
            var current = Path.Combine(root, "Application Support", "TechieDesk");
            Directory.CreateDirectory(Path.Combine(legacy, DataDirectory.KeyRingDirectoryName));
            Directory.CreateDirectory(current);

            var before = new Dictionary<string, string>
            {
                [DataDirectory.AppDbFileName] = "app database",
                [DataDirectory.VectorDbFileName] = "vectors",
                [DataDirectory.RagStoreFileName] = "threads and workspaces",
                [DataDirectory.ConfigFileName] = "{\"apiKey\":\"encrypted\"}",
                [Path.Combine(DataDirectory.KeyRingDirectoryName, "key-ring.xml")] = "<key/>"
            };

            foreach (var artefact in before)
            {
                File.WriteAllText(Path.Combine(legacy, artefact.Key), artefact.Value);
            }

            var moved = DataDirectory.RelocateLegacyDataDirectory(legacy, current);

            Assert.Equal(before.Count, moved.Count);
            foreach (var artefact in before)
            {
                var relocated = Path.Combine(current, artefact.Key);
                Assert.True(File.Exists(relocated), $"{artefact.Key} was lost in the relocation.");
                Assert.Equal(artefact.Value, File.ReadAllText(relocated));
            }

            Assert.False(Directory.Exists(legacy), "The emptied legacy directory must not be orphaned.");

            // Relocation happens ONCE: a second launch finds nothing to move and disturbs nothing.
            Assert.Empty(DataDirectory.RelocateLegacyDataDirectory(legacy, current));
            Assert.Equal(before.Count, Directory
                .EnumerateFiles(current, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A file already live in the per-user directory is never overwritten by its legacy counterpart,
    /// and that counterpart is deliberately left on disk as the evidence of the skip rather than
    /// deleted behind the operator's back.
    /// </summary>
    [Fact]
    public void LegacyDataDirectoryRelocationNeverOverwritesLiveFiles()
    {
        var root = CreateSandbox();
        try
        {
            var legacy = Path.Combine(root, "data");
            var current = Path.Combine(root, "TechieDesk");
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(legacy, DataDirectory.AppDbFileName), "stale");
            File.WriteAllText(Path.Combine(legacy, DataDirectory.VectorDbFileName), "vectors");
            File.WriteAllText(Path.Combine(current, DataDirectory.AppDbFileName), "live");

            var moved = DataDirectory.RelocateLegacyDataDirectory(legacy, current);

            Assert.Equal(new[] { DataDirectory.VectorDbFileName }, moved);
            Assert.Equal("live", File.ReadAllText(Path.Combine(current, DataDirectory.AppDbFileName)));
            Assert.Equal("stale", File.ReadAllText(Path.Combine(legacy, DataDirectory.AppDbFileName)));
            Assert.True(Directory.Exists(legacy), "A directory still holding a skipped file must survive.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Relocation is inert when there is nothing to relocate: a missing legacy directory, and the
    /// degenerate case where the override points the data directory AT the legacy one, which must
    /// never move a live file onto itself.
    /// </summary>
    [Fact]
    public void LegacyDataDirectoryRelocationIsInertWhenThereIsNothingToMove()
    {
        var root = CreateSandbox();
        try
        {
            var current = Path.Combine(root, "TechieDesk");
            Directory.CreateDirectory(current);

            Assert.Empty(DataDirectory.RelocateLegacyDataDirectory(
                Path.Combine(root, "never-existed"), current));

            File.WriteAllText(Path.Combine(current, DataDirectory.AppDbFileName), "live");
            Assert.Empty(DataDirectory.RelocateLegacyDataDirectory(current, current));
            Assert.Equal("live", File.ReadAllText(Path.Combine(current, DataDirectory.AppDbFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Creates an empty throwaway directory under the system temp folder.</summary>
    /// <returns>The absolute path of the created sandbox.</returns>
    private static string CreateSandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), $"techiedesk-relocate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
