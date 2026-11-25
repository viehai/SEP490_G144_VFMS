using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private const string MembershipsRoute = "/api/memberships";

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData(" ", "Member", HttpStatusCode.Unauthorized)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    public async Task MembershipListAndDetailRequireAnAuthenticatedCustomerOrMember(
        string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var list = await client.GetAsync($"{MembershipsRoute}/me");
        using var detail = await client.GetAsync($"{MembershipsRoute}/1");
        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, detail.StatusCode);
    }

    [Fact]
    public async Task MembershipListIsEmptyForACustomerBeforePayment()
    {
        using var member = Client("buyer", "Customer");
        await CreateAsync(member, Purchase([new(_basic)]));

        using var response = await member.GetAsync($"{MembershipsRoute}/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<MembershipSummaryResponse[]>())!);
    }

    [Fact]
    public async Task MembershipQueriesUseTheBeneficiaryAndKeepThePurchasedNameAfterACatalogEdit()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic), new(_combo, "gift@example.test")]));
        await UpdateRenewalPlanAsync("Renamed Basic", 650_000m, duration: 15);
        using var callback = await NotifyAsync(purchase, id: 110);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var paid = await ReadAsync(await buyer.GetAsync($"{Route}/{purchase.OrderNumber}"), HttpStatusCode.OK);
        Assert.NotNull(paid.InvoiceNumber);
        // The test callback represents the bank confirmation time to the second.
        var purchasedAt = DateTimeOffset.FromUnixTimeSeconds(purchase.CreatedAt.AddSeconds(1).ToUnixTimeSeconds());

        var pendingBuyer = Assert.Single(await ListOwnedMembershipsAsync(buyer));
        await ActivateMembershipAsync(buyer, pendingBuyer.MembershipId, Today);
        using var buyerAsMember = Client("buyer", "Member");
        using var beneficiary = Client("gift", "Customer");
        var pendingGift = (await ListOwnedMembershipsAsync(beneficiary)).First();
        await ActivateMembershipAsync(beneficiary, pendingGift.MembershipId, Today);
        var bought = Assert.Single((await buyerAsMember.GetFromJsonAsync<MembershipSummaryResponse[]>(
            $"{MembershipsRoute}/me?memberId=gift&beneficiaryUserId=gift"))!);
        Assert.Equal(new MembershipSummaryResponse(bought.MembershipId, _basic, "Basic", "GYM", Today,
            Today.AddDays(29), "Active") { DurationValue = 30, DurationUnit = MembershipDurationUnit.Day }, bought);

        var gifted = await ListOwnedMembershipsAsync(beneficiary);
        Assert.Equal(2, gifted.Length);
        Assert.Equal(["GYM", "YOGA"], gifted.Select(membership => membership.ComponentType).Order());
        Assert.All(gifted, membership =>
        {
            Assert.Equal(_combo, membership.MembershipPlanId);
            Assert.Equal("Combo", membership.PlanName);
            Assert.Equal("Active", membership.Status);
        });

        using var boughtResponse = await buyerAsMember.GetAsync($"{MembershipsRoute}/{bought.MembershipId}");
        Assert.Equal(HttpStatusCode.OK, boughtResponse.StatusCode);
        var boughtDetail = (await boughtResponse.Content.ReadFromJsonAsync<MembershipDetailResponse>())!;
        Assert.Equal(bought, boughtDetail.Membership);
        Assert.Equal(purchasedAt, boughtDetail.PurchasedAt);
        Assert.Equal(paid.InvoiceNumber, boughtDetail.InvoiceNumber);
        Assert.Equal(0, boughtDetail.Freeze.FreezeCount);
        Assert.Equal(0, boughtDetail.Freeze.TotalFrozenDays);
        Assert.Empty(boughtDetail.Freeze.History);
        var body = await boughtResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("gatewayTransactionReference", body);
        Assert.DoesNotContain("rawGatewayStatus", body);
        Assert.DoesNotContain("paymentTransaction", body);
        Assert.DoesNotContain("beneficiaryUserId", body);
        Assert.DoesNotContain(BankAccount, body);

        var gift = gifted[0];
        var giftDetail = await ReadMembershipDetailAsync(beneficiary, gift.MembershipId);
        Assert.Equal(gift, giftDetail.Membership);
        Assert.Equal(purchasedAt, giftDetail.PurchasedAt);
        Assert.Null(giftDetail.InvoiceNumber);

        using var otherOwner = await buyerAsMember.GetAsync(
            $"{MembershipsRoute}/{gift.MembershipId}?userId=gift&beneficiaryUserId=gift");
        using var otherBuyer = await beneficiary.GetAsync($"{MembershipsRoute}/{bought.MembershipId}");
        using var missing = await buyerAsMember.GetAsync($"{MembershipsRoute}/{int.MaxValue}");
        Assert.Equal(HttpStatusCode.NotFound, otherOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherBuyer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var unauthorizedProblem = await otherOwner.Content.ReadFromJsonAsync<JsonElement>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(missingProblem.GetProperty("code").GetString(), unauthorizedProblem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OnlyTheBeneficiaryCanSelectAValidDateAndARepeatedRequestKeepsTheExistingSelection()
    {
        using var buyer = Client("buyer", "Customer");
        using var gift = Client("gift", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic, "gift@example.test")]));
        using var confirm = await NotifyAsync(purchase, 112);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var membership = Assert.Single(await ListOwnedMembershipsAsync(gift));
        Assert.Empty(await ListOwnedMembershipsAsync(buyer));

        var path = $"{MembershipsRoute}/{membership.MembershipId}/activation";
        using var wrongUser = await buyer.PutAsJsonAsync(path, new ScheduleMembershipActivationRequest(Today));
        Assert.Equal(HttpStatusCode.NotFound, wrongUser.StatusCode);
        foreach (var start in new DateOnly?[] { null, Today.AddDays(-1), Today.AddDays(31) })
        {
            using var invalid = await gift.PutAsJsonAsync(path, new ScheduleMembershipActivationRequest(start));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("MSG-25", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        var chosen = Today.AddDays(30);
        var scheduled = await ActivateMembershipAsync(gift, membership.MembershipId, chosen);
        Assert.Equal(chosen, scheduled.Membership.StartDate);
        Assert.Equal(scheduled.Membership, (await ActivateMembershipAsync(gift, membership.MembershipId, chosen)).Membership);
        using var different = await gift.PutAsJsonAsync(path, new ScheduleMembershipActivationRequest(Today));
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DayThirtyAssignsAStartDateToPaidMembershipsWithNoSelection(bool hasSameService)
    {
        using var buyer = Client("buyer", "Customer");
        var purchasedOn = Today;
        if (hasSameService)
        {
            await UpdateRenewalPlanAsync("Basic 60 days", 500_000m, duration: 60);
            var first = await CreateAsync(buyer, Purchase([new(_basic)]));
            using var firstConfirmation = await NotifyAsync(first, 113);
            Assert.Equal(HttpStatusCode.OK, firstConfirmation.StatusCode);
            await ActivateMembershipAsync(buyer, Assert.Single(await ListOwnedMembershipsAsync(buyer)).MembershipId, purchasedOn);
        }

        var pending = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var confirmation = await NotifyAsync(pending, 114);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var awaiting = Assert.Single(await ListOwnedMembershipsAsync(buyer), row => row.Status == MembershipStatuses.AwaitingActivation);

        _clock.Now = _clock.Now.AddDays(29);
        await RunJobAsync((service, ct) => service.AdvanceMembershipsAsync(ct));
        Assert.Equal(MembershipStatuses.AwaitingActivation, (await ReadMembershipDetailAsync(buyer, awaiting.MembershipId)).Membership.Status);

        _clock.Now = _clock.Now.AddDays(1);
        await RunJobAsync((service, ct) => service.AdvanceMembershipsAsync(ct));
        var dated = (await ReadMembershipDetailAsync(buyer, awaiting.MembershipId)).Membership;
        Assert.Equal(purchasedOn.AddDays(hasSameService ? 60 : 30), dated.StartDate);
        Assert.Equal(hasSameService ? MembershipStatuses.Scheduled : MembershipStatuses.Active, dated.Status);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Contains("Member", await RolesAsync(scope.ServiceProvider.GetRequiredService<VfmsDbContext>(), "buyer"));
    }

    [Fact]
    public async Task MembershipDetailShowsFrozenStatusAndStoredFreezePeriods()
    {
        var membership = await SeedMembershipAsync(Today.AddDays(6), MembershipStatus.Frozen);
        var requestedStart = Today.AddDays(-3);
        var requestedEnd = Today.AddDays(2);
        var actualStart = Today.AddDays(-2);
        var previousStart = Today.AddDays(-10);
        var previousEnd = Today.AddDays(-8);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            // Freeze workflows are outside this use case. Seed an existing record on the purchased membership.
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.MemberMemberships.SingleAsync(row => row.Id == membership.Id);
            stored.TotalFrozenDays = 6;
            stored.FreezeCount = 2;
            db.MembershipFreezes.AddRange(
                new MembershipFreeze
                {
                    MemberMembershipId = stored.Id,
                    RequestedByUserId = "member",
                    RequestedStartDate = previousStart,
                    RequestedEndDate = previousEnd,
                    ActualStartDate = previousStart,
                    ActualEndDate = previousEnd,
                    RequestStatus = FreezeRequestStatus.Completed
                },
                new MembershipFreeze
                {
                    MemberMembershipId = stored.Id,
                    RequestedByUserId = "member",
                    RequestedStartDate = requestedStart,
                    RequestedEndDate = requestedEnd,
                    ActualStartDate = actualStart,
                    RequestStatus = FreezeRequestStatus.Active,
                    ReviewedByUserId = "manager",
                    ReviewNote = "Private review note"
                });
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        var summary = Assert.Single(await ListOwnedMembershipsAsync(member));
        Assert.Equal("Frozen", summary.Status);
        using var response = await member.GetAsync($"{MembershipsRoute}/{membership.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<MembershipDetailResponse>())!;
        Assert.Equal(summary, detail.Membership);
        Assert.Equal(membership.StartDate, detail.Membership.StartDate);
        Assert.Equal(membership.EndDate, detail.Membership.EndDate);
        Assert.Equal(6, detail.Freeze.TotalFrozenDays);
        Assert.Equal(2, detail.Freeze.FreezeCount);
        Assert.Equal([
            new MembershipFreezePeriodResponse(requestedStart, requestedEnd, actualStart, null, "Active", RequestedDays: 6),
            new MembershipFreezePeriodResponse(previousStart, previousEnd, previousStart, previousEnd, "Completed", RequestedDays: 3)
        ], detail.Freeze.History.Select(period => period with { SubmittedAt = null }));
        Assert.All(detail.Freeze.History, period => Assert.NotNull(period.SubmittedAt));
        Assert.DoesNotContain("Private review note", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MembershipQueriesShowThePaidRenewalAndKeepTheExpiredOriginalAfterActivation()
    {
        var original = await SeedMembershipAsync(Today.AddDays(6), MembershipStatus.Active);
        using var member = Client("member", "Member");
        var renewal = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute,
            new RenewMembershipRequest(original.Id)), HttpStatusCode.Created);
        Assert.Equal(original.Id, Assert.Single(await ListOwnedMembershipsAsync(member)).MembershipId);
        using var callback = await NotifyAsync(renewal, id: 111);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        var memberships = await ListOwnedMembershipsAsync(member);
        Assert.Equal(2, memberships.Length);
        var scheduled = Assert.Single(memberships, membership => membership.MembershipId != original.Id);
        var start = original.EndDate.AddDays(1);
        Assert.Equal(new MembershipSummaryResponse(scheduled.MembershipId, _basic, "Basic", "GYM", start,
            start.AddDays(29), "Scheduled") { DurationValue = 30, DurationUnit = MembershipDurationUnit.Day }, scheduled);
        Assert.Equal(scheduled, (await ReadMembershipDetailAsync(member, scheduled.MembershipId)).Membership);

        _clock.Now = _clock.Now.AddDays(7);
        var expired = await ReadMembershipDetailAsync(member, original.Id);
        Assert.Equal("Expired", expired.Membership.Status);
        Assert.Equal(original.EndDate, expired.Membership.EndDate);
        await RunJobAsync((service, ct) => service.AdvanceMembershipsAsync(ct));
        Assert.Equal(expired.Membership, (await ReadMembershipDetailAsync(member, original.Id)).Membership);
        var activated = await ReadMembershipDetailAsync(member, scheduled.MembershipId);
        Assert.Equal(scheduled with { Status = "Active" }, activated.Membership);
    }

    private static async Task<MembershipSummaryResponse[]> ListOwnedMembershipsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MembershipSummaryResponse[]>($"{MembershipsRoute}/me"))!;

    private static async Task<MembershipDetailResponse> ReadMembershipDetailAsync(HttpClient client, int membershipId) =>
        (await client.GetFromJsonAsync<MembershipDetailResponse>($"{MembershipsRoute}/{membershipId}"))!;

    private static async Task<MembershipDetailResponse> ActivateMembershipAsync(HttpClient client, int membershipId, DateOnly startDate)
    {
        using var response = await client.PutAsJsonAsync($"{MembershipsRoute}/{membershipId}/activation", new ScheduleMembershipActivationRequest(startDate));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MembershipDetailResponse>())!;
    }
}
