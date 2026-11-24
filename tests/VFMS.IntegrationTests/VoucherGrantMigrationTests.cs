using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.IntegrationTests;

public sealed class VoucherGrantMigrationTests
{
    [SqlServerMigrationFact]
    public async Task UpgradeKeepsLegacyPromotionAndCreatesNoEntitlements()
    {
        await using var isolated = await LegacySchemaMigrationTests.DisposableMigrationDatabase.CreateAsync();
        await using var db = isolated.Context();
        await db.GetService<IMigrator>().MigrateAsync("20261124035956_LinkFeedbackToPackage");
        db.ApplicationUsers.Add(new ApplicationUser
        {
            UserId = "legacy-manager", FullName = "Legacy", Email = "legacy@example.test", PhoneNumber = "0907000123",
            PasswordHash = "unused", Gym = new Gym { GymName = "Migration" }
        });
        await db.SaveChangesAsync();
        await isolated.ExecuteAsync("""
            INSERT dbo.Vouchers (Code, Name, DiscountType, DiscountValue, StartAt, EndAt, MaxRedemptions,
                MaxRedemptionsPerUser, VoucherStatus, CreatedByUserId, CreatedAt, UpdatedAt, IsDeleted)
            VALUES ('OLD', 'Existing offer', 'FIXED', 12000, GETUTCDATE(), DATEADD(day,7,GETUTCDATE()),
                20, 2, 'Active', 'legacy-manager', GETUTCDATE(), GETUTCDATE(), 0);
            """);
        await db.Database.MigrateAsync();
        var legacy = await db.Vouchers.AsNoTracking().SingleAsync(x => x.Code == "OLD");
        Assert.Equal(VoucherAudience.AllCustomers, legacy.Audience);
        Assert.Equal(2, legacy.MaxRedemptionsPerUser);
        Assert.Equal(12000m, legacy.DiscountValue);
        Assert.Null(legacy.NewCustomerDuration);
        Assert.Null(legacy.NewCustomerUnit);
        Assert.Empty(await db.VoucherGrants.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
