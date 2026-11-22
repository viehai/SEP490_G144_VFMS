namespace VFMS.Contracts.Operations;

/// <summary>
/// How many items a work board has in each status, for the counts on its status filter ("Mới báo (3)").
/// Every status of the board is listed, with 0 when it has none.
/// </summary>
public sealed record StatusCountsResponse(IReadOnlyDictionary<string, int> Counts)
{
    public int Total => Counts.Values.Sum();
}
