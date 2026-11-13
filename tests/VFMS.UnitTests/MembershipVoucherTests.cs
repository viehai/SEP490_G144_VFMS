using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipVoucherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DiscountType.FIXED, "100000", null, "100000", "400000")]
    [InlineData(DiscountType.FIXED, "600000", null, "500000", "0")]
    [InlineData(DiscountType.FIXED, "100000.99", null, "100000", "400000")]
    [InlineData(DiscountType.PERCENT, "10", "100000", "50000", "450000")]
    [InlineData(DiscountType.PERCENT, "50", "100000", "100000", "400000")]
    [InlineData(DiscountType.PERCENT, "33.33", "999999", "166650", "333350")]
    [InlineData(DiscountType.PERCENT, "33.33", "99999.99", "99999", "400001")]
    [InlineData(DiscountType.PERCENT, "0.01", "999999", "50", "499950")]
    [InlineData(DiscountType.PERCENT, "100", "999999", "500000", "0")]
    public async Task CalculatesCappedWholeVndDiscountFromServerSnapshots(
        DiscountType type, string value, string? cap, string expectedDiscount, string expectedDue)
    {
        var fixture = new Fixture();
        fixture.Voucher.DiscountType = type;
        fixture.Voucher.DiscountValue = Money(value);
        fixture.Voucher.MaxDiscountAmount = cap is null ? null : Money(cap);
        fixture.Voucher.MinimumSpend = 500000;
        fixture.Order.Items.Single().Package.Price = 900000;

        var result = await fixture.Apply("  save20  ");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(500000, result.Data!.Subtotal);
        Assert.Equal(Money(expectedDiscount), result.Data.DiscountAmount);
        Assert.Equal(Money(expectedDue), result.Data.AmountDue);
        Assert.Equal(Money(expectedDiscount), fixture.Order.VoucherRedemption!.DiscountAmount);
        Assert.Equal(VoucherRedemptionStatus.Reserved, fixture.Order.VoucherRedemption.RedemptionStatus);
        Assert.Equal("SAVE20", result.Data.AppliedVoucher!.Code);
        Assert.Equal("Campaign", result.Data.AppliedVoucher.Name);
        Assert.Equal("Reserved", result.Data.AppliedVoucher.RedemptionStatus);
        Assert.Equal("Pending", result.Data.PaymentStatus);
        Assert.Empty(fixture.Order.Items.Single().MemberMemberships);
        Assert.Empty(fixture.Repository.Audits);
        Assert.Null(fixture.Order.PaymentTransaction!.Invoice);
        if (result.Data.AmountDue == 0) Assert.Null(result.Data.VietQr);
        else Assert.Equal(result.Data.AmountDue, fixture.Gateway.LastAmount);
    }

    [Theory]
    [InlineData("missing", "VOUCHER_NOT_FOUND")]
    [InlineData("disabled", "VOUCHER_DISABLED")]
    [InlineData("scheduled", "VOUCHER_SCHEDULED")]
    [InlineData("expired", "VOUCHER_EXPIRED")]
    [InlineData("end-boundary", "VOUCHER_EXPIRED")]
    [InlineData("confirmed-quota", "VOUCHER_QUOTA_EXHAUSTED")]
    [InlineData("reserved-quota", "VOUCHER_CAPACITY_UNAVAILABLE")]
    [InlineData("minimum-spend", "MSG-22")]
    [InlineData("invalid-cap", "VOUCHER_CONFIGURATION_INVALID")]
    [InlineData("invalid-type", "VOUCHER_CONFIGURATION_INVALID")]
    [InlineData("paid", "ORDER_NOT_PENDING")]
    [InlineData("expired-order", "ORDER_NOT_PENDING")]
    [InlineData("failed-payment", "ORDER_NOT_PENDING")]
    [InlineData("empty-order", "ORDER_EMPTY")]
    [InlineData("wrong-owner", "MEMBERSHIP_ORDER_NOT_FOUND")]
    [InlineData("ineligible", "PURCHASER_NOT_ELIGIBLE")]
    [InlineData("unavailable-payment", "PAYMENT_UNAVAILABLE")]
    public async Task RejectsInvalidApplicationWithoutFinancialOrReservationMutation(string scenario, string code)
    {
        var fixture = new Fixture();
        switch (scenario)
        {
            case "missing": fixture.Repository.Vouchers.Clear(); break;
            case "disabled": fixture.Voucher.VoucherStatus = VoucherStatus.Inactive; break;
            case "scheduled": fixture.Voucher.StartAt = Now.AddSeconds(1).UtcDateTime; break;
            case "expired": fixture.Voucher.EndAt = Now.AddSeconds(-1).UtcDateTime; break;
            case "end-boundary": fixture.Voucher.EndAt = Now.UtcDateTime; break;
            case "confirmed-quota": fixture.Repository.Confirmed = 10; break;
            case "reserved-quota": fixture.Repository.Reserved = 10; break;
            case "minimum-spend": fixture.Voucher.MinimumSpend = 500001; break;
            case "invalid-cap": fixture.Voucher.MaxDiscountAmount = null; break;
            case "invalid-type": fixture.Voucher.DiscountType = (DiscountType)99; break;
            case "paid": fixture.Order.OrderStatus = OrderStatus.Paid; break;
            case "expired-order": fixture.Clock.Value = Now.AddMinutes(15); break;
            case "failed-payment": fixture.Order.PaymentTransaction!.PaymentStatus = PaymentStatus.Failed; break;
            case "empty-order": fixture.Order.Items.Clear(); break;
            case "wrong-owner": fixture.Order.PurchaserUserId = "someone-else"; break;
            case "ineligible": fixture.Buyer.AccountStatus = AccountStatus.Locked; break;
            case "unavailable-payment": fixture.Gateway.Available = false; break;
        }

        var result = await fixture.Apply();

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(500000, fixture.Order.PaymentTransaction!.AmountDue);
        Assert.Null(fixture.Order.VoucherRedemption);
        Assert.Equal(0, fixture.Repository.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task InvalidCodeShapeIsRejectedBeforeDatabaseTransaction(string? code)
    {
        var fixture = new Fixture();
        var result = await fixture.Apply(code);
        Assert.False(result.IsSuccess);
        Assert.Equal("VOUCHER_CODE_REQUIRED", result.ErrorCode);
        Assert.Equal(0, fixture.Repository.Transactions);
    }

    [Fact]
    public async Task SameCodeRetryKeepsOneReservationAndOriginalFinancialDisplaySnapshotAfterManagerEdit()
    {
        var fixture = new Fixture();
        var first = await fixture.Apply();
        var reservation = fixture.Order.VoucherRedemption;
        fixture.Voucher.Code = "RENAMED";
        fixture.Voucher.Name = "Edited campaign";
        fixture.Voucher.DiscountValue = 1;
        fixture.Voucher.VoucherStatus = VoucherStatus.Inactive;
        fixture.Repository.Confirmed = 10;

        var retry = await fixture.Apply("save20");

        Assert.True(retry.IsSuccess);
        Assert.Same(reservation, fixture.Order.VoucherRedemption);
        Assert.Equal(first.Data!.AmountDue, retry.Data!.AmountDue);
        Assert.Equal(first.Data.AppliedVoucher, retry.Data.AppliedVoucher);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task DifferentVoucherCannotStackAndRemovalAllowsReusingSameRedemptionForReplacement()
    {
        var fixture = new Fixture();
        await fixture.Apply();
        var reservation = fixture.Order.VoucherRedemption;
        var second = new Voucher
        {
            Id = 2, Code = "SECOND", Name = "Second campaign", DiscountType = DiscountType.FIXED,
            DiscountValue = 75000, StartAt = Now.AddDays(-1).UtcDateTime, EndAt = Now.AddDays(1).UtcDateTime
        };
        fixture.Repository.Vouchers.Add(second);
        var stacked = await fixture.Apply("SECOND");
        Assert.Equal("VOUCHER_ALREADY_APPLIED", stacked.ErrorCode);
        var removed = await fixture.Service.RemoveVoucherAsync("buyer", fixture.Order.OrderNumber, default);
        Assert.Equal(500000, removed.Data!.AmountDue);
        Assert.Equal(0, removed.Data.DiscountAmount);
        Assert.Null(removed.Data.AppliedVoucher);
        Assert.Equal(VoucherRedemptionStatus.Released, reservation!.RedemptionStatus);

        var replaced = await fixture.Apply("second");

        Assert.True(replaced.IsSuccess);
        Assert.Same(reservation, fixture.Order.VoucherRedemption);
        Assert.Equal(2, reservation.VoucherId);
        Assert.Equal(VoucherRedemptionStatus.Reserved, reservation.RedemptionStatus);
        Assert.Equal(425000, replaced.Data!.AmountDue);
    }

    [Fact]
    public async Task PaymentUsesDiscountedSnapshotAndDuplicateWebhookConfirmsExactlyOnce()
    {
        var fixture = new Fixture();
        var applied = await fixture.Apply();
        fixture.Voucher.DiscountValue = 5;
        fixture.Voucher.MaxDiscountAmount = 1;
        fixture.Gateway.Transfer = new(fixture.Order.OrderNumber, "payment-1", applied.Data!.AmountDue, Now);
        var first = await fixture.Service.ConfirmSepayAsync("", "", "", [], default);
        var duplicate = await fixture.Service.ConfirmSepayAsync("", "", "", [], default);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(VoucherRedemptionStatus.Confirmed, fixture.Order.VoucherRedemption!.RedemptionStatus);
        Assert.Equal(100000, fixture.Order.VoucherRedemption.DiscountAmount);
        Assert.Equal(400000, fixture.Order.PaymentTransaction!.AmountDue);
        Assert.Equal(400000, fixture.Order.PaymentTransaction.AmountReceived);
        Assert.Single(fixture.Order.Items.Single().MemberMemberships);
        Assert.NotNull(fixture.Order.PaymentTransaction.Invoice);
        Assert.Equal(2, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task FullPriceTransferCannotSettleDiscountedOrder()
    {
        var fixture = new Fixture();
        await fixture.Apply();
        fixture.Gateway.Transfer = new(fixture.Order.OrderNumber, "payment-1", 500000, Now);

        var result = await fixture.Service.ConfirmSepayAsync("", "", "", [], default);

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderStatus.Pending, fixture.Order.OrderStatus);
        Assert.Equal(VoucherRedemptionStatus.Reserved, fixture.Order.VoucherRedemption!.RedemptionStatus);
    }

    [Theory]
    [InlineData(PaymentMethod.VietQR)]
    [InlineData(PaymentMethod.Cash)]
    public async Task FullPricePaymentAfterRemovalPreservesReleasedHistoryWithoutConsumingQuota(PaymentMethod method)
    {
        var fixture = new Fixture(method);
        await fixture.Apply();
        await fixture.Service.RemoveVoucherAsync("buyer", fixture.Order.OrderNumber, default);
        if (method == PaymentMethod.VietQR)
        {
            fixture.Gateway.Transfer = new(fixture.Order.OrderNumber, "payment-1", 500000, Now);
            Assert.True((await fixture.Service.ConfirmSepayAsync("", "", "", [], default)).IsSuccess);
        }
        else
        {
            Assert.True((await fixture.Service.ConfirmCashAsync("staff", fixture.Order.OrderNumber, 500000, null, default)).IsSuccess);
        }
        Assert.Equal(OrderStatus.Paid, fixture.Order.OrderStatus);
        Assert.Equal(500000, fixture.Order.PaymentTransaction!.AmountReceived);
        Assert.Equal(VoucherRedemptionStatus.Released, fixture.Order.VoucherRedemption!.RedemptionStatus);
        var summary = await fixture.Service.GetAsync("buyer", fixture.Order.OrderNumber, default);
        Assert.Equal(0, summary.Data!.DiscountAmount);
        Assert.Null(summary.Data.AppliedVoucher);
    }

    [Fact]
    public async Task CashConfirmationConfirmsReservationOnlyAtActualPayment()
    {
        var fixture = new Fixture(PaymentMethod.Cash);
        await fixture.Apply();
        Assert.Equal(VoucherRedemptionStatus.Reserved, fixture.Order.VoucherRedemption!.RedemptionStatus);

        var paid = await fixture.Service.ConfirmCashAsync("staff", fixture.Order.OrderNumber, 400000, "192.0.2.19", default);

        Assert.True(paid.IsSuccess);
        Assert.Equal("Confirmed", paid.Data!.AppliedVoucher!.RedemptionStatus);
        Assert.Single(fixture.Repository.Audits);
        Assert.Single(fixture.Order.Items.Single().MemberMemberships);
    }

    [Fact]
    public async Task TimeoutReleasesOnceAndLateCallbackCannotConsumeReleasedCapacity()
    {
        var fixture = new Fixture();
        await fixture.Apply();
        fixture.Clock.Value = Now.AddMinutes(15);
        await fixture.Service.ExpireOrdersAsync(default);
        await fixture.Service.ExpireOrdersAsync(default);
        fixture.Gateway.Transfer = new(fixture.Order.OrderNumber, "late-payment", 400000, Now.AddMinutes(1));

        var callback = await fixture.Service.ConfirmSepayAsync("", "", "", [], default);

        Assert.False(callback.IsSuccess);
        Assert.Equal(OrderStatus.Expired, fixture.Order.OrderStatus);
        Assert.Equal(PaymentStatus.Cancelled, fixture.Order.PaymentTransaction!.PaymentStatus);
        Assert.Equal(VoucherRedemptionStatus.Released, fixture.Order.VoucherRedemption!.RedemptionStatus);
        Assert.Empty(fixture.Order.Items.Single().MemberMemberships);
    }

    [Fact]
    public async Task FreeOrderRequiresExplicitOwnerCompletionAndHasNoFabricatedTransfer()
    {
        var fixture = new Fixture();
        fixture.Voucher.DiscountType = DiscountType.FIXED;
        fixture.Voucher.DiscountValue = 600000;
        var applied = await fixture.Apply();
        Assert.Equal(0, applied.Data!.AmountDue);
        Assert.Equal("Pending", applied.Data.OrderStatus);
        Assert.Equal("Reserved", applied.Data.AppliedVoucher!.RedemptionStatus);
        Assert.Null(applied.Data.VietQr);
        Assert.Null((await fixture.Service.GetAsync("buyer", fixture.Order.OrderNumber, default)).Data!.VietQr);

        var completed = await fixture.Service.CompleteNoChargeAsync("buyer", fixture.Order.OrderNumber, default);
        var duplicate = await fixture.Service.CompleteNoChargeAsync("buyer", fixture.Order.OrderNumber, default);

        Assert.True(completed.IsSuccess);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal("NoCharge", completed.Data!.PaymentMethod);
        Assert.Equal("Confirmed", completed.Data.AppliedVoucher!.RedemptionStatus);
        Assert.Equal(0, fixture.Order.PaymentTransaction!.AmountReceived);
        Assert.Equal("NoCharge", fixture.Order.PaymentTransaction.GatewayName);
        Assert.Null(fixture.Order.PaymentTransaction.GatewayTransactionReference);
        Assert.Single(fixture.Order.Items.Single().MemberMemberships);
        Assert.NotNull(fixture.Order.PaymentTransaction.Invoice);
    }

    [Fact]
    public async Task NoChargeCannotCompleteAnOrderWithPositiveAmountDue()
    {
        var fixture = new Fixture();
        await fixture.Apply();
        var result = await fixture.Service.CompleteNoChargeAsync("buyer", fixture.Order.OrderNumber, default);
        Assert.Equal("NO_CHARGE_NOT_ELIGIBLE", result.ErrorCode);
        Assert.Equal(OrderStatus.Pending, fixture.Order.OrderStatus);
        Assert.Equal(VoucherRedemptionStatus.Reserved, fixture.Order.VoucherRedemption!.RedemptionStatus);
    }

    private static decimal Money(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public Gateway Gateway { get; } = new();
        public Repository Repository { get; }
        public MembershipPurchaseService Service { get; }
        public Voucher Voucher => Repository.Vouchers[0];
        public Order Order => Repository.Order;
        public ApplicationUser Buyer => Repository.Buyer;
        public Fixture(PaymentMethod method = PaymentMethod.VietQR)
        {
            Repository = new(method);
            Service = new(Repository, Gateway, Clock);
        }
        public Task<Result<MembershipPurchaseResponse>> Apply(string? code = "SAVE20") =>
            Service.ApplyVoucherAsync("buyer", Order.OrderNumber, new(code), default);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private sealed class Gateway : IMembershipPaymentGateway
    {
        public bool Available { get; set; } = true;
        public decimal LastAmount { get; private set; }
        public SepayTransfer? Transfer { get; set; }
        public VietQrPaymentResponse? CreateVietQr(string orderNumber, decimal amount)
        {
            LastAmount = amount;
            return Available ? new("https://example.test/qr", "BANK", "12345", "GYM", orderNumber) : null;
        }
        public Result<SepayTransfer> Verify(string authorization, string timestamp, string signature, byte[] rawBody, DateTimeOffset now) =>
            Result<SepayTransfer>.Success(Transfer!);
    }

    private sealed class Repository : IMembershipPurchaseRepository
    {
        public List<Voucher> Vouchers { get; } = [new()
        {
            Id = 1, Code = "SAVE20", Name = "Campaign", DiscountType = DiscountType.PERCENT, DiscountValue = 20,
            MaxDiscountAmount = 100000, MaxRedemptions = 10, StartAt = Now.AddDays(-1).UtcDateTime,
            EndAt = Now.AddDays(1).UtcDateTime
        }];
        public ApplicationUser Buyer { get; } = User("buyer", UserRole.Customer);
        public ApplicationUser Staff { get; } = User("staff", UserRole.Staff);
        public Order Order { get; }
        public int Confirmed { get; set; }
        public int Reserved { get; set; }
        public int SaveCount { get; private set; }
        public int Transactions { get; private set; }
        public List<AuditLog> Audits { get; } = [];

        public Repository(PaymentMethod method)
        {
            var plan = new Package
            {
                Id = 1, Name = "Gym", Price = 500000,
                Components = [new() { Id = 1, ComponentType = ComponentType.GYM, DurationDays = 30 }]
            };
            Order = new()
            {
                Id = 1, OrderNumber = "ORD-20261113-012345ABCDEF", PurchaserUserId = "buyer", Purchaser = Buyer,
                CreatedAt = Now.UtcDateTime, Channel = method == PaymentMethod.Cash ? OrderChannel.Counter : OrderChannel.Online,
                Items = [new() { Id = 1, PackageId = 1, Package = plan, PackageNameSnapshot = "Gym", UnitPriceSnapshot = 500000, BeneficiaryUserId = "buyer" }],
                PaymentTransaction = new() { Id = 1, PaymentMethod = method, GatewayName = method == PaymentMethod.Cash ? "Cash" : "SePay", AmountDue = 500000 }
            };
        }

        public async Task<T> ExecuteInSerializableTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
        { Transactions++; return await work(); }
        public Task<List<ApplicationUser>> LoadCheckoutUsersAsync(string purchaserId, string[] emails, CancellationToken ct) =>
            Task.FromResult(new[] { Buyer, Staff }.Where(user => user.UserId == purchaserId).ToList());
        public Task<Voucher?> FindVoucherForUpdateAsync(string normalizedCode, CancellationToken ct) =>
            Task.FromResult(Vouchers.SingleOrDefault(voucher => voucher.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)));
        public Task<(int Confirmed, int Reserved)> CountVoucherCapacityAsync(int voucherId, CancellationToken ct) => Task.FromResult((Confirmed, Reserved));
        public Task<Order?> FindOrderAsync(string orderNumber, string? purchaserId, bool forUpdate, CancellationToken ct) =>
            Task.FromResult(orderNumber == Order.OrderNumber && (purchaserId is null || purchaserId == Order.PurchaserUserId) ? Order : null);
        public Task SaveChangesAsync(CancellationToken ct) { SaveCount++; return Task.CompletedTask; }
        public Task<List<ApplicationUser>> LockUsersAsync(string[] userIds, CancellationToken ct) => Task.FromResult(new List<ApplicationUser> { Buyer });
        public Task<ApplicationRole?> GetMemberRoleAsync(CancellationToken ct) => Task.FromResult<ApplicationRole?>(new() { Id = 2, RoleName = "Member" });
        public Task<List<MemberMembership>> LoadCurrentMembershipsAsync(string[] userIds, DateOnly today, CancellationToken ct) => Task.FromResult(new List<MemberMembership>());
        public Task<string?> FindRenewalPurchaserAsync(string orderNumber, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<bool> SepayReferenceUsedAsync(string reference, CancellationToken ct) => Task.FromResult(false);
        public Task<List<Order>> LoadExpiredOrdersAsync(DateTime onlineCutoff, DateTime cashCutoff, CancellationToken ct) =>
            Task.FromResult(Order.OrderStatus == OrderStatus.Pending && Order.PaymentTransaction!.PaymentStatus == PaymentStatus.Pending &&
                Order.CreatedAt <= (Order.Channel == OrderChannel.Online ? onlineCutoff : cashCutoff) ? new List<Order> { Order } : []);
        public void Add(AuditLog audit) => Audits.Add(audit);
        public void Add(Order order) => throw new NotSupportedException();
        public Task<List<Package>> LoadPlansAsync(int[] ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<OwnedMembership>> ListOwnedMembershipsAsync(string memberId, CancellationToken ct) => throw new NotSupportedException();
        public Task<OwnedMembershipDetail?> FindOwnedMembershipAsync(string memberId, int membershipId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Gym?> GetRenewalPolicyGymAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<List<MemberMembership>> LoadRenewableMembershipsAsync(string memberId, int? membershipId, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<Order>> LoadRenewalOrdersAsync(int[] membershipIds, bool forUpdate, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<MemberMembership>> LoadDueMembershipsAsync(DateOnly today, CancellationToken ct) => throw new NotSupportedException();

        private static ApplicationUser User(string id, UserRole role) => new()
        {
            UserId = id, GymId = 1, AccountStatus = AccountStatus.Active,
            UserRoles = [new() { Role = new() { Id = 1, RoleName = role.ToString() } }]
        };
    }
}
