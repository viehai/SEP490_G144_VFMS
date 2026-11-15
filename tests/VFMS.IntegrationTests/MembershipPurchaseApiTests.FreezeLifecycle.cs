using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task ApprovedFutureFreezeStartsAndCompletesWithoutReapplyingExpiryOrQuota()
    {
        var (membership, request) = await PrepareFutureFreezeAsync();
        var extendedExpiry = membership.EndDate.AddDays(3);
        var initialAudits = await FreezeLifecycleAuditCountAsync();
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Approved", MembershipStatus.Active,
            extendedExpiry, usedDays: 3, notifications: 1);

        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Active", MembershipStatus.Frozen,
            extendedExpiry, usedDays: 3, notifications: 2);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Active", MembershipStatus.Frozen,
            extendedExpiry, usedDays: 3, notifications: 2);

        _clock.Now = _clock.Now.AddDays(2);
        Assert.Equal(request.EndDate, Today);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Active", MembershipStatus.Frozen,
            extendedExpiry, usedDays: 3, notifications: 2);

        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Completed", MembershipStatus.Active,
            extendedExpiry, usedDays: 3, notifications: 3);
        Assert.Equal(initialAudits, await FreezeLifecycleAuditCountAsync());
        await using var scope = _app.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().MembershipFreezes.SingleAsync();
        Assert.Equal(request.StartDate, stored.ActualStartDate);
        Assert.Equal(request.EndDate, stored.ActualEndDate);
        Assert.Equal(3, stored.AppliedExpiryExtensionDays);
    }

    [Fact]
    public async Task ApprovalStartingTodayEntersEffectiveLifecycleOnce()
    {
        var (membership, request) = await PrepareApprovedFreezeAsync();
        await RunFreezeLifecycleAsync();
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Active", MembershipStatus.Frozen,
            membership.EndDate.AddDays(7), usedDays: 7, notifications: 2);
    }

    [Theory]
    [InlineData(2, "Active", MembershipStatus.Frozen)]
    [InlineData(5, "Completed", MembershipStatus.Active)]
    public async Task LifecycleCatchesUpAfterMissingTheApprovedStartDate(
        int elapsedDays, string expectedRequestStatus, MembershipStatus expectedMembershipStatus)
    {
        var (membership, request) = await PrepareFutureFreezeAsync();
        _clock.Now = _clock.Now.AddDays(elapsedDays);
        await RunFreezeLifecycleAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var freeze = await db.MembershipFreezes.SingleAsync();
        var stored = await db.MemberMemberships.SingleAsync();
        Assert.Equal(expectedRequestStatus, freeze.RequestStatus.ToString());
        Assert.Equal(request.StartDate, freeze.ActualStartDate);
        Assert.Equal(expectedMembershipStatus, stored.MembershipStatus);
        Assert.Equal(membership.EndDate.AddDays(3), stored.EndDate);
        Assert.Equal(3, stored.TotalFrozenDays);
        Assert.Equal(1, stored.FreezeCount);
        var notifications = await db.Notifications.CountAsync();
        await RunFreezeLifecycleAsync();
        Assert.Equal(notifications, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task PendingRequestExpiresOnlyAfterItsStartAndCannotBeReviewedOrEditedAfterward()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id, start: Today, days: 3);
        await RunFreezeLifecycleAsync();
        Assert.Equal("Pending", (await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId)).Status);

        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        await RunFreezeLifecycleAsync();
        var expired = await ReadOwnedFreezeRequestAsync(member, request.FreezeRequestId);
        Assert.Equal("Expired", expired.Status);
        Assert.False(expired.CanEdit);
        Assert.False(expired.CanCancel);
        Assert.Empty((await ReadFreezeReviewQueueAsync(manager)).Items);
        using var approve = await manager.PostAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/approve", null);
        using var reject = await manager.PostAsJsonAsync($"{FreezeReviewsRoute}/{request.FreezeRequestId}/reject",
            new RejectMembershipFreezeRequest("Too late"));
        using var edit = await member.PutAsJsonAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}", FreezeInput(membership.Id));
        Assert.All(new[] { approve, reject, edit }, response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Expired", MembershipStatus.Active,
            membership.EndDate, usedDays: 0, notifications: 1);
    }

    [Fact]
    public async Task LifecycleUsesApprovedStateAfterPolicyChangesAndLeavesEarlyCompletionTerminal()
    {
        var (membership, request) = await PrepareFutureFreezeAsync(days: 7);
        await ConfigureFreezeRequestsAsync(new(20, 30, 100, 100, [new(30, MembershipFreezeTierUnits.Days, 0)]));
        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        _clock.Now = _clock.Now.AddDays(2);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId, early: true);
        var notifications = await FreezeLifecycleNotificationCountAsync();

        await RunFreezeLifecycleAsync();
        _clock.Now = _clock.Now.AddDays(10);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Completed", MembershipStatus.Active,
            membership.EndDate.AddDays(3), usedDays: 3, notifications: notifications);
    }

    [Theory]
    [InlineData(1, 31)]
    [InlineData(12, 31)]
    public async Task LifecycleKeepsInclusiveDateSemanticsAcrossMonthAndYearBoundaries(int month, int day)
    {
        var start = new DateOnly(_clock.Now.Year + 1, month, day);
        _clock.Now = new DateTimeOffset(start.AddDays(-1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7));
        var (membership, request) = await PrepareFutureFreezeAsync(days: 2);
        Assert.Equal(start, request.StartDate);
        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        _clock.Now = _clock.Now.AddDays(1);
        Assert.Equal(request.EndDate, Today);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Active", MembershipStatus.Frozen,
            membership.EndDate.AddDays(2), usedDays: 2, notifications: 2);
        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        await AssertFreezeLifecycleStateAsync(request.FreezeRequestId, "Completed", MembershipStatus.Active,
            membership.EndDate.AddDays(2), usedDays: 2, notifications: 3);
    }

    [Theory]
    [InlineData(MembershipStatus.Frozen, MembershipStatus.Expired)]
    [InlineData(MembershipStatus.Suspended, MembershipStatus.Suspended)]
    public async Task CompletionDoesNotGrantAccessToAnExpiredOrSuspendedMembership(
        MembershipStatus currentStatus, MembershipStatus expectedStatus)
    {
        var (_, request) = await PrepareApprovedFreezeAsync();
        await RunFreezeLifecycleAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var membership = await db.MemberMemberships.SingleAsync();
            membership.EndDate = request.EndDate;
            membership.MembershipStatus = currentStatus;
            await db.SaveChangesAsync();
        }
        _clock.Now = _clock.Now.AddDays(7);
        await RunFreezeLifecycleAsync();
        await using var verification = _app.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(FreezeRequestStatus.Completed, (await context.MembershipFreezes.SingleAsync()).RequestStatus);
        Assert.Equal(expectedStatus, (await context.MemberMemberships.SingleAsync()).MembershipStatus);
    }

    [Fact]
    public async Task CompletingOneFreezeAndStartingAnAdjacentFreezeKeepsTheMembershipFrozen()
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var first = await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(1));
        var second = await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(3));
        await ApproveFreezeReviewAsync(manager, first.FreezeRequestId);
        await ApproveFreezeReviewAsync(manager, second.FreezeRequestId);
        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        _clock.Now = _clock.Now.AddDays(2);
        await RunFreezeLifecycleAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(FreezeRequestStatus.Completed, (await db.MembershipFreezes.SingleAsync(row => row.Id == first.FreezeRequestId)).RequestStatus);
        Assert.Equal(FreezeRequestStatus.Active, (await db.MembershipFreezes.SingleAsync(row => row.Id == second.FreezeRequestId)).RequestStatus);
        var stored = await db.MemberMemberships.SingleAsync();
        Assert.Equal(MembershipStatus.Frozen, stored.MembershipStatus);
        Assert.Equal(membership.EndDate.AddDays(4), stored.EndDate);
        Assert.Equal(4, stored.TotalFrozenDays);
        Assert.Equal(2, stored.FreezeCount);
    }

    [Fact]
    public async Task MalformedLegacyApprovalDoesNotStarveAnotherMembersValidFreeze()
    {
        var (membership, request) = await PrepareFutureFreezeAsync();
        var other = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active, "buyer", "Customer");
        using var buyer = Client("buyer", "Member");
        using var manager = Client("manager", "Manager");
        var valid = await CreateFreezeRequestAsync(buyer, other.Id, start: Today.AddDays(1), days: 3);
        await ApproveFreezeReviewAsync(manager, valid.FreezeRequestId);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MembershipFreezes.SingleAsync(row => row.Id == request.FreezeRequestId)).AppliedExpiryExtensionDays = null;
            await db.SaveChangesAsync();
        }

        _clock.Now = _clock.Now.AddDays(1);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMembershipFreezeLifecycleService>()
                .ProcessAsync(request.FreezeRequestId, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(409, result.StatusCode);
        }
        await RunFreezeLifecycleAsync();
        await using var verification = _app.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(FreezeRequestStatus.Approved,
            (await context.MembershipFreezes.SingleAsync(row => row.Id == request.FreezeRequestId)).RequestStatus);
        Assert.Equal(MembershipStatus.Active,
            (await context.MemberMemberships.SingleAsync(row => row.Id == membership.Id)).MembershipStatus);
        Assert.Equal(FreezeRequestStatus.Active,
            (await context.MembershipFreezes.SingleAsync(row => row.Id == valid.FreezeRequestId)).RequestStatus);
        Assert.Equal(MembershipStatus.Frozen,
            (await context.MemberMemberships.SingleAsync(row => row.Id == other.Id)).MembershipStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedLifecycleNotificationRollsBackThatRecordAndLetsTheNextRecordProceed(bool completing)
    {
        var (membership, request) = await PrepareFutureFreezeAsync();
        var other = await SeedMembershipAsync(Today.AddDays(20), MembershipStatus.Active, "buyer", "Customer");
        using var buyer = Client("buyer", "Member");
        using var manager = Client("manager", "Manager");
        var otherRequest = await CreateFreezeRequestAsync(buyer, other.Id, start: request.StartDate, days: 3);
        await ApproveFreezeReviewAsync(manager, otherRequest.FreezeRequestId);
        _clock.Now = _clock.Now.AddDays(1);
        if (completing)
        {
            await RunFreezeLifecycleAsync();
            _clock.Now = _clock.Now.AddDays(3);
        }

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var notifications = await db.Notifications.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_one_freeze_notification BEFORE INSERT ON Notifications
            WHEN NEW.RecipientUserId = 'member'
            BEGIN
                SELECT RAISE(ABORT, 'test lifecycle notification failure');
            END;
            """);
        await RunFreezeLifecycleAsync();
        var failed = await db.MembershipFreezes.AsNoTracking().SingleAsync(row => row.Id == request.FreezeRequestId);
        Assert.Equal(completing ? FreezeRequestStatus.Active : FreezeRequestStatus.Approved, failed.RequestStatus);
        Assert.Equal(completing ? MembershipStatus.Frozen : MembershipStatus.Active,
            (await db.MemberMemberships.AsNoTracking().SingleAsync(row => row.Id == membership.Id)).MembershipStatus);
        Assert.Equal(completing ? FreezeRequestStatus.Completed : FreezeRequestStatus.Active,
            (await db.MembershipFreezes.AsNoTracking().SingleAsync(row => row.Id == otherRequest.FreezeRequestId)).RequestStatus);
        Assert.Equal(notifications + 1, await db.Notifications.CountAsync());

        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_one_freeze_notification");
        await RunFreezeLifecycleAsync();
        Assert.Equal(notifications + 2, await db.Notifications.CountAsync());
        var recovered = await db.MemberMemberships.AsNoTracking().SingleAsync(row => row.Id == membership.Id);
        Assert.Equal(membership.EndDate.AddDays(3), recovered.EndDate);
        Assert.Equal(3, recovered.TotalFrozenDays);
        Assert.Equal(1, recovered.FreezeCount);
    }

    [Fact]
    public async Task ConcurrentLifecycleWorkersCommitOneStartAndOneNotification()
    {
        var (membership, request) = await PrepareFutureFreezeAsync();
        _clock.Now = _clock.Now.AddDays(1);
        await WithFreezeLifecycleDatabaseAsync(async connectionString =>
        {
            await using var services = CreateFreezeLifecycleProvider(connectionString);
            using var start = new Barrier(2);
            await Task.WhenAll(RunConcurrentLifecycleAsync(services, start), RunConcurrentLifecycleAsync(services, start));
            await using var context = FileFreezeContext(connectionString);
            var stored = await context.MemberMemberships.SingleAsync();
            Assert.Equal(MembershipStatus.Frozen, stored.MembershipStatus);
            Assert.Equal(membership.EndDate.AddDays(3), stored.EndDate);
            Assert.Equal(3, stored.TotalFrozenDays);
            Assert.Equal(FreezeRequestStatus.Active, (await context.MembershipFreezes.SingleAsync()).RequestStatus);
            Assert.Equal(2, await context.Notifications.CountAsync());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifecycleWorkerCanOverlapManagerApprovalOrEarlyUnfreezeWithoutDuplicateEffects(bool early)
    {
        MemberMembership membership;
        MembershipFreezeRequestResponse request;
        if (early)
        {
            (membership, request) = await PrepareApprovedFreezeAsync();
            _clock.Now = _clock.Now.AddDays(2);
            using var member = Client("member", "Member");
            await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        }
        else
        {
            membership = await PrepareFreezeReviewMembershipAsync();
            using var member = Client("member", "Member");
            request = await CreateFreezeRequestAsync(member, membership.Id, start: Today, days: 3);
        }

        await WithFreezeLifecycleDatabaseAsync(async connectionString =>
        {
            await using var services = CreateFreezeLifecycleProvider(connectionString);
            using var start = new Barrier(2);
            var job = RunConcurrentLifecycleAsync(services, start);
            var decision = RunLifecycleManagerDecisionAsync(connectionString, request.FreezeRequestId, early, start);
            await Task.WhenAll(job, decision);
            var decisionResult = await decision;
            Assert.True(decisionResult.IsSuccess, decisionResult.ErrorMessage);
            await using var context = FileFreezeContext(connectionString);
            var stored = await context.MemberMemberships.SingleAsync();
            Assert.Equal(membership.EndDate.AddDays(3), stored.EndDate);
            Assert.Equal(3, stored.TotalFrozenDays);
            Assert.Equal(1, stored.FreezeCount);
            Assert.Equal(early ? MembershipStatus.Active : MembershipStatus.Frozen, stored.MembershipStatus);
            var freeze = await context.MembershipFreezes.SingleAsync();
            Assert.Equal(3, freeze.AppliedExpiryExtensionDays);
            if (early)
            {
                Assert.Equal(FreezeRequestStatus.Completed, freeze.RequestStatus);
                Assert.Equal(FreezeRequestStatus.Approved, freeze.EarlyUnfreezeRequestStatus);
            }
            else
                Assert.Contains(freeze.RequestStatus, new[] { FreezeRequestStatus.Approved, FreezeRequestStatus.Active });
            Assert.InRange(await context.Notifications.CountAsync(), early ? 2 : 1, early ? 3 : 2);
            var notifications = await context.Notifications.CountAsync();
            using var followupWorker = NewFreezeLifecycleWorker(services);
            await followupWorker.RunOnceAsync(CancellationToken.None);
            var afterFollowup = await context.Notifications.CountAsync();
            Assert.InRange(afterFollowup, notifications, notifications + (early ? 0 : 1));
        });
    }

    [Fact]
    public async Task CentralGymAccessFollowsFutureFreezeActivationAndApprovedEarlyUnfreeze()
    {
        var (membership, request) = await PrepareFutureFreezeAsync(days: 7);
        await using var scope = _app.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IMembershipAccessService>();
        Assert.True((await access.CheckGymEntryAsync("member", membership.Id, CancellationToken.None)).IsSuccess);
        var foreign = await access.CheckGymEntryAsync("buyer", membership.Id, CancellationToken.None);
        Assert.Equal(404, foreign.StatusCode);

        _clock.Now = _clock.Now.AddDays(1);
        await RunFreezeLifecycleAsync();
        var frozen = await access.CheckGymEntryAsync("member", membership.Id, CancellationToken.None);
        Assert.False(frozen.IsSuccess);
        Assert.Equal(403, frozen.StatusCode);
        Assert.Equal("MSG-36", frozen.ErrorCode);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId, early: true);
        Assert.True((await access.CheckGymEntryAsync("member", membership.Id, CancellationToken.None)).IsSuccess);

        _clock.Now = _clock.Now.AddDays(30);
        var expired = await access.CheckGymEntryAsync("member", membership.Id, CancellationToken.None);
        Assert.False(expired.IsSuccess);
        Assert.Equal("MSG-36", expired.ErrorCode);
    }

    [Fact]
    public async Task CentralClassEligibilityBlocksTheFrozenYogaEntitlementAndPreservesTheSeparateGymEntitlement()
    {
        await ConfigureFreezeRequestsAsync(RequestPolicy with
        {
            EligibilityPaidDays = 0, MinimumRequestDays = 0,
            Quotas = [new(1, MembershipFreezeTierUnits.Months, 10), new(30, MembershipFreezeTierUnits.Days, 10)]
        });
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var purchase = await CreateAsync(member, Purchase([new(_combo)]));
        using var callback = await NotifyAsync(purchase, id: 140);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var memberships = await ListOwnedMembershipsAsync(member);
        var yoga = Assert.Single(memberships, item => item.ComponentType == "YOGA");
        var gym = Assert.Single(memberships, item => item.ComponentType == "GYM");
        await using var scope = _app.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IMembershipAccessService>();
        Assert.True((await access.CheckClassBookingAsync("member", yoga.MembershipId, CancellationToken.None)).IsSuccess);
        var request = await CreateFreezeRequestAsync(member, yoga.MembershipId, start: Today);
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId);
        var frozen = await access.CheckClassBookingAsync("member", yoga.MembershipId, CancellationToken.None);
        Assert.False(frozen.IsSuccess);
        Assert.Equal(403, frozen.StatusCode);
        Assert.Equal("MSG-31", frozen.ErrorCode);
        Assert.True((await access.CheckGymEntryAsync("member", gym.MembershipId, CancellationToken.None)).IsSuccess);
        var foreign = await access.CheckClassBookingAsync("buyer", yoga.MembershipId, CancellationToken.None);
        Assert.Equal(404, foreign.StatusCode);

        await ReadFreezeRequestResponseAsync(await member.PostAsync($"{FreezeRequestsRoute}/{request.FreezeRequestId}/early-unfreeze", null));
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId, early: true);
        Assert.True((await access.CheckClassBookingAsync("member", yoga.MembershipId, CancellationToken.None)).IsSuccess);
    }

    private async Task<(MemberMembership Membership, MembershipFreezeRequestResponse Request)> PrepareFutureFreezeAsync(int days = 3)
    {
        var membership = await PrepareFreezeReviewMembershipAsync();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var request = await CreateFreezeRequestAsync(member, membership.Id, start: Today.AddDays(1), days: days);
        await ApproveFreezeReviewAsync(manager, request.FreezeRequestId);
        return (membership, request);
    }

    private async Task RunFreezeLifecycleAsync()
    {
        using var worker = NewFreezeLifecycleWorker(_app.Services);
        await worker.RunOnceAsync(CancellationToken.None);
    }

    private MembershipFreezeTransitionWorker NewFreezeLifecycleWorker(IServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<MembershipFreezeTransitionWorker>.Instance, _clock);

    private async Task AssertFreezeLifecycleStateAsync(int requestId, string requestStatus, MembershipStatus status,
        DateOnly expiry, int usedDays, int notifications)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var freeze = await db.MembershipFreezes.SingleAsync(row => row.Id == requestId);
        var membership = await db.MemberMemberships.SingleAsync(row => row.Id == freeze.MemberMembershipId);
        Assert.Equal(requestStatus, freeze.RequestStatus.ToString());
        Assert.Equal(status, membership.MembershipStatus);
        Assert.Equal(expiry, membership.EndDate);
        Assert.Equal(usedDays, membership.TotalFrozenDays);
        Assert.Equal(usedDays == 0 ? 0 : 1, membership.FreezeCount);
        Assert.Equal(notifications, await db.Notifications.CountAsync());
    }

    private async Task<int> FreezeLifecycleNotificationCountAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Notifications.CountAsync();
    }

    private async Task<int> FreezeLifecycleAuditCountAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().AuditLogs.CountAsync();
    }

    private async Task WithFreezeLifecycleDatabaseAsync(Func<string, Task> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vfms-freeze-lifecycle-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 30, ForeignKeys = true
        }.ToString();
        try
        {
            await using (var target = new SqliteConnection(connectionString))
            {
                await target.OpenAsync();
                _connection.BackupDatabase(target);
            }
            await action(connectionString);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private ServiceProvider CreateFreezeLifecycleProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddScoped<VfmsDbContext>(_ => FileFreezeContext(connectionString));
        services.AddScoped<IMembershipFreezeLifecycleRepository, MembershipFreezeLifecycleRepository>();
        services.AddScoped<IMembershipFreezeLifecycleService, MembershipFreezeLifecycleService>();
        services.AddScoped<IMembershipFreezeRequestRepository, MembershipFreezeRequestRepository>();
        services.AddScoped<IMembershipFreezePolicyRepository, MembershipFreezePolicyRepository>();
        return services.BuildServiceProvider();
    }

    private static TestContext FileFreezeContext(string connectionString) =>
        new(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connectionString).Options);

    private Task RunConcurrentLifecycleAsync(IServiceProvider services, Barrier start) => Task.Run(async () =>
    {
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Lifecycle workers did not reach the start barrier.");
        using var worker = NewFreezeLifecycleWorker(services);
        await worker.RunOnceAsync(CancellationToken.None);
    });

    private Task<VFMS.Contracts.Common.Result<MembershipFreezeReviewDetailResponse>> RunLifecycleManagerDecisionAsync(
        string connectionString, int requestId, bool early, Barrier start) => Task.Run(async () =>
    {
        await using var db = FileFreezeContext(connectionString);
        var service = new MembershipFreezeReviewService(new MembershipFreezeReviewRepository(db),
            new MembershipFreezeRequestRepository(db), new MembershipFreezePolicyRepository(db),
            new VFMS.Contracts.Memberships.Validators.RejectMembershipFreezeRequestValidator(), _clock);
        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)), "Manager decision did not reach the lifecycle start barrier.");
        return await service.ApproveAsync("manager", requestId, early, null, CancellationToken.None);
    });
}
