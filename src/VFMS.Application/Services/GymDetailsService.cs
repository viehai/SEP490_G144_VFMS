using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;

namespace VFMS.Application.Services;

/// <summary>
/// UC-70 View Gym Details &amp; Facilities: the gym's contact details and opening hours (UC-84), its equipment,
/// yoga studios and locker zones, and the general membership freeze policy. Read only; anyone can view it.
/// </summary>
public sealed class GymDetailsService : IGymDetailsService
{
    private readonly IGymDetailsRepository _repository;
    private readonly IMembershipFreezePolicyService _freezePolicies;

    public GymDetailsService(IGymDetailsRepository repository, IMembershipFreezePolicyService freezePolicies)
    {
        _repository = repository;
        _freezePolicies = freezePolicies;
    }

    public async Task<Result<GymDetailsResponse>> GetAsync(CancellationToken cancellationToken = default)
    {
        var gym = await _repository.GetDefaultGymAsync(cancellationToken);
        if (gym is null)
            return Result<GymDetailsResponse>.Failure("No gym is configured in the system.", "GYM_NOT_CONFIGURED", 404);

        var areas = await _repository.GetEquipmentAreasAsync(gym.Id, cancellationToken);
        var studios = await _repository.GetActiveStudiosAsync(gym.Id, cancellationToken);
        var zones = await _repository.GetLockerZonesAsync(gym.Id, cancellationToken);

        return Result<GymDetailsResponse>.Success(new GymDetailsResponse(
            gym.GymName, gym.Address, gym.PhoneNumber, gym.Email, gym.OpenTime, gym.CloseTime, gym.IsAcceptingMembers,
            areas.Select(a => new GymEquipmentAreaResponse(a.CategoryName, a.Description, a.Total, a.Available)).ToList(),
            studios.Select(s => new GymStudioResponse(s.StudioName, s.FloorNumber, s.Capacity)).ToList(),
            zones.Select(z => new GymLockerZoneResponse(
                z.ZoneName, z.FloorNumber, z.GenderRestriction.ToString(), z.TotalLockers, z.Description)).ToList(),
            await FreezePolicyAsync(cancellationToken)));
    }

    /// <summary>The freeze rules, or null when the Manager has not set them yet or they cannot be read.</summary>
    private async Task<GymFreezePolicySummary?> FreezePolicyAsync(CancellationToken cancellationToken)
    {
        var result = await _freezePolicies.GetAsync(cancellationToken);
        if (!result.IsSuccess || result.Data?.Policy is not { } policy)
            return null;
        return new GymFreezePolicySummary(policy.MinimumFreezeDays, policy.MaximumFreezeDays, policy.MinimumRequestDays,
            policy.EligibilityPaidDays, policy.DefaultMaxFreezeCount);
    }
}
