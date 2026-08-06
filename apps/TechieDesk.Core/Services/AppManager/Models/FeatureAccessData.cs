namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Payload of <c>GET /FeatureSvc/{aFeatureCode}</c> — access status for a single feature
/// (binary or level) for the current user.
/// </summary>
public sealed class FeatureAccessData
{
    /// <summary>Gets or sets the feature code (e.g. <c>EXPORT_PDF</c>).</summary>
    public string FeatureCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the feature display name.</summary>
    public string? FeatureName { get; set; }

    /// <summary>Gets or sets the feature type (<c>Binary</c> or <c>Level</c>).</summary>
    public string? FeatureType { get; set; }

    /// <summary>Gets or sets a value indicating whether the user has access.</summary>
    public bool HasAccess { get; set; }

    /// <summary>Gets or sets where the access decision came from (e.g. <c>license</c>, <c>featureFlag</c>).</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the denial reason when access is not granted.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets the granted level for level-type features.</summary>
    public int? Level { get; set; }

    /// <summary>Gets or sets the human-readable level description.</summary>
    public string? LevelDescription { get; set; }

    /// <summary>Gets or sets the license tier required to unlock a denied feature.</summary>
    public string? RequiredLicense { get; set; }
}
