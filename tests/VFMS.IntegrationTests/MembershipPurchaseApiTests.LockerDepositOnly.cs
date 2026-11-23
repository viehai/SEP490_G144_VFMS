using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20_000)]
    public async Task DailyLockerChargesOnlyDepositEvenWithALegacyRentalFee(decimal legacyFee)
    {
        var (lockerId, _) = await PrepareLocker();
        _clock.Now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(7));
        int policyId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var policy = new LockerPricingPolicy
            {
                PolicyName = "Thuê ngày", RentalType = RentalType.Daily,
                RentalPrice = legacyFee, DepositAmount = 150_000, OverdueDailyFee = 0
            };
            db.LockerPricingPolicies.Add(policy);
            await db.SaveChangesAsync();
            policyId = policy.Id;
        }

        using var staff = Client("staff", "Staff");
        var prices = (await staff.GetFromJsonAsync<LockerPolicy[]>("/api/lockers/staff/pricing"))!;
        Assert.Equal(0, Assert.Single(prices, p => p.Id == policyId).RentalFee);
        var checkout = await CreateLockerCheckout(staff, "/api/lockers/staff/rentals",
            new { memberId = "member", lockerId, policyId, paymentMethod = "Cash" });
        Assert.Equal(150_000, checkout.GetProperty("amountDue").GetDecimal());
        var id = checkout.GetProperty("rentalId").GetInt32();
        using var handover = await staff.PostAsJsonAsync($"/api/lockers/staff/rentals/{id}/handover",
            new { termsAccepted = true, identityVerified = true, cashAmount = 150_000 });
        Assert.True(handover.IsSuccessStatusCode, await handover.Content.ReadAsStringAsync());
        var rental = await handover.Content.ReadFromJsonAsync<LockerRentalView>();
        Assert.Equal(0, rental!.RentalFee);
        Assert.Equal(150_000, rental.DepositHeld);
        await using var verify = _app.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var snapshot = await context.LockerRentalContracts.SingleAsync(r => r.Id == id);
        Assert.Equal(0, snapshot.RentalPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20_000)]
    public async Task LockerPricingStoresDailyAsFreeAndKeepsMonthlyPrices(decimal submittedDailyFee)
    {
        await PrepareLocker();
        using var manager = Client("manager", "Manager");
        using var saved = await manager.PutAsJsonAsync("/api/lockers/manager/pricing", new
        {
            dailyFee = submittedDailyFee, keyDeposit = 150_000, oneMonth = 200_000,
            threeMonths = 550_000, sixMonths = 1_000_000, twelveMonths = 1_900_000, overdueDailyFee = 10_000
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var policies = (await saved.Content.ReadFromJsonAsync<LockerPolicy[]>())!;
        Assert.Equal(0, Assert.Single(policies, p => p.Type == "Daily").RentalFee);
        Assert.Equal(200_000, Assert.Single(policies, p => p.Type == "Monthly").RentalFee);
        Assert.All(policies, p => Assert.Equal(150_000, p.Deposit));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(0, (await db.LockerPricingPolicies.SingleAsync(p => p.RentalType == RentalType.Daily)).RentalPrice);
    }
}
