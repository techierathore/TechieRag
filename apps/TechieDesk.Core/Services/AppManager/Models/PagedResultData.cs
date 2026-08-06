namespace TechieDesk.Services.AppManager.Models;

/// <summary>
/// Generic paged envelope returned by the paginated PaymentSvc endpoints
/// (<c>GET /PaymentSvc/transactions</c>, <c>GET /PaymentSvc/invoices</c>).
/// </summary>
/// <typeparam name="TItem">The item type carried in <see cref="Items"/>.</typeparam>
public sealed class PagedResultData<TItem>
{
    /// <summary>Gets or sets the items on the current page.</summary>
    public List<TItem> Items { get; set; } = new();

    /// <summary>Gets or sets the total number of items across every page.</summary>
    public int TotalCount { get; set; }

    /// <summary>Gets or sets the one-based page number this result represents.</summary>
    public int Page { get; set; }

    /// <summary>Gets or sets the page size used for this result.</summary>
    public int PageSize { get; set; }

    /// <summary>Gets or sets the total number of pages available.</summary>
    public int TotalPages { get; set; }
}
