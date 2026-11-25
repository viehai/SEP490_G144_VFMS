using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VFMS.Domain.Enums;

namespace VFMS.IntegrationTests;

public sealed partial class LegacySchemaMigrationTests
{
    private const string BeforeFreezeEntitlements = "20261124113142_AddVoucherAudienceAndCompetitionGrants";
    private const string FreezeEntitlements = "20261124173752_ReviseMembershipFreezeEntitlements";

    [Fact]
    public void FreezeEntitlementMigrationMatchesTheModelAndRefusesLossyDowngrade()
    {
        using var context = ModelContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrator = context.GetService<IMigrator>();
        var script = migrator.GenerateScript(BeforeFreezeEntitlements, FreezeEntitlements);
        Assert.Contains("OPENJSON", script);
        Assert.Contains("GrantedFreezeAllowanceDays", script);
        Assert.DoesNotContain("DROP TABLE [FreezePolicyTierQuotas]", script);
        var error = Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(FreezeEntitlements, BeforeFreezeEntitlements));
        Assert.Contains("Restore the pre-migration backup", error.Message);
    }

    [SqlServerMigrationFact]
    public async Task UpgradeUsesPurchasedTermsForTheGrantAndCurrentTermsForThePlanOverride()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        var original = await SeedLegacyDatabaseAsync(database);
        await using var context = database.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeFreezeEntitlements);
        await SeedFreezeEntitlementLegacyPolicyAsync(database);
        await database.ExecuteAsync("""
            UPDATE dbo.PackageComponents SET DurationDays=90, DurationMonths=3;
            UPDATE oi SET ComponentTermsSnapshot=CONCAT('[{"Id":', m.PackageComponentId, ',"DurationDays":30}]')
            FROM dbo.OrderItems oi JOIN dbo.MemberMemberships m ON m.OrderItemId=oi.Id;
            INSERT INTO dbo.MemberMemberships
                (OrderItemId,PackageComponentId,MemberUserId,MembershipStatus,StartDate,EndDate,TotalFrozenDays,FreezeCount,CreatedAt,UpdatedAt,IsDeleted)
            SELECT OrderItemId,PackageComponentId,MemberUserId,'AwaitingActivation','0001-01-01','0001-01-01',0,0,
                SYSUTCDATETIME(),SYSUTCDATETIME(),0 FROM dbo.MemberMemberships;
            """);

        await migrator.MigrateAsync(FreezeEntitlements);

        var plan = await context.Packages.AsNoTracking().SingleAsync();
        Assert.Equal(10, plan.FreezeAllowanceOverrideDays);
        Assert.Equal(MembershipDurationUnit.Day, plan.FreezeAllowanceOverrideUnit);
        var component = await context.PackageComponents.AsNoTracking().SingleAsync();
        Assert.Equal(90, component.DurationDays);
        Assert.Equal(3, component.DurationMonths);
        var memberships = await context.MemberMemberships.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
        Assert.All(memberships, row => Assert.Equal(20, row.GrantedFreezeAllowanceDays));
        Assert.Equal(original.MembershipEnd, memberships[0].EndDate);
        Assert.Equal(7, memberships[0].TotalFrozenDays);
        Assert.Equal(1, memberships[0].FreezeCount);
        Assert.Equal(MembershipStatus.AwaitingActivation, memberships[1].MembershipStatus);
        Assert.Equal(DateOnly.MinValue, memberships[1].StartDate);
        var freeze = await context.MembershipFreezes.AsNoTracking().SingleAsync();
        Assert.Equal(7, freeze.AppliedExpiryExtensionDays);
        Assert.Equal("Policy captured before upgrade", freeze.AppliedPolicySnapshot);
        var policy = await context.FreezePolicies.AsNoTracking().SingleAsync();
        Assert.Null(policy.DefaultFreezeAllowanceDays);
        Assert.Equal(3, policy.TierQuotas.Count);
    }

    [SqlServerMigrationFact]
    public async Task CompositePlanWithConflictingLegacyRightsStopsTheUpgradeBeforeChangingData()
    {
        await using var database = await DisposableMigrationDatabase.CreateAsync();
        await SeedLegacyDatabaseAsync(database);
        await using var context = database.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeFreezeEntitlements);
        await SeedFreezeEntitlementLegacyPolicyAsync(database);
        await database.ExecuteAsync("""
            INSERT INTO dbo.PackageComponents
                (PackageId,ComponentType,DurationDays,AllowFreeze,CreatedAt,UpdatedAt,IsDeleted)
            SELECT PackageId,'YOGA',90,1,SYSUTCDATETIME(),SYSUTCDATETIME(),0
            FROM dbo.PackageComponents;
            """);

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => migrator.MigrateAsync(FreezeEntitlements));

        Assert.Equal(51020, error.Number);
        Assert.Contains("Freeze allowance preflight: package", error.Message);
        Assert.DoesNotContain(FreezeEntitlements, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Packages') AND name=N'FreezeAllowanceOverrideDays'"));
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FreezePolicyTierQuotas"));
    }

    private static Task SeedFreezeEntitlementLegacyPolicyAsync(DisposableMigrationDatabase database) => database.ExecuteAsync("""
        INSERT INTO dbo.FreezePolicies
            (PolicyName,EligibilityPaidDays,MinimumRequestDays,MinFreezeDaysPerRequest,DefaultMaxFreezeDays,DefaultMaxFreezeCount,
             PolicyStatus,IsGlobal,CreatedByUserId,CreatedAt,UpdatedAt,IsDeleted)
        VALUES ('Legacy tiers',0,1,1,60,0,'Active',1,'migration-member',SYSUTCDATETIME(),SYSUTCDATETIME(),0);
        DECLARE @policyId int = SCOPE_IDENTITY();
        INSERT INTO dbo.FreezePolicyTierQuotas (FreezePolicyId,Unit,Duration,MaximumCumulativeFreezeDays)
        VALUES (@policyId,'Days',30,20),(@policyId,'Days',90,10),(@policyId,'Months',3,50);
        """);
}
