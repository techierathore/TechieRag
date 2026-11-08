using System.Text.Json;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// Reads the git-ignored <c>livetests.settings.json</c> that sits beside the tests
/// (<c>tests/TechieRag.Tests/livetests.settings.json</c>; REQ-RAG-152 / BRD-210).
/// </summary>
/// <remarks>
/// <para><b>A settings file, never an environment variable</b> (owner's rule, 2026-11-07). The file is
/// written by the harness that starts the server (<c>tests/TechieRag.Tests/livetests/start-postgres.sh</c>),
/// with the password that harness chose, and is git-ignored; <c>livetests.settings.example.json</c> beside it
/// is the committed shape.</para>
/// <para><b>Found by walking up from the test binaries.</b> An xUnit discovery attribute runs before any
/// configuration binder exists, so the file is read directly: first in the output folder, then in each parent
/// folder, which reaches the test project folder from <c>bin/&lt;configuration&gt;/&lt;framework&gt;</c>. No copy
/// step, so a deleted settings file cannot survive as a stale copy in <c>bin</c>.</para>
/// </remarks>
public static class LiveTestSettings
{
    /// <summary>The settings file name.</summary>
    public const string FileName = "livetests.settings.json";

    private static readonly Lazy<string?> PostgresValue = new(ReadPostgres);

    /// <summary>Gets the PostgreSQL connection string, or null when no settings file names one.</summary>
    public static string? PostgresConnectionString => PostgresValue.Value;

    /// <summary>Gets the settings file found for this run, or null when there is none.</summary>
    public static string? SettingsPath => FindSettingsFile(AppContext.BaseDirectory);

    /// <summary>Finds the settings file at or above a folder.</summary>
    /// <param name="startFolder">The folder to start from.</param>
    /// <returns>The full path, or null when no folder up to the root holds one.</returns>
    public static string? FindSettingsFile(string startFolder)
    {
        for (var folder = new DirectoryInfo(startFolder); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Reads <c>Postgres:ConnectionString</c> from a settings file's JSON.</summary>
    /// <param name="json">The file's text.</param>
    /// <returns>The connection string, or null when the file does not name a non-empty one.</returns>
    public static string? ParsePostgres(string json) => ParseValue(json, "Postgres", "ConnectionString");

    /// <summary>Reads <c>Section:Key</c> from a settings file's JSON.</summary>
    /// <param name="json">The file's text.</param>
    /// <param name="section">The section, for example <c>ImageGeneration</c>.</param>
    /// <param name="key">The key, for example <c>OpenAIKey</c>.</param>
    /// <returns>The value, or null when the file does not name a non-empty string there.</returns>
    public static string? ParseValue(string json, string section, string key)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return document.RootElement.TryGetProperty(section, out var node)
            && node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
                ? text
                : null;
    }

    /// <summary>Reads <c>Section:Key</c> from the settings file, when there is one.</summary>
    /// <param name="section">The section, for example <c>ImageGeneration</c>.</param>
    /// <param name="key">The key, for example <c>OpenAIKey</c>.</param>
    /// <returns>The value, or null.</returns>
    public static string? Get(string section, string key)
    {
        var path = SettingsPath;
        return path is null ? null : ParseValue(File.ReadAllText(path), section, key);
    }

    /// <summary>Reads the PostgreSQL connection string from the settings file, when there is one.</summary>
    /// <returns>The connection string, or null.</returns>
    private static string? ReadPostgres() => Get("Postgres", "ConnectionString");
}
