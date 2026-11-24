using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

/// <summary>UC-70 View Gym Details &amp; Facilities. Reads only; nothing is tracked.</summary>
public sealed class GymDetailsRepository : IGymDetailsRepository
{
    private readonly VfmsDbContext _context;

    public GymDetailsRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public Task<Gym?> GetDefaultGymAsync(CancellationToken cancellationToken = default) =>
        _context.Gyms.AsNoTracking().OrderBy(g => g.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<GymEquipmentArea>> GetEquipmentAreasAsync(
        int gymId, CancellationToken cancellationToken = default)
    {
        // A gym has tens to hundreds of machines, so the counts are made in memory from a small projection.
        var items = await _context.Equipments.AsNoTracking()
            .Where(e => e.GymId == gymId && e.EquipmentStatus != EquipmentStatus.Retired)
            .Select(e => new
            {
                e.CatalogCategoryId,
                CategoryName = e.CatalogCategory == null ? null : e.CatalogCategory.CategoryName,
                Description = e.CatalogCategory == null ? null : e.CatalogCategory.Description,
                Working = e.EquipmentStatus == EquipmentStatus.Operational,
            })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(e => e.CatalogCategoryId)
            .Select(g => new GymEquipmentArea(
                g.First().CategoryName, g.First().Description, g.Count(), g.Count(e => e.Working)))
            .OrderBy(a => a.CategoryName is null)
            .ThenBy(a => a.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<YogaStudio>> GetActiveStudiosAsync(
        int gymId, CancellationToken cancellationToken = default) =>
        await _context.YogaStudios.AsNoTracking()
            .Where(s => s.GymId == gymId && s.IsActive)
            .OrderBy(s => s.FloorNumber).ThenBy(s => s.StudioName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LockerZone>> GetLockerZonesAsync(
        int gymId, CancellationToken cancellationToken = default) =>
        await _context.LockerZones.AsNoTracking()
            .Where(z => z.GymId == gymId)
            .OrderBy(z => z.FloorNumber).ThenBy(z => z.ZoneName)
            .ToListAsync(cancellationToken);
}
