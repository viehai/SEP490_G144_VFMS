using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string FreezeRequestsRoute = "/api/membership-freeze-requests";
    private static readonly MembershipFreezePolicyRequest RequestPolicy = new(2, 7, 2, 5,
        [new(30, MembershipFreezeTierUnits.Days, 10)]);

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData(" ", "Member", HttpStatusCode.Unauthorized)]
    [InlineData("buyer", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    public async Task FreezeRequestEndpointsRequireAnAuthenticatedMember(string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        var request = new MembershipFreezeRequest(1, Today.AddDays(2), 2, "Travel");
        using var eligibility = await client.GetAsync("/api/memberships/1/freeze-eligibility");
        using var list = await client.GetAsync(FreezeRequestsRoute);
        using var detail = await client.GetAsync($"{FreezeRequestsRoute}/1");
        using var create = await client.PostAsJsonAsync(FreezeRequestsRoute, request);
        using var edit = await client.PutAsJsonAsync($"{FreezeRequestsRoute}/1", request);
        using var cancel = await client.PostAsync($"{FreezeRequestsRoute}/1/cancel", null);
        using var early = await client.PostAsync($"{FreezeRequestsRoute}/1/early-unfreeze", null);
        Assert.All(new[] { eligibility, list, detail, create, edit, cancel, early }, response =>
            Assert.Equal(expected, response.StatusCode));
    }

    [Fact]
    public async Task FreezeSubmissionUsesPolicyAndIdentityAndKeepsThePaidEntitlementUnchanged()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        Assert.Empty((await member.GetFromJsonAsync<MembershipFreezeRequestResponse[]>(FreezeRequestsRoute))!);
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.True(eligibility.Eligible);
        Assert.Equal(9, eligibility.PaidDays);
        Assert.Equal(10, eligibility.RemainingQuota);
        Assert.Equal(Today, eligibility.Today);
        Assert.Equal(Today.AddDays(2), eligibility.EarliestStartDate);
        Assert.Equal(2, eligibility.Policy?.MinimumFreezeDays);
        Assert.Equal(7, eligibility.Policy?.MaximumFreezeDays);
        Assert.Equal(5, eligibility.Policy?.EligibilityPaidDays);

        var submitted = await ReadFreezeRequestResponseAsync(await member.PostAsJsonAsync(FreezeRequestsRoute, new
        {
            membershipId = membership.Id,
            startDate = Today.AddDays(2),
            requestedDays = 2,
            reason = "  Travel  ",
            memberId = "buyer",
            requestedByUserId = "buyer",
            remainingQuota = 999,
            status = "Approved"
        }), HttpStatusCode.Created);
        Assert.Equal(membership.Id, submitted.MembershipId);
        Assert.Equal("Basic", submitted.PlanName);
        Assert.Equal(Today.AddDays(2), submitted.StartDate);
        Assert.Equal(Today.AddDays(3), submitted.EndDate);
        Assert.Equal(2, submitted.RequestedDays);
        Assert.Equal("Travel", submitted.Reason);
        Assert.Equal("Pending", submitted.Status);
        Assert.True(submitted.CanEdit);
        Assert.True(submitted.CanCancel);
        Assert.False(submitted.CanRequestEarlyUnfreeze);
        Assert.Null(submitted.EarlyUnfreezeRequestedAt);
        Assert.Equal(2, submitted.AppliedPolicy?.Policy?.MinimumFreezeDays);
        var readBack = await ReadOwnedFreezeRequestAsync(member, submitted.FreezeRequestId);
        Assert.Equal(submitted.FreezeRequestId, readBack.FreezeRequestId);
        Assert.Equal(submitted.Status, readBack.Status);
        Assert.Equal(submitted.Reason, readBack.Reason);
        Assert.Equal(submitted.StartDate, readBack.StartDate);
        Assert.Equal(submitted.EndDate, readBack.EndDate);
        Assert.Equal(submitted.SubmittedAt, readBack.SubmittedAt);
        Assert.Equal(submitted.AppliedPolicy?.Policy, readBack.AppliedPolicy?.Policy);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var storedRequest = await db.MembershipFreezes.SingleAsync();
        Assert.Equal("member", storedRequest.RequestedByUserId);
        Assert.Equal(FreezeRequestStatus.Pending, storedRequest.RequestStatus);
        var unchanged = await db.MemberMemberships.SingleAsync();
        Assert.Equal(membership.StartDate, unchanged.StartDate);
        Assert.Equal(membership.EndDate, unchanged.EndDate);
        Assert.Equal(MembershipStatus.Active, unchanged.MembershipStatus);
        Assert.Equal(0, unchanged.TotalFrozenDays);
        Assert.Equal(0, unchanged.FreezeCount);
    }

    [Fact]
    public async Task FreezeListsAndEveryResourceEndpointEnforceBeneficiaryOwnership()
    {
        var owned = await PrepareFreezeMembershipAsync();
        var foreign = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active, "buyer", "Customer");
        using var member = Client("member", "Member");
        using var other = Client("buyer", "Member");
        var ownRequest = await CreateFreezeRequestAsync(member, owned.Id);
        var otherRequest = await CreateFreezeRequestAsync(other, foreign.Id);

        var listed = Assert.Single((await member.GetFromJsonAsync<MembershipFreezeRequestResponse[]>(
            $"{FreezeRequestsRoute}?memberId=buyer"))!);
        Assert.Equal(ownRequest.FreezeRequestId, listed.FreezeRequestId);
        using var eligibility = await member.GetAsync($"/api/memberships/{foreign.Id}/freeze-eligibility");
        using var create = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(foreign.Id));
        using var detail = await member.GetAsync($"{FreezeRequestsRoute}/{otherRequest.FreezeRequestId}");
        using var missing = await member.GetAsync($"{FreezeRequestsRoute}/{int.MaxValue}");
        using var edit = await member.PutAsJsonAsync($"{FreezeRequestsRoute}/{otherRequest.FreezeRequestId}", FreezeInput(owned.Id));
        using var cancel = await member.PostAsync($"{FreezeRequestsRoute}/{otherRequest.FreezeRequestId}/cancel", null);
        using var early = await member.PostAsync($"{FreezeRequestsRoute}/{otherRequest.FreezeRequestId}/early-unfreeze", null);
        Assert.All(new[] { eligibility, create, detail, missing, edit, cancel, early }, response =>
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        using var move = await member.PutAsJsonAsync($"{FreezeRequestsRoute}/{ownRequest.FreezeRequestId}", FreezeInput(foreign.Id));
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
        Assert.Equal(owned.Id, (await ReadOwnedFreezeRequestAsync(member, ownRequest.FreezeRequestId)).MembershipId);
    }

    [Theory]
    [InlineData(2, 2, HttpStatusCode.Created)]
    [InlineData(7, 2, HttpStatusCode.Created)]
    [InlineData(1, 2, HttpStatusCode.BadRequest)]
    [InlineData(8, 2, HttpStatusCode.BadRequest)]
    [InlineData(2, 1, HttpStatusCode.BadRequest)]
    [InlineData(2, -1, HttpStatusCode.BadRequest)]
    public async Task FreezeDurationAndAdvanceNoticeUseTheConfiguredInclusiveBoundaries(
        int requestedDays, int noticeDays, HttpStatusCode expected)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute,
            new MembershipFreezeRequest(membership.Id, Today.AddDays(noticeDays), requestedDays, "Travel"));
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Created)
        {
            var created = (await response.Content.ReadFromJsonAsync<MembershipFreezeRequestResponse>())!;
            Assert.Equal(Today.AddDays(noticeDays + requestedDays - 1), created.EndDate);
        }
    }

    [Theory]
    [InlineData(4, HttpStatusCode.BadRequest)]
    [InlineData(5, HttpStatusCode.Created)]
    public async Task FreezeEligibilityCountsElapsedGymCalendarDaysFromActivation(int elapsedDays, HttpStatusCode expected)
    {
        var membership = await PrepareFreezeMembershipAsync(elapsedDays);
        using var member = Client("member", "Member");
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.Equal(elapsedDays, eligibility.PaidDays);
        Assert.Equal(expected == HttpStatusCode.Created, eligibility.Eligible);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Frozen")]
    [InlineData("Expired")]
    [InlineData("Suspended")]
    [InlineData("Cancelled")]
    public async Task NonActiveMembershipsCannotSubmitNormalFreezeRequests(string status)
    {
        await ConfigureFreezeRequestsAsync();
        var expiry = status switch
        {
            "Scheduled" => Today.AddDays(30),
            "Expired" => Today.AddDays(-1),
            _ => Today.AddDays(20)
        };
        var membership = await SeedMembershipAsync(expiry, Enum.Parse<MembershipStatus>(status));
        using var member = Client("member", "Member");
        Assert.False((await GetFreezeEligibilityAsync(member, membership.Id)).Eligible);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ActiveMembershipPastItsExpiryCannotSubmitFreezeBeforeTheLifecycleJobRuns()
    {
        await ConfigureFreezeRequestsAsync();
        var membership = await SeedMembershipAsync(Today.AddDays(-1), MembershipStatus.Active);
        using var member = Client("member", "Member");
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.Equal("Expired", eligibility.Membership.Status);
        Assert.False(eligibility.Eligible);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FreezeSubmissionRequiresAnActivePolicyAndAnApplicableTier()
    {
        var membership = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active);
        using var member = Client("member", "Member");
        using var noPolicy = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noPolicy.StatusCode);

        await ConfigureFreezeRequestsAsync(RequestPolicy with { Quotas = [new(1, MembershipFreezeTierUnits.Months, 10)] });
        Assert.False((await GetFreezeEligibilityAsync(member, membership.Id)).Eligible);
        using var noTier = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(HttpStatusCode.BadRequest, noTier.StatusCode);
    }

    [Theory]
    [InlineData(6, HttpStatusCode.Created)]
    [InlineData(7, HttpStatusCode.BadRequest)]
    public async Task FreezeQuotaUsesPersistedUsageWithoutDoubleCountingCompletedHistory(int days, HttpStatusCode expected)
    {
        var membership = await PrepareFreezeMembershipAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.MemberMemberships.SingleAsync();
            stored.TotalFrozenDays = 4;
            stored.FreezeCount = 1;
            db.MembershipFreezes.Add(StoredFreeze(membership.Id, FreezeRequestStatus.Completed, Today.AddDays(-6), 4));
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        Assert.Equal(6, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingQuota);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id, days: days));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ApprovedFreezeDaysCountAgainstQuotaBeforeTheFreezeBegins()
    {
        var membership = await PrepareFreezeMembershipAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.MembershipFreezes.Add(StoredFreeze(membership.Id, FreezeRequestStatus.Approved, Today.AddDays(12), 4));
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        Assert.Equal(6, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingQuota);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id, days: 7));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Approved")]
    public async Task FreezePeriodsCannotOverlapAtAnInclusiveBoundary(string status)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var first = await CreateFreezeRequestAsync(member, membership.Id, days: 3);
        if (status == "Approved")
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MembershipFreezes.SingleAsync()).RequestStatus = FreezeRequestStatus.Approved;
            await db.SaveChangesAsync();
        }

        using var overlap = await member.PostAsJsonAsync(FreezeRequestsRoute,
            FreezeInput(membership.Id, start: first.EndDate, days: 2));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        var adjacent = await CreateFreezeRequestAsync(member, membership.Id, start: first.EndDate.AddDays(1));
        Assert.Equal(first.EndDate.AddDays(1), adjacent.StartDate);
    }

    [Fact]
    public async Task DisjointPendingRequestsDoNotReserveOrPermanentlyDeductQuota()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        await CreateFreezeRequestAsync(member, membership.Id, days: 7);
        await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(10), days: 7);
        Assert.Equal(10, (await GetFreezeEligibilityAsync(member, membership.Id)).RemainingQuota);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(2, await db.MembershipFreezes.CountAsync());
        Assert.Equal(0, (await db.MemberMemberships.SingleAsync()).TotalFrozenDays);
    }

    [Fact]
    public async Task EditingPendingRequestsUsesTheCurrentPolicyAndPreservesThePreviousPolicyUntilEdited()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var original = await CreateFreezeRequestAsync(member, membership.Id);
        var renamed = await ReadFreezeRequestResponseAsync(await member.PutAsJsonAsync(
            $"{FreezeRequestsRoute}/{original.FreezeRequestId}", FreezeInput(membership.Id) with { Reason = "Updated reason" }));
        Assert.Equal("Updated reason", renamed.Reason);

        await ConfigureFreezeRequestsAsync(new(3, 6, 4, 5, [new(30, MembershipFreezeTierUnits.Days, 9)]));
        var history = await ReadOwnedFreezeRequestAsync(member, original.FreezeRequestId);
        Assert.Equal(2, history.AppliedPolicy?.Policy?.MinimumFreezeDays);
        using var invalidEdit = await member.PutAsJsonAsync($"{FreezeRequestsRoute}/{original.FreezeRequestId}",
            FreezeInput(membership.Id, start: Today.AddDays(5)));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEdit.StatusCode);

        var edited = await ReadFreezeRequestResponseAsync(await member.PutAsJsonAsync(
            $"{FreezeRequestsRoute}/{original.FreezeRequestId}", FreezeInput(membership.Id, start: Today.AddDays(5), days: 3)));
        Assert.Equal(original.FreezeRequestId, edited.FreezeRequestId);
        Assert.Equal(original.SubmittedAt, edited.SubmittedAt);
        Assert.Equal(Today.AddDays(7), edited.EndDate);
        Assert.Equal(3, edited.AppliedPolicy?.Policy?.MinimumFreezeDays);
        Assert.Equal(9, Assert.Single(edited.AppliedPolicy!.Quotas).MaximumCumulativeFreezeDays);
    }

    [Fact]
    public async Task CancellingPendingFreezePreservesItsHistoryAndTheMembership()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var created = await CreateFreezeRequestAsync(member, membership.Id);
        var cancelled = await ReadFreezeRequestResponseAsync(await member.PostAsync(
            $"{FreezeRequestsRoute}/{created.FreezeRequestId}/cancel", null));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.False(cancelled.CanEdit);
        Assert.False(cancelled.CanCancel);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.False((await db.MembershipFreezes.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        var unchanged = await db.MemberMemberships.SingleAsync();
        Assert.Equal(MembershipStatus.Active, unchanged.MembershipStatus);
        Assert.Equal(membership.EndDate, unchanged.EndDate);
        Assert.Equal(0, unchanged.TotalFrozenDays);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task MembersCannotEditOrCancelNonPendingFreezes(string status)
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var created = await CreateFreezeRequestAsync(member, membership.Id);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MembershipFreezes.SingleAsync()).RequestStatus = Enum.Parse<FreezeRequestStatus>(status);
            await db.SaveChangesAsync();
        }

        using var edit = await member.PutAsJsonAsync($"{FreezeRequestsRoute}/{created.FreezeRequestId}", FreezeInput(membership.Id));
        using var cancel = await member.PostAsync($"{FreezeRequestsRoute}/{created.FreezeRequestId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        var unchanged = await ReadOwnedFreezeRequestAsync(member, created.FreezeRequestId);
        Assert.Equal(status, unchanged.Status);
        Assert.False(unchanged.CanEdit);
        Assert.False(unchanged.CanCancel);
    }

    [Fact]
    public async Task FreezeTierComesFromThePurchasedComponentSnapshotAfterCatalogDurationChanges()
    {
        var membership = await PrepareFreezeMembershipAsync();
        await UpdateRenewalPlanAsync("Renamed plan", 600_000m, duration: 60);
        using var member = Client("member", "Member");
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.True(eligibility.Eligible);
        Assert.Equal(10, eligibility.RemainingQuota);
        var created = await CreateFreezeRequestAsync(member, membership.Id);
        Assert.Equal("Basic", created.PlanName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreezeTierSupportsLegacySnapshotsAndFallsBackOnlyWhenTheSnapshotIsAbsent(bool legacySnapshot)
    {
        var membership = await PrepareFreezeMembershipAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var component = await db.PackageComponents.SingleAsync(row => row.PackageId == _basic);
            var item = await db.OrderItems.SingleAsync();
            item.ComponentTermsSnapshot = legacySnapshot ? JsonSerializer.Serialize(new[]
            {
                new { component.Id, Type = component.ComponentType, DurationDays = 30, DurationMonths = (int?)null }
            }) : null;
            component.DurationDays = 60;
            await db.SaveChangesAsync();
        }

        await ConfigureFreezeRequestsAsync(RequestPolicy with
        {
            Quotas = [new(30, MembershipFreezeTierUnits.Days, 10), new(60, MembershipFreezeTierUnits.Days, 0)]
        });
        using var member = Client("member", "Member");
        var eligibility = await GetFreezeEligibilityAsync(member, membership.Id);
        Assert.Equal(legacySnapshot ? 10 : 0, eligibility.RemainingQuota);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.Id));
        Assert.Equal(legacySnapshot ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("PT")]
    [InlineData("YOGA")]
    public async Task PTEntitlementsAndWeeklyYogaCannotUseTheMembershipFreezeWorkflow(string type)
    {
        await ConfigureFreezeRequestsAsync(RequestPolicy with
        {
            EligibilityPaidDays = 0,
            Quotas = [new(30, MembershipFreezeTierUnits.Days, 10), new(7, MembershipFreezeTierUnits.Days, 10)]
        });
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var combo = await db.Packages.Include(row => row.Components).SingleAsync(row => row.Id == _combo);
            if (type == "PT")
                combo.Components.Add(new PackageComponent { ComponentType = ComponentType.PT, DurationDays = 30, SessionCount = 6 });
            else
            {
                var yoga = combo.Components.Single(component => component.ComponentType == ComponentType.YOGA);
                yoga.DurationMonths = null;
                yoga.DurationDays = 7;
            }
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        var purchase = await CreateAsync(member, Purchase([new(_combo)]));
        using var callback = await NotifyAsync(purchase, id: 130);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var membership = Assert.Single(await ListOwnedMembershipsAsync(member), row => row.ComponentType == type);
        Assert.False((await GetFreezeEligibilityAsync(member, membership.MembershipId)).Eligible);
        using var response = await member.PostAsJsonAsync(FreezeRequestsRoute, FreezeInput(membership.MembershipId));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RequestedFreezeMayRunPastCurrentExpiryWithoutExtendingTheMembership()
    {
        var membership = await PrepareFreezeMembershipAsync();
        using var member = Client("member", "Member");
        var created = await CreateFreezeRequestAsync(member, membership.Id, start: membership.EndDate, days: 7);
        Assert.Equal(membership.EndDate.AddDays(6), created.EndDate);
        Assert.Equal(membership.EndDate, (await ReadMembershipDetailAsync(member, membership.Id)).Membership.EndDate);
    }

    [Fact]
    public async Task EarlyUnfreezeIsAnIdempotentRequestAndKeepsTheFrozenEntitlementUnchanged()
    {
        var membership = await PrepareFreezeMembershipAsync();
        int requestId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.MemberMemberships.SingleAsync();
            stored.MembershipStatus = MembershipStatus.Frozen;
            stored.TotalFrozenDays = 2;
            stored.FreezeCount = 1;
            var freeze = StoredFreeze(membership.Id, FreezeRequestStatus.Active, Today.AddDays(-2), 6);
            freeze.ActualStartDate = freeze.RequestedStartDate;
            db.MembershipFreezes.Add(freeze);
            await db.SaveChangesAsync();
            requestId = freeze.Id;
        }

        using var member = Client("member", "Member");
        Assert.True((await ReadOwnedFreezeRequestAsync(member, requestId)).CanRequestEarlyUnfreeze);
        var requested = await ReadFreezeRequestResponseAsync(await member.PostAsync(
            $"{FreezeRequestsRoute}/{requestId}/early-unfreeze", null));
        Assert.Equal("Active", requested.Status);
        Assert.Equal("Pending", requested.EarlyUnfreezeStatus);
        Assert.NotNull(requested.EarlyUnfreezeRequestedAt);
        Assert.False(requested.CanRequestEarlyUnfreeze);
        var repeated = await ReadFreezeRequestResponseAsync(await member.PostAsync(
            $"{FreezeRequestsRoute}/{requestId}/early-unfreeze", null));
        Assert.Equal(requested.EarlyUnfreezeRequestedAt, repeated.EarlyUnfreezeRequestedAt);

        await using var settledScope = _app.Services.CreateAsyncScope();
        var settledDb = settledScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var unchanged = await settledDb.MemberMemberships.SingleAsync();
        Assert.Equal(MembershipStatus.Frozen, unchanged.MembershipStatus);
        Assert.Equal(membership.EndDate, unchanged.EndDate);
        Assert.Equal(2, unchanged.TotalFrozenDays);
        Assert.Equal(1, unchanged.FreezeCount);
        var preserved = await settledDb.MembershipFreezes.SingleAsync();
        Assert.Null(preserved.ActualEndDate);
        Assert.Null(preserved.EarlyUnfreezeAt);
        Assert.Equal(Today.AddDays(3), preserved.RequestedEndDate);
    }

    [Theory]
    [InlineData("Pending", -2, 6, "Frozen")]
    [InlineData("Approved", 2, 6, "Frozen")]
    [InlineData("Active", -2, 3, "Frozen")]
    [InlineData("Active", -2, 6, "Active")]
    public async Task EarlyUnfreezeRequiresAnOngoingFreezeWithTimeRemaining(
        string requestStatus, int startOffset, int duration, string membershipStatus)
    {
        var membership = await PrepareFreezeMembershipAsync();
        int requestId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MemberMemberships.SingleAsync()).MembershipStatus = Enum.Parse<MembershipStatus>(membershipStatus);
            var freeze = StoredFreeze(membership.Id, Enum.Parse<FreezeRequestStatus>(requestStatus), Today.AddDays(startOffset), duration);
            db.MembershipFreezes.Add(freeze);
            await db.SaveChangesAsync();
            requestId = freeze.Id;
        }

        using var member = Client("member", "Member");
        using var response = await member.PostAsync($"{FreezeRequestsRoute}/{requestId}/early-unfreeze", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<MemberMembership> PrepareFreezeMembershipAsync(int elapsedDays = 9)
    {
        await ConfigureFreezeRequestsAsync();
        return await SeedMembershipAsync(Today.AddDays(29 - elapsedDays), MembershipStatus.Active);
    }

    private async Task ConfigureFreezeRequestsAsync(MembershipFreezePolicyRequest? request = null)
    {
        using var manager = Client("manager", "Manager");
        await SaveFreezePolicyAsync(manager, request ?? RequestPolicy);
    }

    private MembershipFreezeRequest FreezeInput(int membershipId, DateOnly? start = null, int days = 2) =>
        new(membershipId, start ?? Today.AddDays(2), days, "Travel");

    private async Task<MembershipFreezeRequestResponse> CreateFreezeRequestAsync(
        HttpClient member, int membershipId, DateOnly? start = null, int days = 2) =>
        await ReadFreezeRequestResponseAsync(await member.PostAsJsonAsync(FreezeRequestsRoute,
            FreezeInput(membershipId, start, days)), HttpStatusCode.Created);

    private static async Task<MembershipFreezeEligibilityResponse> GetFreezeEligibilityAsync(HttpClient member, int membershipId) =>
        (await member.GetFromJsonAsync<MembershipFreezeEligibilityResponse>($"/api/memberships/{membershipId}/freeze-eligibility"))!;

    private static async Task<MembershipFreezeRequestResponse> ReadOwnedFreezeRequestAsync(HttpClient member, int requestId) =>
        (await member.GetFromJsonAsync<MembershipFreezeRequestResponse>($"{FreezeRequestsRoute}/{requestId}"))!;

    private static async Task<MembershipFreezeRequestResponse> ReadFreezeRequestResponseAsync(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}. {body}");
            return JsonSerializer.Deserialize<MembershipFreezeRequestResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }

    private static MembershipFreeze StoredFreeze(int membershipId, FreezeRequestStatus status, DateOnly start, int days) => new()
    {
        MemberMembershipId = membershipId,
        RequestedByUserId = "member",
        RequestedStartDate = start,
        RequestedEndDate = start.AddDays(days - 1),
        RequestStatus = status
    };
}
