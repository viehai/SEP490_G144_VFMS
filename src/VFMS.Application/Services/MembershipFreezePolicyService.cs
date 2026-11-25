using System.Text.Json;
using FluentValidation;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using ContractUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;
using DomainUnit = VFMS.Domain.Enums.MembershipDurationUnit;

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

        FreezePolicy? policy = null;
        await repository.ExecuteInSerializableTransactionAsync(async () =>
        {
            policy = await repository.GetActiveGlobalAsync(true, ct);
            var creating = policy is null;
            var before = policy is null ? null : Snapshot(policy);
            policy ??= new FreezePolicy { PolicyName = "Membership Freeze Policy", CreatedByUserId = managerUserId };
            policy.MinFreezeDaysPerRequest = ToDays(request.MinimumFreezeDays, request.MinimumFreezeDaysUnit);
            policy.DefaultMaxFreezeDays = ToDays(request.MaximumFreezeDays, request.MaximumFreezeDaysUnit);
            policy.MinimumRequestDays = ToDays(request.MinimumRequestDays, request.MinimumRequestDaysUnit);
            policy.EligibilityPaidDays = ToDays(request.EligibilityPaidDays, request.EligibilityPaidDaysUnit);
            policy.DefaultMaxFreezeCount = request.DefaultMaxFreezeCount ?? policy.DefaultMaxFreezeCount;
            policy.MinimumFreezeDaysUnit = (DomainUnit)request.MinimumFreezeDaysUnit;
            policy.MaximumFreezeDaysUnit = (DomainUnit)request.MaximumFreezeDaysUnit;
            policy.MinimumRequestDaysUnit = (DomainUnit)request.MinimumRequestDaysUnit;
            policy.EligibilityPaidDaysUnit = (DomainUnit)request.EligibilityPaidDaysUnit;
            var after = Snapshot(policy);
            if (before == after)
                return;

            var audit = AuditedWriter.Audit(managerUserId, creating ? AuditActionType.Create : AuditActionType.Update,
                nameof(FreezePolicy), ipAddress, before, after);
            if (creating)
                await AuditedWriter.SaveNewAsync(repository, policy, audit, notifications: null, ct);
            else
            {
                policy.UpdatedAt = audit.OccurredAt;
                await AuditedWriter.SaveChangedAsync(repository, policy, audit, notifications: null, ct);
            }
        }, ct);

        return Result<MembershipFreezePolicyResponse>.Success(ToResponse(policy));
    }

    private static int ToDays(int? value, ContractUnit unit)
    {
        if (value.HasValue && MembershipFreezeDuration.TryToDays(value.Value, unit, out var days))
            return days;
        throw new InvalidOperationException("Freeze duration was not validated.");
    }

    private static MembershipFreezePolicyResponse ToResponse(FreezePolicy? policy) =>
        new(Configuration(policy), []);

    internal static MembershipFreezePolicyConfiguration? Configuration(FreezePolicy? policy) =>
        policy is { MinFreezeDaysPerRequest: { } minimum }
            ? new(policy.Id, minimum, policy.DefaultMaxFreezeDays, policy.MinimumRequestDays, policy.EligibilityPaidDays,
                DateTime.SpecifyKind(policy.UpdatedAt, DateTimeKind.Utc),
                (ContractUnit)policy.MinimumFreezeDaysUnit, (ContractUnit)policy.MaximumFreezeDaysUnit,
                (ContractUnit)policy.MinimumRequestDaysUnit, (ContractUnit)policy.EligibilityPaidDaysUnit,
                policy.DefaultMaxFreezeCount)
            : null;

    private static string Snapshot(FreezePolicy policy) => JsonSerializer.Serialize(new
    {
        MinimumFreezeDays = policy.MinFreezeDaysPerRequest,
        MaximumFreezeDays = policy.DefaultMaxFreezeDays,
        policy.MinimumRequestDays,
        policy.EligibilityPaidDays,
        policy.DefaultMaxFreezeCount,
        policy.MinimumFreezeDaysUnit,
        policy.MaximumFreezeDaysUnit,
        policy.MinimumRequestDaysUnit,
        policy.EligibilityPaidDaysUnit
    });
}
