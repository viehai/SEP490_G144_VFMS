namespace VFMS.Contracts.Operations;

/// <summary>
/// How many items a work board has in each status, for the counts on its status filter ("Mới báo (3)").
/// Every status of the board is listed, with 0 when it has none.
/// </summary>
public sealed record StatusCountsResponse(IReadOnlyDictionary<string, int> Counts)
{
    public int Total => Counts.Values.Sum();
}

/// <summary>
/// Filter values that cover several statuses at once, for the work lists:
/// <see cref="OpenIssues"/> = Reported, InProgress and Escalated issues; <see cref="ActiveOrders"/> = orders not yet Completed or Cancelled.
/// </summary>
public static class StatusFilters
{
    public const string OpenIssues = "Open";
    public const string ActiveOrders = "Active";
    public static readonly string[] OpenIssueStatuses = ["Reported", "InProgress", "Escalated"];
    public static readonly string[] ActiveOrderStatuses = ["Pending", "Processing", "ReadyForPickup", "OutForDelivery"];
}
