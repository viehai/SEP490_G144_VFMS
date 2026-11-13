using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

internal static class TestSeed
{
    /// <summary>A paid one-component package and a membership for the member: Active from 5 days ago for 25 more days.</summary>
    public static MemberMembership Membership(string memberUserId, ComponentType type = ComponentType.GYM, int? yogaClassTypeId = null)
    {
        var package = new Package { Name = $"{type} plan", Price = 100 };
        var component = new PackageComponent { Package = package, ComponentType = type, DurationMonths = 1, YogaClassTypeId = yogaClassTypeId };
        var order = new Order { OrderNumber = $"ORD-{Guid.NewGuid():N}", PurchaserUserId = memberUserId, OrderStatus = OrderStatus.Paid };
        var item = new OrderItem
        {
            Order = order, Package = package, BeneficiaryUserId = memberUserId, UnitPriceSnapshot = 100, PackageNameSnapshot = package.Name
        };
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        return new MemberMembership
        {
            OrderItem = item, PackageComponent = component, MemberUserId = memberUserId, MembershipStatus = MembershipStatus.Active,
            StartDate = today.AddDays(-5), EndDate = today.AddDays(25)
        };
    }
}

/// <summary>The real VFMS model on SQLite for relational tests.</summary>
internal sealed class SqliteTestContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Map SQL Server binary data and voucher collation to SQLite equivalents in relational tests.
        modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
        modelBuilder.Entity<Voucher>().Property(voucher => voucher.Code).UseCollation("NOCASE");
    }
}
