using System.Data;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipFreezePolicyRepository(VfmsDbContext context)
    : AuditedRepositoryBase(context), IMembershipFreezePolicyRepository
{
    public Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct)
    {
        // Lock the global policy key range, including when there is no configured row yet.
        var policies = forUpdate && Context.Database.IsSqlServer()
            ? Context.FreezePolicies.FromSqlRaw("SELECT * FROM [FreezePolicies] WITH (UPDLOCK, HOLDLOCK)")
            : Context.FreezePolicies;
        if (!forUpdate)
            policies = policies.AsNoTracking();

        return policies.SingleOrDefaultAsync(policy => policy.IsGlobal && policy.PolicyStatus == PolicyStatus.Active, ct);
    }

    public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await work();
        await transaction.CommitAsync(ct);
    }
}
