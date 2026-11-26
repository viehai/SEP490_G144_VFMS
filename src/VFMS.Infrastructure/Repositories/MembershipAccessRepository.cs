using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class MembershipAccessRepository(VfmsDbContext context) : IMembershipAccessRepository
{
    public Task<OwnedMembershipAccess?> FindOwnedAsync(string memberId, int membershipId, CancellationToken ct) =>
        context.MemberMemberships.AsNoTracking()
            .Where(membership => membership.Id == membershipId && membership.MemberUserId == memberId)
            .Select(membership => new OwnedMembershipAccess(membership.Id, membership.PackageComponent.ComponentType,
                membership.MembershipStatus, membership.StartDate, membership.EndDate)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EntryPlan>> GetEntryPlansAsync(IReadOnlyList<string> userIds, DateOnly today, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var purchases = context.PTPurchases.AsNoTracking().Where(p => userIds.Contains(p.MemberUserId));
        // A linked PT component and its contract describe the same purchase. The contract owns its freeze and completion state.
        var memberships = await context.MemberMemberships.AsNoTracking()
            .Where(m => userIds.Contains(m.MemberUserId) &&
                (m.PackageComponent.ComponentType == ComponentType.GYM || m.PackageComponent.ComponentType == ComponentType.YOGA ||
                 m.PackageComponent.ComponentType == ComponentType.PT && !purchases.Any(p =>
                     p.MemberUserId == m.MemberUserId && p.MemberMembershipId == m.Id)))
            .Select(m => new { m.MemberUserId, m.Id, m.MembershipStatus, m.StartDate, m.EndDate })
            .ToListAsync(cancellationToken);
        var plans = memberships.Select(m => new EntryPlan(m.MemberUserId, m.Id,
            MembershipRules.StatusOn(m.MembershipStatus, m.StartDate, m.EndDate, today).ToString(),
            m.MembershipStatus == MembershipStatus.AwaitingActivation ? null : m.EndDate)).ToList();

        var runningIds = purchases.Where(PersonalTrainingRules.StillRunning(today)).Select(p => p.Id);
        var contracts = await purchases.Include(p => p.PTFreezes)
            .Select(p => new { Contract = p, StillRunning = runningIds.Contains(p.Id),
                MembershipId = p.MemberMembership != null && p.MemberMembership.MemberUserId == p.MemberUserId &&
                    p.MemberMembership.PackageComponent.ComponentType == ComponentType.PT ? p.MemberMembershipId : null })
            .ToListAsync(cancellationToken);
        foreach (var row in contracts)
        {
            var p = row.Contract;
            var status = PersonalTrainingRules.EffectiveStatus(p, today, nowUtc, row.StillRunning);
            // Older frozen contracts can lack the freeze record that specifies the resume date.
            if (status == PTPurchaseStatus.Active && p.Status == PTPurchaseStatus.Frozen && p.PTFreezes.Count == 0)
                status = PTPurchaseStatus.Frozen;
            var label = status == PTPurchaseStatus.Active && p.StartDate > today ? "Scheduled" : status.ToString();
            var until = status == PTPurchaseStatus.Active && today > p.EndDate ? PersonalTrainingRules.MakeUpDeadline(p.EndDate) : p.EndDate;
            plans.Add(new EntryPlan(p.MemberUserId, row.MembershipId, label, until));
        }
        return plans;
    }
}
