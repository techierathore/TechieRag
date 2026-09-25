using System.Globalization;
using TechieRag.Local.Runtime;

namespace TechieRag.Local.HuggingFace;

/// <summary>
/// What a Hugging Face name resolved to: the snapshot at one commit, its file set and its folder in the
/// model root; and the pin that makes the resolution happen once (REQ-RAG-108 / BRD-166).
/// </summary>
/// <param name="Snapshot">The licence, commit and files.</param>
/// <param name="Variant">The file set a download fetches and a check verifies.</param>
/// <param name="Directory">The folder the files live in.</param>
internal sealed record HuggingFaceResolution(HuggingFaceSnapshot Snapshot, LocalModelVariant Variant, string Directory)
{
    /// <summary>
    /// Builds the resolution for a snapshot under a name's model root.
    /// </summary>
    /// <param name="source">The name.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The resolution.</returns>
    public static HuggingFaceResolution For(HuggingFaceModelSource source, HuggingFaceSnapshot snapshot) =>
        new(snapshot, snapshot.ToVariant(source.FolderName(snapshot.Commit)), source.GetDirectory(snapshot.Commit));

    /// <summary>
    /// Restores a model's resolution from disk, without the network: the commit is the version itself
    /// when it is a commit id, else the recorded pin; the licence and files are that commit folder's
    /// manifest. When the files are also on disk, the model's limits are read from its configuration.
    /// </summary>
    /// <param name="model">A model created from a Hugging Face name.</param>
    /// <returns>True when the model is now resolved.</returns>
    public static bool TryRestore(LocalModel model)
    {
        var source = model.HuggingFace;
        if (source is null)
        {
            return false;
        }

        var commit = source.IsExactCommit ? source.Version : ReadPin(source);
        if (commit is null)
        {
            return false;
        }

        var snapshot = HuggingFaceSnapshot.TryReadManifest(source.GetDirectory(commit), source, commit);
        if (snapshot is null)
        {
            return false;
        }

        var resolution = For(source, snapshot);
        model.ApplyResolution(resolution);
        if (resolution.Variant.IsOnDisk(resolution.Directory))
        {
            TryApplyConfig(model, resolution.Directory);
        }

        return true;
    }

    /// <summary>Records the commit a name resolved to, unless the name is itself a commit.</summary>
    /// <param name="source">The name.</param>
    /// <param name="commit">The commit.</param>
    public static void WritePin(HuggingFaceModelSource source, string commit)
    {
        if (source.IsExactCommit)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(source.Root);
        var line = string.Create(CultureInfo.InvariantCulture, $"{commit} {source.Id}");
        File.WriteAllText(source.PinPath + ".tmp", line);
        File.Move(source.PinPath + ".tmp", source.PinPath, overwrite: true);
    }

    /// <summary>Reads the commit a name was pinned to, when the pin is for exactly this name.</summary>
    /// <param name="source">The name.</param>
    /// <returns>The commit, or null.</returns>
    public static string? ReadPin(HuggingFaceModelSource source)
    {
        if (!File.Exists(source.PinPath))
        {
            return null;
        }

        var parts = File.ReadAllText(source.PinPath).Trim().Split(' ', 2);
        return parts.Length == 2
               && HuggingFaceModelSource.IsCommit(parts[0])
               && string.Equals(parts[1], source.Id, StringComparison.OrdinalIgnoreCase)
            ? parts[0]
            : null;
    }

    private static void TryApplyConfig(LocalModel model, string directory)
    {
        try
        {
            model.ApplyGenAiConfig(GenAiConfigInfo.Read(directory));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // Read again, with its error surfaced, when the model loads.
        }
    }
}
