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
    private const string FreezePolicyRoute = "/api/manager/membership-freeze-policy";

    private static readonly MembershipFreezePolicyRequest InitialFreezePolicy = new(7, 60, 1, 14,
        [new(6, MembershipFreezeTierUnits.Days, 0), new(6, MembershipFreezeTierUnits.Months, 30),
         new(12, MembershipFreezeTierUnits.Months, 60)]);

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData(" ", "Manager", HttpStatusCode.Unauthorized)]
    [InlineData("member", "Member", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "Customer", HttpStatusCode.Forbidden)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Admin", HttpStatusCode.Forbidden)]
    public async Task FreezePolicyReadAndUpdateRequireAnAuthenticatedManager(
        string? user, string? role, HttpStatusCode expected)
    {
        using var client = Client(user, role);
        using var read = await client.GetAsync(FreezePolicyRoute);
        using var update = await client.PutAsJsonAsync(FreezePolicyRoute, InitialFreezePolicy);
        Assert.Equal(expected, read.StatusCode);
        Assert.Equal(expected, update.StatusCode);
    }

    [Fact]
    public async Task ManagerCreatesAndUpdatesTheSinglePolicyAndAuditsBothChanges()
    {
        using var manager = Client("manager", "Manager");
        var unconfigured = await ReadFreezePolicyAsync(manager);
        Assert.Null(unconfigured.Policy);
        Assert.Empty(unconfigured.Quotas);

        var startedAt = DateTime.UtcNow;
        var created = await SaveFreezePolicyAsync(manager, InitialFreezePolicy);
        var policy = Assert.IsType<MembershipFreezePolicyConfiguration>(created.Policy);
        Assert.True(policy.PolicyId > 0);
        Assert.Equal(7, policy.MinimumFreezeDays);
        Assert.Equal(60, policy.MaximumFreezeDays);
        Assert.Equal(1, policy.MinimumRequestDays);
        Assert.Equal(14, policy.EligibilityPaidDays);
        Assert.Equal(3, created.Quotas.Count);
        Assert.Contains(new MembershipFreezeTierQuotaResponse(6, MembershipFreezeTierUnits.Days, 0), created.Quotas);
        Assert.Contains(new MembershipFreezeTierQuotaResponse(6, MembershipFreezeTierUnits.Months, 30), created.Quotas);
        Assert.Contains(new MembershipFreezeTierQuotaResponse(12, MembershipFreezeTierUnits.Months, 60), created.Quotas);
        await AssertStoredFreezePolicyAsync(manager, created);

        var request = new MembershipFreezePolicyRequest(8, 45, 0, 30,
            [new(12, MembershipFreezeTierUnits.Months, 75)]);
        var updated = await SaveFreezePolicyAsync(manager, request);
        var revised = Assert.IsType<MembershipFreezePolicyConfiguration>(updated.Policy);
        Assert.Equal(policy.PolicyId, revised.PolicyId);
        Assert.Equal(8, revised.MinimumFreezeDays);
        Assert.Equal(45, revised.MaximumFreezeDays);
        Assert.Equal(0, revised.MinimumRequestDays);
        Assert.Equal(30, revised.EligibilityPaidDays);
        Assert.Equal(new MembershipFreezeTierQuotaResponse(12, MembershipFreezeTierUnits.Months, 75),
            Assert.Single(updated.Quotas));
        await AssertStoredFreezePolicyAsync(manager, updated);

        // Replaying identical configuration does not change the version or add an audit entry.
        var unchanged = await SaveFreezePolicyAsync(manager, request);
        Assert.Equal(updated.Policy, unchanged.Policy);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(policy.PolicyId, (await db.FreezePolicies.IgnoreQueryFilters().SingleAsync()).Id);
        var audits = await db.AuditLogs.Where(log => log.AuditEntity == nameof(FreezePolicy))
            .OrderBy(log => log.Id).ToListAsync();
        Assert.Collection(audits,
            entry =>
            {
                Assert.Equal(AuditActionType.Create, entry.ActionType);
                Assert.Null(entry.OldValue);
                AssertFreezePolicySnapshot(entry.NewValue, 7, 60, 1, 14, 3);
            },
            entry =>
            {
                Assert.Equal(AuditActionType.Update, entry.ActionType);
                Assert.Equal(audits[0].NewValue, entry.OldValue);
                AssertFreezePolicySnapshot(entry.NewValue, 8, 45, 0, 30, 1);
                var quotas = JsonSerializer.Deserialize<JsonElement>(entry.NewValue!).GetProperty("Quotas");
                Assert.Equal(75, Assert.Single(quotas.EnumerateArray()).GetProperty("MaximumCumulativeFreezeDays").GetInt32());
            });
        Assert.All(audits, entry =>
        {
            Assert.Equal("manager", entry.ActorUserId);
            Assert.Equal(policy.PolicyId, entry.EntityId);
            Assert.InRange(entry.OccurredAt, startedAt, DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task ManagerCompletesTheLegacyGlobalPolicyWithoutReplacingItsOtherSettings()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var legacy = new FreezePolicy
        {
            PolicyName = "Legacy freeze defaults",
            CreatedByUserId = "manager",
            EligibilityPaidDays = 14,
            DefaultMaxFreezeCount = 3,
            DefaultMaxFreezeDays = 20,
            MinimumRequestDays = 2
        };
        db.FreezePolicies.Add(legacy);
        await db.SaveChangesAsync();

        using var manager = Client("manager", "Manager");
        Assert.Null((await ReadFreezePolicyAsync(manager)).Policy);
        var configured = await SaveFreezePolicyAsync(manager, new(7, 7, 0, 14, []));
        Assert.Equal(legacy.Id, configured.Policy?.PolicyId);
        Assert.Equal(7, configured.Policy?.MinimumFreezeDays);
        Assert.Equal(7, configured.Policy?.MaximumFreezeDays);
        Assert.Equal(14, configured.Policy?.EligibilityPaidDays);

        var stored = await db.FreezePolicies.AsNoTracking().SingleAsync();
        Assert.Equal("Legacy freeze defaults", stored.PolicyName);
        Assert.Equal(14, stored.EligibilityPaidDays);
        Assert.Equal(3, stored.DefaultMaxFreezeCount);
        Assert.Equal(AuditActionType.Update, (await db.AuditLogs.SingleAsync()).ActionType);
    }

    [Fact]
    public async Task InvalidFreezePolicyValuesReturnValidationErrorsWithoutChangingPolicyOrAudit()
    {
        using var manager = Client("manager", "Manager");
        var configured = await SaveFreezePolicyAsync(manager, InitialFreezePolicy);
        MembershipFreezePolicyRequest[] invalidRequests =
        [
            InitialFreezePolicy with { MinimumFreezeDays = null },
            InitialFreezePolicy with { MinimumFreezeDays = 0 },
            InitialFreezePolicy with { MaximumFreezeDays = 0 },
            InitialFreezePolicy with { MaximumFreezeDays = 6 },
            InitialFreezePolicy with { MinimumRequestDays = -1 },
            InitialFreezePolicy with { EligibilityPaidDays = null },
            InitialFreezePolicy with { EligibilityPaidDays = -1 },
            InitialFreezePolicy with { Quotas = null },
            InitialFreezePolicy with { Quotas = [new(0, MembershipFreezeTierUnits.Months, 30)] },
            InitialFreezePolicy with { Quotas = [new(6, "Weeks", 30)] },
            InitialFreezePolicy with { Quotas = [new(6, MembershipFreezeTierUnits.Months, -1)] },
            InitialFreezePolicy with
            {
                Quotas = [new(6, MembershipFreezeTierUnits.Months, 30), new(6, MembershipFreezeTierUnits.Months, 60)]
            }
        ];

        foreach (var request in invalidRequests)
        {
            using var response = await manager.PutAsJsonAsync(FreezePolicyRoute, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
        }

        await AssertStoredFreezePolicyAsync(manager, configured);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdatingFreezePolicyPreservesPurchasedMembershipAndExistingFreezeRequests()
    {
        var membership = await SeedMembershipAsync(Today.AddDays(25), MembershipStatus.Active);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.MembershipFreezes.AddRange(
                new MembershipFreeze
                {
                    MemberMembershipId = membership.Id,
                    RequestedByUserId = "member",
                    RequestedStartDate = Today.AddDays(2),
                    RequestedEndDate = Today.AddDays(8),
                    RequestStatus = FreezeRequestStatus.Pending
                },
                new MembershipFreeze
                {
                    MemberMembershipId = membership.Id,
                    RequestedByUserId = "member",
                    RequestedStartDate = Today.AddDays(10),
                    RequestedEndDate = Today.AddDays(16),
                    RequestStatus = FreezeRequestStatus.Approved,
                    ReviewedByUserId = "manager"
                });
            await db.SaveChangesAsync();
        }

        using var member = Client("member", "Member");
        var before = await ReadMembershipDetailAsync(member, membership.Id);
        Assert.Equal(2, before.Freeze.History.Count);

        using var manager = Client("manager", "Manager");
        await SaveFreezePolicyAsync(manager, InitialFreezePolicy);
        var updated = await SaveFreezePolicyAsync(manager, new(10, 20, 3, 14, []));
        Assert.Empty(updated.Quotas);

        var after = await ReadMembershipDetailAsync(member, membership.Id);
        Assert.Equal(before.Membership, after.Membership);
        Assert.Equal(before.PurchasedAt, after.PurchasedAt);
        Assert.Equal(before.InvoiceNumber, after.InvoiceNumber);
        Assert.Equal(before.Freeze.TotalFrozenDays, after.Freeze.TotalFrozenDays);
        Assert.Equal(before.Freeze.FreezeCount, after.Freeze.FreezeCount);
        Assert.Equal(before.Freeze.History.ToArray(), after.Freeze.History.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFreezePolicyAuditRollsBackThePolicyAndItsQuotas(bool hasPolicy)
    {
        using var manager = Client("manager", "Manager");
        var original = hasPolicy ? await SaveFreezePolicyAsync(manager, InitialFreezePolicy) : await ReadFreezePolicyAsync(manager);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_freeze_policy_audit BEFORE INSERT ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'test freeze policy audit failure');
            END;
            """);

        using var failed = await manager.PutAsJsonAsync(FreezePolicyRoute, new MembershipFreezePolicyRequest(9, 30, 0, 14,
            [new(12, MembershipFreezeTierUnits.Months, 10)]));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertStoredFreezePolicyAsync(manager, original);
        Assert.Equal(hasPolicy ? 1 : 0, await db.FreezePolicies.CountAsync());
        Assert.Equal(hasPolicy ? 1 : 0, await db.AuditLogs.CountAsync());
    }

    private static async Task<MembershipFreezePolicyResponse> ReadFreezePolicyAsync(HttpClient manager) =>
        (await manager.GetFromJsonAsync<MembershipFreezePolicyResponse>(FreezePolicyRoute))!;

    private static async Task<MembershipFreezePolicyResponse> SaveFreezePolicyAsync(
        HttpClient manager, MembershipFreezePolicyRequest request)
    {
        using var response = await manager.PutAsJsonAsync(FreezePolicyRoute, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK, got {response.StatusCode}. {body}");
        return (await response.Content.ReadFromJsonAsync<MembershipFreezePolicyResponse>())!;
    }

    private static async Task AssertStoredFreezePolicyAsync(HttpClient manager, MembershipFreezePolicyResponse expected)
    {
        var stored = await ReadFreezePolicyAsync(manager);
        Assert.Equal(expected.Policy, stored.Policy);
        Assert.Equal(expected.Quotas.ToArray(), stored.Quotas.ToArray());
    }

    private static void AssertFreezePolicySnapshot(string? snapshot, int minimum, int maximum, int notice, int eligibility, int tierCount)
    {
        Assert.NotNull(snapshot);
        var json = JsonSerializer.Deserialize<JsonElement>(snapshot);
        Assert.Equal(minimum, json.GetProperty("MinimumFreezeDays").GetInt32());
        Assert.Equal(maximum, json.GetProperty("MaximumFreezeDays").GetInt32());
        Assert.Equal(notice, json.GetProperty("MinimumRequestDays").GetInt32());
        Assert.Equal(eligibility, json.GetProperty("EligibilityPaidDays").GetInt32());
        Assert.Equal(tierCount, json.GetProperty("Quotas").GetArrayLength());
    }
}
