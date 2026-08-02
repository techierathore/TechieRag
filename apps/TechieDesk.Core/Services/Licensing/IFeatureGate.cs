namespace TechieDesk.Services.Licensing;

/// <summary>
/// Feature gating over AppManager FeatureSvc (binary + level features, REQ-FN-014/BRD-50).
/// Gates on the license tier, not on a role: the retired role/capability matrix (REQ-FN-041) has
/// no successor. In offline single-user mode it resolves against the local Free tier; when the license grace
/// window has expired premium features are denied. Scoped per circuit; results are memoized.
/// </summary>
public interface IFeatureGate
{
    /// <summary>Returns whether the current user may use a feature.</summary>
    /// <param name="featureCode">The feature code (e.g. <c>CONNECTORS</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsEnabledAsync(string featureCode, CancellationToken cancellationToken = default);

    /// <summary>Returns the granted level for a level-type feature, or null when denied/binary.</summary>
    /// <param name="featureCode">The feature code (e.g. <c>API_ACCESS</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int?> GetLevelAsync(string featureCode, CancellationToken cancellationToken = default);

    /// <summary>Returns the full <see cref="FeatureDecision"/> for a feature.</summary>
    /// <param name="featureCode">The feature code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FeatureDecision> EvaluateAsync(string featureCode, CancellationToken cancellationToken = default);
}
