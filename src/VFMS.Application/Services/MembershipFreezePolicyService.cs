using System.Text.Json;
using FluentValidation;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class MembershipFreezePolicyService(
    IMembershipFreezePolicyRepository repository, IValidator<MembershipFreezePolicyRequest> validator)
    : IMembershipFreezePolicyService
{
    public async Task<Result<MembershipFreezePolicyResponse>> GetAsync(CancellationToken ct = default) =>
        Result<MembershipFreezePolicyResponse>.Success(ToResponse(await repository.GetActiveGlobalAsync(false, ct)));

    public async Task<Result<MembershipFreezePolicyResponse>> UpdateAsync(
        MembershipFreezePolicyRequest request, string managerUserId, string? ipAddress, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return Result<MembershipFreezePolicyResponse>.Failure(
                string.Join(" ", validation.Errors.Select(error => error.ErrorMessage).Distinct()));

        var requested = request.Quotas!.ToDictionary(
            quota => (Duration: quota.Duration!.Value, Unit: Enum.Parse<FreezeTierUnit>(quota.Unit!)),
            quota => quota.MaximumCumulativeFreezeDays!.Value);
        FreezePolicy? policy = null;
        await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            policy = await repository.GetActiveGlobalAsync(true, ct);
            var creating = policy is null;
            var before = policy is null ? null : Snapshot(policy);
            policy ??= new FreezePolicy { PolicyName = "Membership Freeze Policy", CreatedByUserId = managerUserId };
            policy.MinFreezeDaysPerRequest = request.MinimumFreezeDays!.Value;
            policy.DefaultMaxFreezeDays = request.MaximumFreezeDays!.Value;
            policy.MinimumRequestDays = request.MinimumRequestDays!.Value;
            policy.EligibilityPaidDays = request.EligibilityPaidDays!.Value;
            ApplyQuotas(policy, requested);
            var after = Snapshot(policy);
            if (before == after)
                return;

            var audit = AuditedWriter.Audit(managerUserId, creating ? AuditActionType.Create : AuditActionType.Update,
                nameof(FreezePolicy), ipAddress, before, after);
            if (creating)
                await AuditedWriter.SaveNewAsync(repository, policy, audit, notifications: null, ct);
            else
            {
                // A change limited to the owned quota rows also updates the policy timestamp.
                policy.UpdatedAt = audit.OccurredAt;
                await AuditedWriter.SaveChangedAsync(repository, policy, audit, notifications: null, ct);
            }
        }, ct);

        return Result<MembershipFreezePolicyResponse>.Success(ToResponse(policy));
    }

    private static void ApplyQuotas(FreezePolicy policy, Dictionary<(int Duration, FreezeTierUnit Unit), int> requested)
    {
        foreach (var quota in policy.TierQuotas.ToArray())
        {
            if (requested.Remove((quota.Duration, quota.Unit), out var maximum))
                quota.MaximumCumulativeFreezeDays = maximum;
            else
                policy.TierQuotas.Remove(quota);
        }

        foreach (var (tier, maximum) in requested)
            policy.TierQuotas.Add(new FreezePolicyTierQuota
            {
                Duration = tier.Duration,
                Unit = tier.Unit,
                MaximumCumulativeFreezeDays = maximum
            });
    }

    private static MembershipFreezePolicyResponse ToResponse(FreezePolicy? policy) =>
        policy is { MinFreezeDaysPerRequest: { } minimum }
            ? new(new(policy.Id, minimum, policy.DefaultMaxFreezeDays, policy.MinimumRequestDays,
                    policy.EligibilityPaidDays,
                    DateTime.SpecifyKind(policy.UpdatedAt, DateTimeKind.Utc)),
                ToQuotaResponses(policy))
            : new(null, []);

    private static MembershipFreezeTierQuotaResponse[] ToQuotaResponses(FreezePolicy policy) =>
        policy.TierQuotas.OrderBy(quota => quota.Unit).ThenBy(quota => quota.Duration)
            .Select(quota => new MembershipFreezeTierQuotaResponse(
                quota.Duration, quota.Unit.ToString(), quota.MaximumCumulativeFreezeDays)).ToArray();

    private static string Snapshot(FreezePolicy policy) => JsonSerializer.Serialize(new
    {
        MinimumFreezeDays = policy.MinFreezeDaysPerRequest,
        MaximumFreezeDays = policy.DefaultMaxFreezeDays,
        policy.MinimumRequestDays,
        policy.EligibilityPaidDays,
        Quotas = ToQuotaResponses(policy)
    });
}
