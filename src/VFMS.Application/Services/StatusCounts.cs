using VFMS.Contracts.Operations;

namespace VFMS.Application.Services;

/// <summary>Turns per-status counts from the database into a response that lists every status.</summary>
internal static class StatusCounts
{
    public static StatusCountsResponse From<TStatus>(IReadOnlyDictionary<TStatus, int> counts, params TStatus[] leaveOut)
        where TStatus : struct, Enum =>
        new(Enum.GetValues<TStatus>()
            .Where(status => !leaveOut.Contains(status))
            .ToDictionary(status => status.ToString(), status => counts.GetValueOrDefault(status)));
}
