using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipFreezePolicyTests
{
    private const string ManagerId = "manager";
    private const string ManagerIpAddress = "192.0.2.16";
    private static readonly MembershipFreezePolicyRequest ValidRequest = new(3, 20, 2, 11,
        [new(6, MembershipFreezeTierUnits.Months, 30), new(12, MembershipFreezeTierUnits.Months, 60)]);

    [Fact]
    public async Task GetReturnsAnEmptyConfigurationBeforeSetup()
    {
        var repository = new PolicyRepositoryStub();
        using var cancellation = new CancellationTokenSource();

        var result = await CreateService(repository).GetAsync(cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.Policy);
        Assert.Empty(result.Data.Quotas);
        Assert.Equal((false, cancellation.Token), repository.LastRead);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task ManagerCreatesAReadableGlobalPolicyWithAnAuditEntry()
    {
        var repository = new PolicyRepositoryStub();
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();

        var created = await service.UpdateAsync(ValidRequest, ManagerId, ManagerIpAddress, cancellation.Token);

        Assert.True(created.IsSuccess);
        Assert.Equal((true, cancellation.Token), repository.LastRead);
        Assert.Equal(cancellation.Token, repository.LastTransactionToken);
        var stored = Assert.IsType<FreezePolicy>(repository.Policy);
        Assert.True(stored.IsGlobal);
        Assert.Equal(PolicyStatus.Active, stored.PolicyStatus);
        Assert.Equal(ManagerId, stored.CreatedByUserId);
        Assert.NotEqual(0, stored.Id);
        var audit = Assert.Single(repository.Audits);
        AssertAudit(audit, stored.Id, AuditActionType.Create);
        Assert.Null(audit.OldValue);

        var read = await service.GetAsync(cancellation.Token);

        Assert.True(read.IsSuccess);
        Assert.Equal(created.Data!.Policy, read.Data!.Policy);
        var configuration = Assert.IsType<MembershipFreezePolicyConfiguration>(read.Data.Policy);
        Assert.Equal(stored.Id, configuration.PolicyId);
        Assert.Equal(3, configuration.MinimumFreezeDays);
        Assert.Equal(20, configuration.MaximumFreezeDays);
        Assert.Equal(2, configuration.MinimumRequestDays);
        Assert.Equal(11, configuration.EligibilityPaidDays);
        Assert.Equal(new MembershipFreezeTierQuotaResponse[]
        {
            new(6, MembershipFreezeTierUnits.Months, 30),
            new(12, MembershipFreezeTierUnits.Months, 60)
        }, read.Data.Quotas.OrderBy(quota => quota.Duration));
    }

    [Fact]
    public async Task ManagerUpdatesTheSamePolicyAndReplacesTierQuotas()
    {
        var policy = ExistingPolicy();
        var repository = new PolicyRepositoryStub { Policy = policy };
        var service = CreateService(repository);
        var request = new MembershipFreezePolicyRequest(1, 1, 0, 5,
            [new(6, MembershipFreezeTierUnits.Months, 0), new(6, MembershipFreezeTierUnits.Days, 2)]);

        var result = await service.UpdateAsync(request, ManagerId, ManagerIpAddress);

        Assert.True(result.IsSuccess);
        Assert.Same(policy, repository.Policy);
        var configuration = Assert.IsType<MembershipFreezePolicyConfiguration>(result.Data!.Policy);
        Assert.Equal(policy.Id, configuration.PolicyId);
        Assert.Equal(1, configuration.MinimumFreezeDays);
        Assert.Equal(1, configuration.MaximumFreezeDays);
        Assert.Equal(0, configuration.MinimumRequestDays);
        Assert.Equal(5, configuration.EligibilityPaidDays);
        Assert.Collection(policy.TierQuotas.OrderBy(quota => quota.Unit),
            quota =>
            {
                Assert.Equal(6, quota.Duration);
                Assert.Equal(FreezeTierUnit.Days, quota.Unit);
                Assert.Equal(2, quota.MaximumCumulativeFreezeDays);
            },
            quota =>
            {
                Assert.Equal(6, quota.Duration);
                Assert.Equal(FreezeTierUnit.Months, quota.Unit);
                Assert.Equal(0, quota.MaximumCumulativeFreezeDays);
            });
        Assert.Equal(5, policy.EligibilityPaidDays);
        Assert.Equal(4, policy.DefaultMaxFreezeCount);
        var audit = Assert.Single(repository.Audits);
        AssertAudit(audit, policy.Id, AuditActionType.Update);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValue));
        Assert.NotEqual(audit.OldValue, audit.NewValue);
    }

    [Fact]
    public async Task ReorderingUnchangedQuotasDoesNotSaveOrAuditAnUpdate()
    {
        var repository = new PolicyRepositoryStub { Policy = ExistingPolicy() };
        var reordered = ValidRequest with { Quotas = ValidRequest.Quotas!.Reverse().ToArray() };

        var result = await CreateService(repository).UpdateAsync(reordered, ManagerId, ManagerIpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(repository.Audits);
    }

    [Theory]
    [InlineData(null, 20, 2)]
    [InlineData(0, 20, 2)]
    [InlineData(-1, 20, 2)]
    [InlineData(3, null, 2)]
    [InlineData(3, 0, 2)]
    [InlineData(3, -1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(3, 20, null)]
    [InlineData(3, 20, -1)]
    public Task InvalidFreezeDurationAndNoticeAreRejected(int? minimum, int? maximum, int? notice) =>
        AssertRejectedAsync(ValidRequest with
        {
            MinimumFreezeDays = minimum,
            MaximumFreezeDays = maximum,
            MinimumRequestDays = notice
        });

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public Task InvalidEligibilityPaidDaysAreRejected(int? eligibilityPaidDays) =>
        AssertRejectedAsync(ValidRequest with { EligibilityPaidDays = eligibilityPaidDays });

    [Theory]
    [InlineData(null, MembershipFreezeTierUnits.Months, 30)]
    [InlineData(0, MembershipFreezeTierUnits.Months, 30)]
    [InlineData(-1, MembershipFreezeTierUnits.Months, 30)]
    [InlineData(6, null, 30)]
    [InlineData(6, "Weeks", 30)]
    [InlineData(6, MembershipFreezeTierUnits.Months, null)]
    [InlineData(6, MembershipFreezeTierUnits.Months, -1)]
    public Task InvalidTierQuotasAreRejected(int? duration, string? unit, int? quota) =>
        AssertRejectedAsync(ValidRequest with { Quotas = [new(duration, unit, quota)] });

    [Fact]
    public Task DuplicateDurationAndUnitAreRejected() => AssertRejectedAsync(ValidRequest with
    {
        Quotas = [new(6, MembershipFreezeTierUnits.Months, 30), new(6, MembershipFreezeTierUnits.Months, 40)]
    });

    private static async Task AssertRejectedAsync(MembershipFreezePolicyRequest request)
    {
        var policy = ExistingPolicy();
        var repository = new PolicyRepositoryStub { Policy = policy };

        var result = await CreateService(repository).UpdateAsync(request, ManagerId, ManagerIpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(3, policy.MinFreezeDaysPerRequest);
        Assert.Equal(20, policy.DefaultMaxFreezeDays);
        Assert.Equal(2, policy.MinimumRequestDays);
        Assert.Equal(new[] { 30, 60 }, policy.TierQuotas.OrderBy(quota => quota.Duration)
            .Select(quota => quota.MaximumCumulativeFreezeDays));
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(repository.Audits);
    }

    private static void AssertAudit(AuditLog audit, int policyId, AuditActionType action)
    {
        Assert.Equal(ManagerId, audit.ActorUserId);
        Assert.Equal(ManagerIpAddress, audit.IpAddress);
        Assert.Equal(nameof(FreezePolicy), audit.AuditEntity);
        Assert.Equal(policyId, audit.EntityId);
        Assert.Equal(action, audit.ActionType);
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValue));
    }

    private static MembershipFreezePolicyService CreateService(PolicyRepositoryStub repository) =>
        new(repository, new MembershipFreezePolicyRequestValidator());

    private static FreezePolicy ExistingPolicy() => new()
    {
        Id = 9,
        PolicyName = "Membership freeze policy",
        CreatedByUserId = "original-manager",
        MinFreezeDaysPerRequest = 3,
        DefaultMaxFreezeDays = 20,
        MinimumRequestDays = 2,
        EligibilityPaidDays = 11,
        DefaultMaxFreezeCount = 4,
        TierQuotas =
        [
            new() { Duration = 6, Unit = FreezeTierUnit.Months, MaximumCumulativeFreezeDays = 30 },
            new() { Duration = 12, Unit = FreezeTierUnit.Months, MaximumCumulativeFreezeDays = 60 }
        ]
    };

    private sealed class PolicyRepositoryStub : IMembershipFreezePolicyRepository
    {
        public FreezePolicy? Policy { get; set; }
        public List<AuditLog> Audits { get; } = [];
        public int SaveCount { get; private set; }
        public (bool ForUpdate, CancellationToken Token) LastRead { get; private set; }
        public CancellationToken LastTransactionToken { get; private set; }

        public Task<FreezePolicy?> GetActiveGlobalAsync(bool forUpdate, CancellationToken ct)
        {
            LastRead = (forUpdate, ct);
            return Task.FromResult(Policy);
        }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is FreezePolicy policy)
            {
                Assert.Null(Policy);
                Policy = policy;
            }
            else
            {
                Audits.Add(Assert.IsType<AuditLog>(entity));
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Assert.Equal(LastTransactionToken, cancellationToken);
            SaveCount++;
            if (Policy is { Id: 0 })
                Policy.Id = 9;
            return Task.CompletedTask;
        }

        public Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
        {
            LastTransactionToken = ct;
            return work();
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(
            string roleName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
