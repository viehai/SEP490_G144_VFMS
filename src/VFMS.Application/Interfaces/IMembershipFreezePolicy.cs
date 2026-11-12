using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

public interface IMembershipFreezePolicyService
{
    Task<Result<MembershipFreezePolicyResponse>> GetAsync(CancellationToken ct = default);
    Task<Result<MembershipFreezePolicyResponse>> UpdateAsync(
        MembershipFreezePolicyRequest request, string managerUserId, string? ipAddress, CancellationToken ct = default);
}

public interface IMembershipFreezePolicyRepository : IAuditedRepository
{
    Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct);
    Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct);
}
