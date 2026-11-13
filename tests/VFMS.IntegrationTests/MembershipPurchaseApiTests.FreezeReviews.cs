using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string FreezeReviewsRoute = "/api/manager/membership-freeze-requests";

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData(" ", "Manager", HttpStatusCode.Unauthorized)]
    [InlineData("member", "Member", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Admin", HttpStatusCode.Forbidden)]
    public async Task EveryFreezeReviewEndpointRequiresAnAuthenticatedManager(string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var queue = await client.GetAsync(FreezeReviewsRoute);
        using var detail = await client.GetAsync($"{FreezeReviewsRoute}/1");
        using var approve = await client.PostAsync($"{FreezeReviewsRoute}/1/approve", null);
        using var reject = await client.PostAsJsonAsync($"{FreezeReviewsRoute}/1/reject", new RejectMembershipFreezeRequest("Reason"));
        using var earlyApprove = await client.PostAsync($"{FreezeReviewsRoute}/1/early-unfreeze/approve", null);
        using var earlyReject = await client.PostAsJsonAsync($"{FreezeReviewsRoute}/1/early-unfreeze/reject", new RejectMembershipFreezeRequest("Reason"));
        Assert.All(new[] { queue, detail, approve, reject, earlyApprove, earlyReject }, response =>
            Assert.Equal(expected, response.StatusCode));
    }

    [Fact]
    public async Task ManagerQueueSeparatesNormalAndEarlyUnfreezeRequestsAndPaginates()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        var other = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active, "buyer", "Customer");
        using var member = Client("member", "Member");
        using var otherMember = Client("buyer", "Member");
        using var manager = Client("manager", "Manager");
        var pending = await CreateFreezeRequestAsync(member, membership.Id, days: 4);
        var effective = await CreateFreezeRequestAsync(otherMember, other.Id, start: Today, days: 4);
        await ApproveFreezeReviewAsync(manager, effective.FreezeRequestId);
        await ReadFreezeRequestResponseAsync(await otherMember.PostAsync(
            $"{FreezeRequestsRoute}/{effective.FreezeRequestId}/early-unfreeze", null));

        var normal = await ReadFreezeReviewQueueAsync(manager, "?type=Freeze");
        var normalRow = Assert.Single(normal.Items);
        Assert.Equal(pending.FreezeRequestId, normalRow.FreezeRequestId);
        Assert.Equal("Freeze", normalRow.RequestType);
        Assert.Equal("member", normalRow.MemberName);
        Assert.Equal("member@example.test", normalRow.MemberEmail);
        Assert.Equal("Basic", normalRow.PlanName);
        Assert.Equal("Pending", normalRow.Status);
        var early = Assert.Single((await ReadFreezeReviewQueueAsync(manager, "?type=EarlyUnfreeze")).Items);
        Assert.Equal(effective.FreezeRequestId, early.FreezeRequestId);
        Assert.Equal("EarlyUnfreeze", early.RequestType);
        Assert.Equal("Pending", early.Status);

        var paged = await ReadFreezeReviewQueueAsync(manager, "?page=1&pageSize=1");
        Assert.Equal(1, paged.Page);
        Assert.Equal(1, paged.PageSize);
        Assert.Equal(2, paged.TotalCount);
        var first = Assert.Single(paged.Items);
        var second = Assert.Single((await ReadFreezeReviewQueueAsync(manager, "?page=2&pageSize=1")).Items);
        Assert.NotEqual(first.FreezeRequestId, second.FreezeRequestId);

        var detail = (await manager.GetFromJsonAsync<MembershipFreezeReviewDetailResponse>(
            $"{FreezeReviewsRoute}/{pending.FreezeRequestId}?type=Freeze"))!;
        Assert.Equal(normalRow, detail.Request);
        Assert.Equal(membership.Id, detail.Membership.MembershipId);
        Assert.Equal(membership.StartDate, detail.Membership.StartDate);
        Assert.Equal(membership.EndDate, detail.Membership.EndDate);
        Assert.Equal("Travel", detail.Reason);
        Assert.Equal(10, detail.RemainingQuota);
        Assert.Equal(2, detail.AppliedPolicy?.Policy?.MinimumFreezeDays);
        Assert.True(detail.CanApprove);
        Assert.True(detail.CanReject);
        using var missing = await manager.GetAsync($"{FreezeReviewsRoute}/{int.MaxValue}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FreezeApprovalExtendsExpiryAndConsumesQuotaExactlyOnce(int startOffset)
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var pending = await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(startOffset), days: 4);
        var approved = await ApproveFreezeReviewAsync(manager, pending.FreezeRequestId);
        Assert.Equal("Approved", approved.Request.Status);
        Assert.Equal(4, approved.AppliedExpiryExtensionDays);
        Assert.Equal(membership.EndDate.AddDays(4), approved.Membership.EndDate);
        Assert.Equal(startOffset == 0 ? "Frozen" : "Active", approved.Membership.Status);
        Assert.False(approved.CanApprove);
        Assert.False(approved.CanReject);
        using var repeat = await manager.PostAsync($"{FreezeReviewsRoute}/{pending.FreezeRequestId}/approve", null);
        using var opposite = await manager.PostAsJsonAsync($"{FreezeReviewsRoute}/{pending.FreezeRequestId}/reject",
            new RejectMembershipFreezeRequest("Stale decision"));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, opposite.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.MemberMemberships.SingleAsync();
        Assert.Equal(membership.EndDate.AddDays(4), stored.EndDate);
        Assert.Equal(4, stored.TotalFrozenDays);
        Assert.Equal(1, stored.FreezeCount);
        var freeze = await db.MembershipFreezes.SingleAsync();
        Assert.Equal(FreezeRequestStatus.Approved, freeze.RequestStatus);
        Assert.Equal(4, freeze.AppliedExpiryExtensionDays);
        Assert.Equal("manager", freeze.ReviewedByUserId);
        Assert.Equal(_clock.Now.UtcDateTime, freeze.ReviewedAt);
        Assert.Equal(startOffset == 0 ? Today : (DateOnly?)null, freeze.ActualStartDate);
        Assert.Equal("member", (await db.Notifications.SingleAsync()).RecipientUserId);
        var audit = await db.AuditLogs.SingleAsync(row => row.AuditEntity == nameof(MembershipFreeze));
        Assert.Equal("manager", audit.ActorUserId);
        Assert.Equal(freeze.Id, audit.EntityId);
    }

    [Fact]
    public async Task ApprovalUsesTheSubmittedPolicySnapshotAfterManagerChangesTheGlobalPolicy()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id, days: 4);
        await ConfigureFreezeRequestsAsync(new(20, 30, 100, 100,
            [new(30, MembershipFreezeTierUnits.Days, 0)]));

        var approved = await ApproveFreezeReviewAsync(manager, request.FreezeRequestId);
        Assert.Equal("Approved", approved.Request.Status);
        Assert.Equal(membership.EndDate.AddDays(4), approved.Membership.EndDate);
        Assert.Equal(2, approved.AppliedPolicy?.Policy?.MinimumFreezeDays);
        Assert.Equal(10, Assert.Single(approved.AppliedPolicy!.Quotas).MaximumCumulativeFreezeDays);
    }

    [Fact]
    public async Task ApprovingOnePendingRequestMakesTheCompetingRequestFailDynamicQuotaRevalidation()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var first = await CreateFreezeRequestAsync(member, membership.Id, days: 7);
        var competing = await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(10), days: 7);
        await ApproveFreezeReviewAsync(manager, first.FreezeRequestId);
        using var insufficient = await manager.PostAsync($"{FreezeReviewsRoute}/{competing.FreezeRequestId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
        Assert.Equal("Pending", (await ReadOwnedFreezeRequestAsync(member, competing.FreezeRequestId)).Status);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.MemberMemberships.SingleAsync();
        Assert.Equal(membership.EndDate.AddDays(7), stored.EndDate);
        Assert.Equal(7, stored.TotalFrozenDays);
        Assert.Equal(1, stored.FreezeCount);
        Assert.Single(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FreezeRejectionRequiresAReason(string? reason)
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        using var response = await manager.PostAsJsonAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/reject",
            new RejectMembershipFreezeRequest(reason));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Pending", (await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).Status);
    }

    [Fact]
    public async Task FreezeRejectionStoresDecisionAndNotifiesMemberWithoutChangingTheEntitlement()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var pending = await CreateFreezeRequestAsync(member, membership.Id);
        var rejected = await ReadFreezeReviewResponseAsync(await manager.PostAsJsonAsync(
            $"{FreezeReviewsRoute}/{pending.FreezeRequestId}/reject", new RejectMembershipFreezeRequest("  Insufficient evidence  ")));
        Assert.Equal("Rejected", rejected.Request.Status);
        Assert.Equal("Insufficient evidence", rejected.ReviewNote);
        Assert.Equal(membership.EndDate, rejected.Membership.EndDate);
        using var retry = await manager.PostAsJsonAsync($"{FreezeReviewsRoute}/{pending.FreezeRequestId}/reject",
            new RejectMembershipFreezeRequest("Another decision"));
        using var approve = await manager.PostAsync($"{FreezeReviewsRoute}/{pending.FreezeRequestId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        var visibleToMember = await ReadOwnedFreezeRequestAsync(member, pending.FreezeRequestId);
        Assert.Equal("Insufficient evidence", visibleToMember.ReviewNote);
        Assert.Empty((await ReadFreezeReviewQueueAsync(manager)).Items);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var unchanged = await db.MemberMemberships.SingleAsync();
        Assert.Equal(MembershipStatus.Active, unchanged.MembershipStatus);
        Assert.Equal(membership.EndDate, unchanged.EndDate);
        Assert.Equal(0, unchanged.TotalFrozenDays);
        Assert.Equal(0, unchanged.FreezeCount);
        var freeze = await db.MembershipFreezes.SingleAsync();
        Assert.Equal("manager", freeze.ReviewedByUserId);
        Assert.Equal(_clock.Now.UtcDateTime, freeze.ReviewedAt);
        Assert.Null(freeze.AppliedExpiryExtensionDays);
        Assert.Contains("Insufficient evidence", (await db.Notifications.SingleAsync()).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledOrPastStartRequestsCannotBeApproved(bool cancelled)
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id);
        if (cancelled)
            await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/cancel", null));
        else
            _clock.Now = _clock.Now.AddDays(3);

        using var response = await manager.PostAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var unchanged = await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().MemberMemberships.SingleAsync();
        Assert.Equal(membership.EndDate, unchanged.EndDate);
        Assert.Equal(0, unchanged.TotalFrozenDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task EarlyUnfreezeApprovalReconcilesInclusiveActualDaysAndCannotBeAppliedTwice(int elapsedDays)
    {
        var (membership, request) = await PrepareApprovedFreezeAsync();
        _clock.Now = _clock.Now.AddDays(elapsedDays);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        var actual = elapsedDays + 1;
        var decision = await ApproveFreezeReviewAsync(manager, request.FreezeRequestId, early: true);
        Assert.Equal("Approved", decision.Request.Status);
        Assert.Equal("EarlyUnfreeze", decision.Request.RequestType);
        Assert.Equal("Active", decision.Membership.Status);
        Assert.Equal(membership.EndDate.AddDays(actual), decision.Membership.EndDate);
        Assert.Equal(actual, decision.ActualFrozenDays);
        Assert.Equal(actual, decision.AppliedExpiryExtensionDays);
        using var repeated = await manager.PostAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/early-unfreeze/approve", null);
        using var opposite = await manager.PostAsJsonAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/early-unfreeze/reject",
            new RejectMembershipFreezeRequest("Stale decision"));
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, opposite.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.MemberMemberships.SingleAsync();
        Assert.Equal(membership.EndDate.AddDays(actual), stored.EndDate);
        Assert.Equal(actual, stored.TotalFrozenDays);
        Assert.Equal(1, stored.FreezeCount);
        var freeze = await db.MembershipFreezes.SingleAsync();
        Assert.Equal(FreezeRequestStatus.Completed, freeze.RequestStatus);
        Assert.Equal(FreezeRequestStatus.Approved, freeze.EarlyUnfreezeRequestStatus);
        Assert.Equal(Today, freeze.ActualEndDate);
        Assert.Equal(_clock.Now.UtcDateTime, freeze.EarlyUnfreezeAt);
        Assert.Equal("manager", freeze.EarlyUnfreezeReviewedByUserId);
        Assert.Equal(_clock.Now.UtcDateTime, freeze.EarlyUnfreezeReviewedAt);
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(row => row.AuditEntity == nameof(MembershipFreeze)));
        var tracked = await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId);
        Assert.Equal("Completed", tracked.Status);
        Assert.Equal("Approved", tracked.EarlyUnfreezeStatus);
    }

    [Fact]
    public async Task EarlyUnfreezeRejectionPreservesTheApprovedFreezeAndAllowsMemberResubmission()
    {
        var (membership, request) = await PrepareApprovedFreezeAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        var rejected = await ReadFreezeReviewResponseAsync(await manager.PostAsJsonAsync(
            $"{FreezeReviewsRoute}/{request.FreezeRequestId}/early-unfreeze/reject", new RejectMembershipFreezeRequest("  Continue the agreed period  ")));
        Assert.Equal("Rejected", rejected.Request.Status);
        Assert.Equal("Continue the agreed period", rejected.EarlyUnfreezeReviewNote);
        var rejectedMemberView = await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId);
        Assert.Equal("Continue the agreed period", rejectedMemberView.EarlyUnfreezeReviewNote);
        Assert.NotNull(rejectedMemberView.EarlyUnfreezeReviewedAt);
        Assert.Equal("Frozen", rejected.Membership.Status);
        Assert.Equal(membership.EndDate.AddDays(7), rejected.Membership.EndDate);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(7, (await db.MemberMemberships.SingleAsync()).TotalFrozenDays);
            var stored = await db.MembershipFreezes.SingleAsync();
            Assert.Equal(FreezeRequestStatus.Approved, stored.RequestStatus);
            Assert.Equal(FreezeRequestStatus.Rejected, stored.EarlyUnfreezeRequestStatus);
            Assert.Null(stored.ActualEndDate);
            Assert.Null(stored.EarlyUnfreezeAt);
            Assert.Equal(7, stored.AppliedExpiryExtensionDays);
        }

        _clock.Now = _clock.Now.AddDays(1);
        var resubmitted = await ReadFreezeRequestResponseAsync(await member.PostAsync(
            $"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        Assert.Equal("Pending", resubmitted.EarlyUnfreezeStatus);
        Assert.Null(resubmitted.EarlyUnfreezeReviewNote);
        Assert.Null(resubmitted.EarlyUnfreezeReviewedAt);
        Assert.Equal(rejectedMemberView.ReviewedAt, resubmitted.ReviewedAt);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().MembershipFreezes.SingleAsync();
            Assert.Null(stored.EarlyUnfreezeReviewedByUserId);
            Assert.Equal("manager", stored.ReviewedByUserId);
        }
        var approved = await ApproveFreezeReviewAsync(manager, request.FreezeRequestId, early: true);
        Assert.Equal(membership.EndDate.AddDays(2), approved.Membership.EndDate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EarlyUnfreezeApprovalRejectsExpiredPeriodsAndUnknownLegacyExtensions(bool unknownExtension)
    {
        var (membership, request) = await PrepareApprovedFreezeAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        if (unknownExtension)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MembershipFreezes.SingleAsync()).AppliedExpiryExtensionDays = null;
            await db.SaveChangesAsync();
        }
        else
            _clock.Now = _clock.Now.AddDays(7);

        using var response = await manager.PostAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/early-unfreeze/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verification = _app.Services.CreateAsyncScope();
        var unchanged = await verification.ServiceProvider.GetRequiredService<VfmsDbContext>().MemberMemberships.SingleAsync();
        Assert.Equal(membership.EndDate.AddDays(7), unchanged.EndDate);
        Assert.Equal(7, unchanged.TotalFrozenDays);
        Assert.Equal(MembershipStatus.Frozen, unchanged.MembershipStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDecisionNotificationRollsBackNormalAndEarlyFreezeEffects(bool early)
    {
        MemberMembership membership;
        MembershipFreezeRequestResponse request;
        if (early)
            (membership, request) = await PrepareApprovedFreezeAsync();
        else
        {
            membership = await PrepareFreezeReviewMembershipAsync();
            using var requester = Client("member", "Member");
            request = await CreateFreezeRequestAsync(requester, membership.Id, start: Today, days: 7);
        }

        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        if (early)
        {
            _clock.Now = _clock.Now.AddDays(2);
            await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        }
        var before = await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var beforeMembership = await db.MemberMemberships.AsNoTracking().SingleAsync();
        var beforeNotifications = await db.Notifications.CountAsync();
        var beforeAudits = await db.AuditLogs.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_freeze_decision_notification BEFORE INSERT ON Notifications
            BEGIN
                SELECT RAISE(ABORT, 'test decision notification failure');
            END;
            """);

        var segment = early ? "/early-unfreeze" : "";
        using var failed = await manager.PostAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}{segment}/approve", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var after = await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        var afterMembership = await db.MemberMemberships.AsNoTracking().SingleAsync();
        Assert.Equal(beforeMembership.EndDate, afterMembership.EndDate);
        Assert.Equal(beforeMembership.MembershipStatus, afterMembership.MembershipStatus);
        Assert.Equal(beforeMembership.TotalFrozenDays, afterMembership.TotalFrozenDays);
        Assert.Equal(beforeMembership.FreezeCount, afterMembership.FreezeCount);
        Assert.Equal(beforeNotifications, await db.Notifications.CountAsync());
        Assert.Equal(beforeAudits, await db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentManagersProduceExactlyOneDecisionAndOneSetOfEffects(bool competingRejection)
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        var request = await CreateFreezeRequestAsync(member, membership.Id, days: 4);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(row => row.GymName == "VFMS");
            var role = await db.ApplicationRoles.SingleAsync(row => row.RoleName == "Manager");
            db.ApplicationUsers.Add(User("review-manager-2", gym, role));
            await db.SaveChangesAsync();
        }

        var databasePath = Path.Combine(Path.GetTempPath(), $"vfms-freeze-review-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false, DefaultTimeout = 30, ForeignKeys = true
        }.ToString();
        try
        {
            await using (var target = new SqliteConnection(connectionString))
            {
                await target.OpenAsync();
                _connection.BackupDatabase(target);
            }

            using var start = new Barrier(2);
            var decisions = new[]
            {
                RunConcurrentReviewAsync(connectionString, request.FreezeRequestId, "manager", reject: false, start),
                RunConcurrentReviewAsync(connectionString, request.FreezeRequestId, "review-manager-2", competingRejection, start)
            };
            var results = await Task.WhenAll(decisions);
            Assert.Single(results, result => result.IsSuccess);
            Assert.Single(results, result => !result.IsSuccess && result.StatusCode == 409);

            await using var verification = new TestContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connectionString).Options);
            var storedRequest = await verification.MembershipFreezes.SingleAsync();
            var storedMembership = await verification.MemberMemberships.SingleAsync();
            var approved = storedRequest.RequestStatus == FreezeRequestStatus.Approved;
            Assert.Equal(approved ? membership.EndDate.AddDays(4) : membership.EndDate, storedMembership.EndDate);
            Assert.Equal(approved ? 4 : 0, storedMembership.TotalFrozenDays);
            Assert.Equal(approved ? 1 : 0, storedMembership.FreezeCount);
            Assert.Equal(results[0].IsSuccess ? "manager" : "review-manager-2", storedRequest.ReviewedByUserId);
            Assert.Single(await verification.Notifications.ToListAsync());
            Assert.Single(await verification.AuditLogs.Where(row => row.AuditEntity == nameof(MembershipFreeze)).ToListAsync());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private Task<Result<MembershipFreezeReviewDetailResponse>> RunConcurrentReviewAsync(
        string connectionString, int requestId, string actor, bool reject, Barrier start) => Task.Run(async () =>
    {
        await using var db = new TestContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connectionString).Options);
        var service = new MembershipFreezeReviewService(new MembershipFreezeReviewRepository(db),
            new MembershipFreezeRequestRepository(db), new MembershipFreezePolicyRepository(db),
            new RejectMembershipFreezeRequestValidator(), _clock);
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Concurrent review workers did not reach the start barrier.");
        return reject
            ? await service.RejectAsync(actor, requestId, false, new("Competing decision"), null, CancellationToken.None)
            : await service.ApproveAsync(actor, requestId, false, null, CancellationToken.None);
    });

    private async Task<MemberMembership> PrepareFreezeReviewMembershipAsync()
    {
        var membership = await PrepareFreezeMembershipAsync();
        await ConfigureFreezeRequestsAsync(RequestPolicy with { MinimumRequestDays = 0 });
        return membership;
    }

    private async Task<(MemberMembership Membership, MembershipFreezeRequestResponse Request)> PrepareApprovedFreezeAsync()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id, start: Today, days: 7);
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId);
        return (membership, request);
    }

    private static async Task<PagedResponse<MembershipFreezeReviewSummaryResponse>> ReadFreezeReviewQueueAsync(
        HttpClient manager, string query = "") =>
        (await manager.GetFromJsonAsync<PagedResponse<MembershipFreezeReviewSummaryResponse>>($"{FreezeReviewsRoute}{query}"))!;

    private static async Task<MembershipFreezeReviewDetailResponse> ApproveFreezeReviewAsync(HttpClient manager, int requestId, bool early = false)
    {
        var segment = early ? "/early-unfreeze" : "";
        return await ReadFreezeReviewResponseAsync(await manager.PostAsync($"{FreezeReviewsRoute}/{requestId}{segment}/approve", null));
    }

    private static async Task<MembershipFreezeReviewDetailResponse> ReadFreezeReviewResponseAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK, got {response.StatusCode}. {body}");
            return JsonSerializer.Deserialize<MembershipFreezeReviewDetailResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }
}
