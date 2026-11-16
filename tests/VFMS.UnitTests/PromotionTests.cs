using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Promotions;
using VFMS.Contracts.Promotions.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class PromotionTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc);
    private static readonly CreatePromotionRequest Valid = new(" Campaign ", " save10 ", PromotionDiscountType.Percentage,
        10m, 100000m, 50000m, 100, Now.AddDays(-1), Now.AddDays(1), true);

    [Fact]
    public async Task ManagerCreatesPercentagePromotionWithNormalizedCodeZeroUsageAndAudit()
    {
        var repository = new RepositoryStub();
        using var cancellation = new CancellationTokenSource();
        var result = await Service(repository).CreateAsync(Valid, "manager", "192.0.2.18", cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        var voucher = Assert.Single(repository.Vouchers);
        Assert.Equal("Campaign", voucher.Name);
        Assert.Equal("SAVE10", voucher.Code);
        Assert.Equal(DiscountType.PERCENT, voucher.DiscountType);
        Assert.Equal(10m, voucher.DiscountValue);
        Assert.Equal(100000m, voucher.MinimumSpend);
        Assert.Equal(50000m, voucher.MaxDiscountAmount);
        Assert.Equal(100, voucher.MaxRedemptions);
        Assert.Null(voucher.MaxRedemptionsPerUser);
        Assert.Empty(voucher.Redemptions);
        Assert.Equal(0, result.Data!.UsedCount);
        Assert.Equal(PromotionStatus.Active, result.Data.Status);
        Assert.Equal(cancellation.Token, repository.LastToken);
        var audit = Assert.Single(repository.Audits);
        Assert.Equal(voucher.Id, audit.EntityId);
        Assert.Equal("manager", audit.ActorUserId);
        Assert.Equal("192.0.2.18", audit.IpAddress);
        Assert.Equal(nameof(Voucher), audit.AuditEntity);
        Assert.Equal(AuditActionType.Create, audit.ActionType);
        Assert.Null(audit.OldValue);
        Assert.Contains("SAVE10", audit.NewValue);
    }

    [Fact]
    public async Task ManagerCreatesFixedPromotionWithoutPercentageCapAndWithUnlimitedQuota()
    {
        var repository = new RepositoryStub();
        var request = Valid with
        {
            DiscountType = PromotionDiscountType.Fixed, DiscountValue = 500000m,
            MaxDiscountAmount = null, MinimumSpend = null, MaxRedemptions = null
        };

        var result = await Service(repository).CreateAsync(request, "manager", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(PromotionDiscountType.Fixed, result.Data!.DiscountType);
        Assert.Null(result.Data.MaxDiscountAmount);
        Assert.Null(result.Data.MinimumSpend);
        Assert.Null(result.Data.MaxRedemptions);
        Assert.Equal(DiscountType.FIXED, Assert.Single(repository.Vouchers).DiscountType);
    }

    [Theory]
    [InlineData("name-required")]
    [InlineData("name-blank")]
    [InlineData("name-length")]
    [InlineData("code-required")]
    [InlineData("code-blank")]
    [InlineData("code-length")]
    [InlineData("type-required")]
    [InlineData("type-unknown")]
    [InlineData("value-required")]
    [InlineData("value-zero")]
    [InlineData("value-negative")]
    [InlineData("value-too-precise")]
    [InlineData("percent-over-100")]
    [InlineData("minimum-negative")]
    [InlineData("minimum-too-precise")]
    [InlineData("cap-required")]
    [InlineData("cap-zero")]
    [InlineData("cap-negative")]
    [InlineData("cap-too-precise")]
    [InlineData("quota-zero")]
    [InlineData("quota-negative")]
    [InlineData("start-required")]
    [InlineData("start-default")]
    [InlineData("start-not-utc")]
    [InlineData("end-required")]
    [InlineData("end-equal")]
    [InlineData("end-before")]
    [InlineData("end-not-utc")]
    [InlineData("enabled-required")]
    public async Task InvalidConfigurationIsRejectedBeforeMutation(string scenario)
    {
        var request = scenario switch
        {
            "name-required" => Valid with { Name = null },
            "name-blank" => Valid with { Name = "  " },
            "name-length" => Valid with { Name = new string('a', 257) },
            "code-required" => Valid with { Code = null },
            "code-blank" => Valid with { Code = "  " },
            "code-length" => Valid with { Code = new string('a', 51) },
            "type-required" => Valid with { DiscountType = null },
            "type-unknown" => Valid with { DiscountType = (PromotionDiscountType)55 },
            "value-required" => Valid with { DiscountValue = null },
            "value-zero" => Valid with { DiscountValue = 0 },
            "value-negative" => Valid with { DiscountValue = -1 },
            "value-too-precise" => Valid with { DiscountValue = 0.001m },
            "percent-over-100" => Valid with { DiscountValue = 100.01m },
            "minimum-negative" => Valid with { MinimumSpend = -1 },
            "minimum-too-precise" => Valid with { MinimumSpend = 1.001m },
            "cap-required" => Valid with { MaxDiscountAmount = null },
            "cap-zero" => Valid with { MaxDiscountAmount = 0 },
            "cap-negative" => Valid with { MaxDiscountAmount = -1 },
            "cap-too-precise" => Valid with { MaxDiscountAmount = 1.001m },
            "quota-zero" => Valid with { MaxRedemptions = 0 },
            "quota-negative" => Valid with { MaxRedemptions = -1 },
            "start-required" => Valid with { StartAt = null },
            "start-default" => Valid with { StartAt = default(DateTime) },
            "start-not-utc" => Valid with { StartAt = DateTime.SpecifyKind(Valid.StartAt!.Value, DateTimeKind.Unspecified) },
            "end-required" => Valid with { EndAt = null },
            "end-equal" => Valid with { EndAt = Valid.StartAt },
            "end-before" => Valid with { EndAt = Valid.StartAt!.Value.AddSeconds(-1) },
            "end-not-utc" => Valid with { EndAt = DateTime.SpecifyKind(Valid.EndAt!.Value, DateTimeKind.Local) },
            "enabled-required" => Valid with { IsEnabled = null },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var repository = new RepositoryStub();

        var created = await Service(repository).CreateAsync(request, "manager", null);
        var updated = await Service(repository).UpdateAsync(1, ToUpdate(request), "manager", null);

        Assert.False(created.IsSuccess);
        Assert.False(updated.IsSuccess);
        Assert.Equal(400, created.StatusCode);
        Assert.Equal("PROMOTION_VALIDATION_FAILED", created.ErrorCode);
        Assert.Equal(0, repository.TransactionCount);
        Assert.Empty(repository.Vouchers);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task PercentageOneHundredAndZeroMinimumSpendAreValid()
    {
        var result = await Service(new()).CreateAsync(Valid with { DiscountValue = 100, MinimumSpend = 0 }, "manager", null);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("SAVE10")]
    [InlineData("save10")]
    [InlineData(" Save10 ")]
    public async Task DuplicateNormalizedCodeIsRejectedForCreateAndEdit(string duplicate)
    {
        var repository = new RepositoryStub { Vouchers = [Existing(), Existing(2, "OTHER")] };
        var service = Service(repository);

        var created = await service.CreateAsync(Valid with { Code = duplicate }, "manager", null);
        var updated = await service.UpdateAsync(2, ToUpdate(Valid with { Code = duplicate }), "manager", null);

        Assert.Equal("PROMOTION_CODE_EXISTS", created.ErrorCode);
        Assert.Equal(409, created.StatusCode);
        Assert.Equal("PROMOTION_CODE_EXISTS", updated.ErrorCode);
        Assert.Equal("OTHER", repository.Vouchers[1].Code);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task CompetingUniqueIndexViolationBecomesConflict()
    {
        var repository = new RepositoryStub { FailUniqueCode = true };

        var result = await Service(repository).CreateAsync(Valid, "manager", null);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("PROMOTION_CODE_EXISTS", result.ErrorCode);
        Assert.Empty(repository.Vouchers);
        Assert.Empty(repository.Audits);
    }

    [Theory]
    [InlineData("before-start", PromotionStatus.Scheduled)]
    [InlineData("at-start", PromotionStatus.Active)]
    [InlineData("within-period", PromotionStatus.Active)]
    [InlineData("at-end", PromotionStatus.Expired)]
    [InlineData("after-end", PromotionStatus.Expired)]
    [InlineData("quota-reached", PromotionStatus.Expired)]
    [InlineData("quota-exceeded", PromotionStatus.Expired)]
    [InlineData("disabled-active", PromotionStatus.Disabled)]
    [InlineData("disabled-expired", PromotionStatus.Disabled)]
    [InlineData("legacy-expired-future", PromotionStatus.Scheduled)]
    public async Task LifecycleUsesServerClockEnabledStateAndGlobalUsage(string scenario, PromotionStatus expected)
    {
        var voucher = Existing();
        var used = 0;
        switch (scenario)
        {
            case "before-start": voucher.StartAt = Now.AddSeconds(1); break;
            case "at-start": voucher.StartAt = Now; break;
            case "at-end": voucher.EndAt = Now; break;
            case "after-end": voucher.EndAt = Now.AddSeconds(-1); break;
            case "quota-reached": used = voucher.MaxRedemptions!.Value; break;
            case "quota-exceeded": used = voucher.MaxRedemptions!.Value + 1; break;
            case "disabled-active": voucher.VoucherStatus = VoucherStatus.Inactive; break;
            case "disabled-expired": voucher.VoucherStatus = VoucherStatus.Inactive; voucher.EndAt = Now.AddDays(-1); break;
            case "legacy-expired-future": voucher.VoucherStatus = VoucherStatus.Expired; voucher.StartAt = Now.AddSeconds(1); break;
        }
        var repository = new RepositoryStub { Vouchers = [voucher], UsedCount = used };
        var service = Service(repository);

        var detail = await service.GetAsync(voucher.Id);
        var list = await service.ListAsync();

        Assert.Equal(expected, detail.Data!.Status);
        Assert.Equal(expected, Assert.Single(list.Data!).Status);
        Assert.Equal(used, detail.Data.UsedCount);
        Assert.Equal(DateTimeKind.Utc, detail.Data.StartAt.Kind);
        Assert.Equal(DateTimeKind.Utc, detail.Data.EndAt.Kind);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task ManagerUpdatesSameVoucherWithoutResettingUsageOrLegacyFields()
    {
        var voucher = Existing();
        voucher.Description = "Existing campaign description";
        voucher.MaxRedemptionsPerUser = 3;
        voucher.Redemptions.Add(new() { RedemptionStatus = VoucherRedemptionStatus.Confirmed });
        var repository = new RepositoryStub { Vouchers = [voucher], UsedCount = 7 };
        var request = ToUpdate(Valid) with
        {
            Name = "Updated", Code = "newcode", DiscountType = PromotionDiscountType.Fixed,
            DiscountValue = 200000m, MaxDiscountAmount = null, MaxRedemptions = 7
        };

        var result = await Service(repository).UpdateAsync(1, request, "manager", "192.0.2.18");

        Assert.True(result.IsSuccess);
        Assert.Same(voucher, Assert.Single(repository.Vouchers));
        Assert.Equal("NEWCODE", voucher.Code);
        Assert.Equal(7, result.Data!.UsedCount);
        Assert.Equal(PromotionStatus.Expired, result.Data.Status);
        Assert.Equal("original-manager", voucher.CreatedByUserId);
        Assert.Equal(3, voucher.MaxRedemptionsPerUser);
        Assert.Equal("Existing campaign description", voucher.Description);
        Assert.Single(voucher.Redemptions);
        var audit = Assert.Single(repository.Audits);
        Assert.Equal(AuditActionType.Update, audit.ActionType);
        Assert.Contains("SAVE10", audit.OldValue);
        Assert.Contains("NEWCODE", audit.NewValue);
    }

    [Fact]
    public async Task UsageLimitCannotDropBelowConfirmedCount()
    {
        var voucher = Existing();
        var repository = new RepositoryStub { Vouchers = [voucher], UsedCount = 8 };

        var result = await Service(repository).UpdateAsync(1, ToUpdate(Valid) with { MaxRedemptions = 7 }, "manager", null);

        Assert.False(result.IsSuccess);
        Assert.Equal("PROMOTION_USAGE_LIMIT_TOO_LOW", result.ErrorCode);
        Assert.Equal(100, voucher.MaxRedemptions);
        Assert.Empty(repository.Audits);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task DisableAndEnablePreserveUsageHistoryAndAuditAvailability()
    {
        var voucher = Existing();
        voucher.Redemptions.Add(new() { RedemptionStatus = VoucherRedemptionStatus.Confirmed });
        var repository = new RepositoryStub { Vouchers = [voucher], UsedCount = 10 };
        var service = Service(repository);

        var disabled = await service.SetEnabledAsync(1, new(false), "manager", null);
        Assert.Equal(PromotionStatus.Disabled, disabled.Data!.Status);
        Assert.False(disabled.Data.IsEnabled);
        var enabled = await service.SetEnabledAsync(1, new(true), "manager", null);

        Assert.Equal(PromotionStatus.Active, enabled.Data!.Status);
        Assert.True(enabled.Data.IsEnabled);
        Assert.Equal(10, enabled.Data.UsedCount);
        Assert.Single(voucher.Redemptions);
        Assert.False(voucher.IsDeleted);
        Assert.Equal(2, repository.Audits.Count);
        Assert.All(repository.Audits, audit => Assert.Equal(AuditActionType.Update, audit.ActionType));
        Assert.Contains("\"IsEnabled\":true", repository.Audits[0].OldValue);
        Assert.Contains("\"IsEnabled\":false", repository.Audits[0].NewValue);
        Assert.Contains("\"IsEnabled\":true", repository.Audits[1].NewValue);
    }

    [Fact]
    public async Task NoOpEditNormalizesDatabaseDateKindsAndDoesNotAudit()
    {
        var voucher = Existing();
        voucher.StartAt = DateTime.SpecifyKind(voucher.StartAt, DateTimeKind.Unspecified);
        voucher.EndAt = DateTime.SpecifyKind(voucher.EndAt, DateTimeKind.Unspecified);
        voucher.VoucherStatus = VoucherStatus.Expired;
        var repository = new RepositoryStub { Vouchers = [voucher] };
        var service = Service(repository);

        var update = await service.UpdateAsync(1, ToUpdate(Valid), "manager", null);
        var enable = await service.SetEnabledAsync(1, new(true), "manager", null);

        Assert.True(update.IsSuccess);
        Assert.True(enable.IsSuccess);
        Assert.Equal(VoucherStatus.Expired, voucher.VoucherStatus);
        Assert.Empty(repository.Audits);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task NoOpEditComparesMoneyByValueAcrossSqlDecimalScaleDifferences()
    {
        var voucher = Existing();
        voucher.DiscountValue = 10.00m;
        voucher.MinimumSpend = 100000.00m;
        voucher.MaxDiscountAmount = 50000.00m;
        var repository = new RepositoryStub { Vouchers = [voucher] };

        var result = await Service(repository).UpdateAsync(1, ToUpdate(Valid), "manager", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task EnableCannotMakeExhaustedOrEndedPromotionActive()
    {
        var voucher = Existing();
        voucher.VoucherStatus = VoucherStatus.Inactive;
        var repository = new RepositoryStub { Vouchers = [voucher], UsedCount = 100 };

        var result = await Service(repository).SetEnabledAsync(1, new(true), "manager", null);

        Assert.Equal(PromotionStatus.Expired, result.Data!.Status);
        Assert.True(result.Data.IsEnabled);
    }

    [Fact]
    public async Task MissingEnabledInputAndMissingVoucherFailWithoutAudit()
    {
        var repository = new RepositoryStub();
        var service = Service(repository);
        Assert.False((await service.SetEnabledAsync(1, new(null), "manager", null)).IsSuccess);
        Assert.Equal(404, (await service.GetAsync(1)).StatusCode);
        Assert.Equal(404, (await service.UpdateAsync(1, ToUpdate(Valid), "manager", null)).StatusCode);
        Assert.Equal(404, (await service.SetEnabledAsync(1, new(false), "manager", null)).StatusCode);
        Assert.Empty(repository.Audits);
    }

    [Fact]
    public async Task AuditFailureRollsBackCreateThroughExistingTransactionPattern()
    {
        var repository = new RepositoryStub { FailAudit = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(repository).CreateAsync(Valid, "manager", null));

        Assert.Empty(repository.Vouchers);
        Assert.Empty(repository.Audits);
    }

    private static PromotionService Service(RepositoryStub repository) => new(repository,
        new CreatePromotionRequestValidator(), new UpdatePromotionRequestValidator(),
        new SetPromotionEnabledRequestValidator(), new Clock());

    private static UpdatePromotionRequest ToUpdate(CreatePromotionRequest request) =>
        new(request.Name, request.Code, request.DiscountType, request.DiscountValue, request.MinimumSpend,
            request.MaxDiscountAmount, request.MaxRedemptions, request.StartAt, request.EndAt, request.IsEnabled);

    private static Voucher Existing(int id = 1, string code = "SAVE10") => new()
    {
        Id = id, Name = "Campaign", Code = code, DiscountType = DiscountType.PERCENT,
        DiscountValue = 10, MinimumSpend = 100000, MaxDiscountAmount = 50000, MaxRedemptions = 100,
        StartAt = Now.AddDays(-1), EndAt = Now.AddDays(1), VoucherStatus = VoucherStatus.Active,
        CreatedByUserId = "original-manager"
    };

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class RepositoryStub : IPromotionRepository
    {
        public List<Voucher> Vouchers { get; init; } = [];
        public List<AuditLog> Audits { get; } = [];
        public int UsedCount { get; init; }
        public bool FailAudit { get; init; }
        public bool FailUniqueCode { get; init; }
        public int SaveCount { get; private set; }
        public int TransactionCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<IReadOnlyList<PromotionReadModel>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PromotionReadModel>>(Vouchers.Select(Read).ToArray());
        public Task<PromotionReadModel?> GetAsync(int id, CancellationToken ct) =>
            Task.FromResult(Vouchers.SingleOrDefault(v => v.Id == id) is { } voucher ? Read(voucher) : null);
        public Task<Voucher?> GetForUpdateAsync(int id, CancellationToken ct) =>
            Task.FromResult(Vouchers.SingleOrDefault(v => v.Id == id));
        public Task<int> GetUsedCountAsync(int id, CancellationToken ct) => Task.FromResult(UsedCount);
        public Task<int> GetCommittedCapacityAsync(int id, CancellationToken ct) => Task.FromResult(UsedCount);
        public Task<bool> CodeExistsAsync(string normalizedCode, int? excludingId, CancellationToken ct) =>
            Task.FromResult(Vouchers.Any(v => v.Id != excludingId &&
                string.Equals(v.Code.Trim(), normalizedCode, StringComparison.OrdinalIgnoreCase)));

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity is Voucher voucher)
                Vouchers.Add(voucher);
            else
                Audits.Add(Assert.IsType<AuditLog>(entity));
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            LastToken = ct;
            SaveCount++;
            if (FailUniqueCode)
                throw new PromotionCodeConflictException(new InvalidOperationException("Unique code"));
            if (FailAudit && Audits.Count > 0)
                throw new InvalidOperationException("Audit failed.");
            foreach (var voucher in Vouchers.Where(v => v.Id == 0))
                voucher.Id = Vouchers.Max(v => v.Id) + 1;
            return Task.CompletedTask;
        }

        public async Task ExecuteInSerializableTransactionAsync(Func<Task> work, CancellationToken ct)
        {
            TransactionCount++;
            LastToken = ct;
            var vouchers = Vouchers.Count;
            var audits = Audits.Count;
            try { await work(); }
            catch
            {
                Vouchers.RemoveRange(vouchers, Vouchers.Count - vouchers);
                Audits.RemoveRange(audits, Audits.Count - audits);
                throw;
            }
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default) => work();
        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string role, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private PromotionReadModel Read(Voucher voucher) => new(voucher.Id, voucher.Name, voucher.Code,
            voucher.DiscountType, voucher.DiscountValue, voucher.MinimumSpend, voucher.MaxDiscountAmount,
            voucher.MaxRedemptions, UsedCount, voucher.StartAt, voucher.EndAt, voucher.VoucherStatus);
    }
}
