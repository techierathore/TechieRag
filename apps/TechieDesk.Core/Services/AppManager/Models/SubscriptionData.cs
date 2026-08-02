namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// One of the current user's subscriptions from <c>GET /PaymentSvc/subscriptions</c>
/// (REQ-UI-030 / BRD-77).
/// </summary>
public sealed class SubscriptionData
{
    /// <summary>Gets or sets the subscription identifier.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Gets or sets the plan display name (e.g. <c>Professional Monthly</c>).</summary>
    public string? PlanName { get; set; }

    /// <summary>Gets or sets the subscription status (e.g. <c>Active</c>, <c>Cancelled</c>).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the billing cycle (e.g. <c>Monthly</c>, <c>Yearly</c>).</summary>
    public string? BillingCycle { get; set; }

    /// <summary>Gets or sets the recurring amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the ISO currency code.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Gets or sets when the subscription started.</summary>
    public DateTimeOffset StartDate { get; set; }

    /// <summary>Gets or sets the end of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subscription is set to end when the current
    /// period closes rather than renewing.
    /// </summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>Gets or sets the next billing date, when the subscription will renew.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
