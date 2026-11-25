using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using MembershipDurationUnit = VFMS.Contracts.Memberships.MembershipDurationUnit;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task MembershipGetsTheAllowanceAtSettlementAndKeepsItBeforeAndAfterActivation()
    {
        await SetGlobalFreezeCountAsync(15);
        using var buyer = Client("buyer", "Customer");
        var checkout = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SetGlobalFreezeCountAsync(20);
        using var payment = await NotifyAsync(checkout, id: 171);
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        var waiting = Assert.Single(await ListOwnedMembershipsAsync(buyer));
        Assert.Equal(MembershipStatuses.AwaitingActivation, waiting.Status);
        await SetGlobalFreezeCountAsync(7);
        Assert.Equal(20, await ReadGrantedFreezeCountAsync(waiting.MembershipId));

        await ActivateMembershipAsync(buyer, waiting.MembershipId, Today);
        Assert.Equal(20, await ReadGrantedFreezeCountAsync(waiting.MembershipId));

        var nextCheckout = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var nextPayment = await NotifyAsync(nextCheckout, id: 172);
        Assert.Equal(HttpStatusCode.OK, nextPayment.StatusCode);
        var next = Assert.Single(await ListOwnedMembershipsAsync(buyer), row => row.Status == MembershipStatuses.AwaitingActivation);
        Assert.Equal(7, await ReadGrantedFreezeCountAsync(next.MembershipId));
    }

    [Fact]
    public async Task CashSettlementGrantsDifferentAllowancesToSameDurationPlans()
    {
        await SetGlobalFreezeCountAsync(20);
        int customId;
        int disabledId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var custom = Plan("Custom", 100_000m, PackageStatus.Active);
            custom.Components.Single().MaxFreezeCount = 2;
            custom.Components.Single().MaxFreezeDaysPerRequest = 120;
            var disabled = Plan("Disabled", 100_000m, PackageStatus.Active);
            disabled.Components.Single().MaxFreezeCount = 0;
            db.Packages.AddRange(custom, disabled);
            await db.SaveChangesAsync();
            (customId, disabledId) = (custom.Id, disabled.Id);
        }

        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var order = await CreateAsync(buyer, Purchase([new(_basic), new(customId), new(disabledId)], MembershipPaymentMethods.Cash));
        var route = $"/api/staff/membership-purchases/{order.OrderNumber}/confirm-cash";
        var paid = await ReadAsync(await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(order.AmountDue)), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);

        var memberships = (await ListOwnedMembershipsAsync(buyer)).ToDictionary(row => row.MembershipPlanId);
        Assert.Equal(20, await ReadGrantedFreezeCountAsync(memberships[_basic].MembershipId));
        Assert.Equal(2, await ReadGrantedFreezeCountAsync(memberships[customId].MembershipId));
        Assert.Equal(120, (await ReadMembershipDetailAsync(buyer, memberships[customId].MembershipId)).Freeze.GrantedMaxFreezeDays);
        Assert.Equal(0, await ReadGrantedFreezeCountAsync(memberships[disabledId].MembershipId));
    }

    [Fact]
    public async Task TwoGymFreezeVisitsEachAllow120DaysAndPendingCancellationReleasesTheVisit()
    {
        await ConfigureFreezeRequestsAsync(RequestPolicy with { DefaultMaxFreezeCount = 2, MinimumFreezeDays = 30, MaximumFreezeDays = 90 });
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var component = await db.PackageComponents.SingleAsync(row => row.PackageId == _basic);
            component.MaxFreezeDaysPerRequest = 120;
            await db.SaveChangesAsync();
        }

        var membership = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.Equal(2, eligibility.RemainingFreezeCount);
        Assert.Equal(30, eligibility.Policy!.MinimumFreezeDays);
        Assert.Equal(120, eligibility.MaximumAvailableDays);
        using var tooLong = await PostFreezeWithDocumentAsync(member, FreezeInput(membership.Id, days: 121));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var pending = await CreateFreezeRequestAsync(member, membership.Id, days: 120);
        Assert.Equal(1, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingFreezeCount);
        using var cancel = await member.PostAsync($"{FreezeRequestsRoute}/{pending.FreezeRequestId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(2, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingFreezeCount);

        var first = await CreateFreezeRequestAsync(member, membership.Id, days: 120);
        await ApproveFreezeReviewAsync(manager, first.FreezeRequestId);
        var remaining = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.Equal(1, remaining.RemainingFreezeCount);
        Assert.Equal(120, remaining.MaximumAvailableDays);
        var second = await CreateFreezeRequestAsync(member, membership.Id, first.EndDate.AddDays(1), 120);
        Assert.Equal(0, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingFreezeCount);
        await ApproveFreezeReviewAsync(manager, second.FreezeRequestId);
        var third = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.False(third.Eligible);
        Assert.Equal("FREEZE_VISITS_EXHAUSTED", third.MessageCode);
        using var refused = await PostFreezeWithDocumentAsync(member, FreezeInput(membership.Id, second.EndDate.AddDays(1), 30));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var detail = await ReadMembershipDetailAsync(member, membership.Id);
        Assert.Equal(2, detail.Freeze.FreezeCount);
        Assert.Equal(membership.EndDate.AddDays(240), detail.Membership.EndDate);
    }

    [Theory]
    [InlineData(2025, 1, 31, 2, 27)]
    [InlineData(2024, 1, 31, 2, 28)]
    [InlineData(2024, 2, 29, 3, 28)]
    public async Task CalendarTermUsesItsPurchasedMonthAtActivation(int year, int month, int day, int endMonth, int endDay)
    {
        _clock.Now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.FromHours(7));
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var component = await db.PackageComponents.SingleAsync(row => row.PackageId == _basic);
            component.DurationDays = null;
            component.DurationMonths = 1;
            await db.SaveChangesAsync();
        }

        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var order = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        await UpdateRenewalPlanAsync("Edited to days", 500_000, duration: 30);
        var route = $"/api/staff/membership-purchases/{order.OrderNumber}/confirm-cash";
        var paid = await ReadAsync(await staff.PostAsJsonAsync(route, new ConfirmMembershipCashRequest(order.AmountDue)), HttpStatusCode.OK);
        Assert.Equal("Paid", paid.OrderStatus);

        var waiting = Assert.Single(await ListOwnedMembershipsAsync(buyer));
        Assert.Equal(1, waiting.DurationValue);
        Assert.Equal(MembershipDurationUnit.Month, waiting.DurationUnit);
        var active = await ActivateMembershipAsync(buyer, waiting.MembershipId, Today);
        Assert.Equal(new DateOnly(year, endMonth, endDay), active.Membership.EndDate);
        Assert.Equal(MembershipDurationUnit.Month, active.Membership.DurationUnit);
    }

    private async Task SetGlobalFreezeCountAsync(int count)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var policy = await db.FreezePolicies.SingleOrDefaultAsync();
        if (policy is null)
        {
            policy = new FreezePolicy
            {
                CreatedByUserId = "manager", PolicyName = "Membership freeze", MinFreezeDaysPerRequest = 1,
                DefaultMaxFreezeDays = 60
            };
            db.FreezePolicies.Add(policy);
        }
        policy.DefaultMaxFreezeCount = count;
        await db.SaveChangesAsync();
    }

    private async Task<int?> ReadGrantedFreezeCountAsync(int membershipId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().MemberMemberships
            .Where(row => row.Id == membershipId).Select(row => row.GrantedFreezeCount).SingleAsync();
    }
}
