namespace TechieRag.Local.Tests;

/// <summary>
/// Finds files of the repository (the folder holding <c>TechieRag.slnx</c>) from the test's output folder.
/// </summary>
internal static class RepoFiles
{
    /// <summary>Gets the full path of a repository file.</summary>
    /// <param name="relativePath">The path from the repository root, with forward slashes.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="FileNotFoundException">No folder above the test output holds the file.</exception>
    public static string Locate(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} above {AppContext.BaseDirectory}.");
    }

    /// <summary>Reads a repository file.</summary>
    /// <param name="relativePath">The path from the repository root.</param>
    /// <returns>The text.</returns>
    public static string Read(string relativePath) => File.ReadAllText(Locate(relativePath));
}
