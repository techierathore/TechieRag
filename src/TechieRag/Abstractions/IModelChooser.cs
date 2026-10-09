using TechieRag.Llm;

namespace TechieRag.Abstractions;

/// <summary>
/// Chooses which model does a piece of work (REQ-RAG-128 / BRD-188, Chatur feedback TR-RAG-006).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A host that routes work to several models depends on this interface rather
/// than on the concrete <see cref="ModelChooser"/>, so a unit test can hand it a fixed choice and
/// never call a model.</para>
/// <para><b>Code Flow:</b> <see cref="ModelChooser"/> implements it with a small model; its remarks
/// are the authoritative behaviour.</para>
/// </remarks>
public interface IModelChooser
{
    /// <summary>Chooses one of the candidate models for a request.</summary>
    /// <param name="request">The work to be done, as the user or the host wrote it.</param>
    /// <param name="candidates">The models to choose from; at least one, ids distinct.</param>
    /// <param name="cancellationToken">Cancels the choice.</param>
    /// <returns>The chosen candidate, the reason, and how it was chosen.</returns>
    Task<ModelChoice> ChooseAsync(
        string request,
        IReadOnlyList<ModelCandidate> candidates,
        CancellationToken cancellationToken = default);
}
