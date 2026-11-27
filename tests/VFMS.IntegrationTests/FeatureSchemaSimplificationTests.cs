using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public class FeatureSchemaSimplificationTests
{
    private static VfmsDbContext Context() => new(new DbContextOptionsBuilder<VfmsDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SchemaModelOnly;Integrated Security=true").Options);

    // The persisted shape is an explicit requirement: a CLR-only projection must never become a duplicate column again.
    [Theory]
    [InlineData(typeof(TrainerApplication), 28)]
    [InlineData(typeof(TrainerProfile), 17)]
    [InlineData(typeof(TrainerRole), 7)]
    [InlineData(typeof(TrainerReview), 15)]
    [InlineData(typeof(PTPurchase), 16)]
    [InlineData(typeof(PTSession), 31)]
    [InlineData(typeof(PTFreeze), 22)]
    [InlineData(typeof(PTAvailability), 8)]
    [InlineData(typeof(Locker), 7)]
    [InlineData(typeof(LockerZone), 10)]
    [InlineData(typeof(LockerPricingPolicy), 10)]
    [InlineData(typeof(LockerRentalContract), 25)]
    [InlineData(typeof(LockerIncidentLog), 15)]
    [InlineData(typeof(GymAnnouncement), 11)]
    [InlineData(typeof(Notification), 13)]
    [InlineData(typeof(PaymentTransaction), 29)]
    [InlineData(typeof(PackageComponent), 17)]
    public void Agreed_table_shape_has_one_persisted_source(Type entity, int expected)
    {
        using var db = Context();
        Assert.Equal(expected, db.Model.FindEntityType(entity)!.GetProperties().Count());
    }

    [Theory]
    [InlineData(typeof(ApplicationUser), 19)]
    [InlineData(typeof(PhysicalProfile), 18)]
    [InlineData(typeof(UploadedImage), 9)]
    [InlineData(typeof(Order), 9)]
    [InlineData(typeof(OrderItem), 12)]
    [InlineData(typeof(Invoice), 8)]
    public void Shared_identity_order_and_invoice_columns_remain(Type entity, int expected)
    {
        using var db = Context();
        Assert.Equal(expected, db.Model.FindEntityType(entity)!.GetProperties().Count());
    }

    [Fact]
    public void Per_payment_quote_deadlines_and_refund_evidence_remain_persisted()
    {
        using var db = Context();
        var payment = db.Model.FindEntityType(typeof(PaymentTransaction))!;
        string[] evidence = ["LockerRentalContractId", "LockerPurpose", "RenewalMonths", "OverdueFees",
            "RentalExpiryAtQuote", "PaymentStartedAt", "PaymentExpiresAt", "HoldExpiresAt",
            "SourcePaymentTransactionId", "RefundAmount", "RefundReason", "RefundStatus", "RefundResolvedAt"];
        Assert.All(evidence, name => Assert.NotNull(payment.FindProperty(name)));
    }
}
