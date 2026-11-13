using VFMS.Contracts.Common;

namespace VFMS.Application.Interfaces;

public interface IMembershipFreezeLifecycleService
{
    Task<Result<bool>> ProcessAsync(int requestId, CancellationToken ct);
}

public interface IMembershipFreezeLifecycleRepository : IAuditedRepository
{
    Task<IReadOnlyList<int>> ListDueAsync(DateOnly today, int afterId, int pageSize, CancellationToken ct);
    Task<MembershipFreezeLifecycleTarget?> FindAsync(int requestId, CancellationToken ct);
}

public sealed record MembershipFreezeLifecycleTarget(int RequestId, int MembershipId, string MemberId);
