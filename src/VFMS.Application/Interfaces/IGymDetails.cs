using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Domain.Entities;

namespace VFMS.Application.Interfaces;

/// <summary>Equipment of one category, counted for the gym details page.</summary>
public sealed record GymEquipmentArea(string? CategoryName, string? Description, int Total, int Available);

/// <summary>Read-only data for UC-70 View Gym Details &amp; Facilities.</summary>
public interface IGymDetailsRepository
{
    /// <summary>The gym the system runs (the first one; the system manages a single gym).</summary>
    Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default);

    /// <summary>Equipment that is not retired, grouped by category, named categories first in name order.</summary>
    Task<IReadOnlyList<GymEquipmentArea>> GetEquipmentAreasAsync(int gymId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YogaStudio>> GetActiveStudiosAsync(int gymId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LockerZone>> GetLockerZonesAsync(int gymId, CancellationToken cancellationToken = default);
}

/// <summary>UC-70 View Gym Details &amp; Facilities.</summary>
public interface IGymDetailsService
{
    Task<Result<GymDetailsResponse>> GetAsync(CancellationToken cancellationToken = default);
}
