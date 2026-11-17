using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipTransactionTests
{
    private static readonly DateTime PurchasedAt = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData("customer")]
    [InlineData("member")]
    public async Task HistoryUsesAuthenticatedPurchaserScopeAndForwardsPaginationAndCancellation(string owner)
    {
        var repository = new RepositoryStub { Rows = [Summary()] };
        using var cancellation = new CancellationTokenSource();

        var result = await new MembershipTransactionService(repository).ListAsync(owner, 2, 10, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal((owner, 2, 10, cancellation.Token), repository.LastList);
        var page = Assert.IsType<VFMS.Contracts.Common.PagedResponse<MembershipTransactionSummaryResponse>>(result.Data);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal("Original gym plan", Assert.Single(page.Items).PurchaseSummary);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPaginationNeverQueriesTheRepository(int page, int pageSize)
    {
        var repository = new RepositoryStub();

        var result = await new MembershipTransactionService(repository).ListAsync("customer", page, pageSize, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_PAGINATION", result.ErrorCode);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task EmptyHistoryIsASuccessfulEmptyPage()
    {
        var result = await new MembershipTransactionService(new RepositoryStub()).ListAsync("customer", 1, 10, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data!.TotalCount);
        Assert.Empty(result.Data.Items);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(999)]
    public async Task MissingAndOtherPurchasersTransactionReturnIdenticalNotFound(int orderId)
    {
        var repository = new RepositoryStub();
        using var cancellation = new CancellationTokenSource();

        var result = await new MembershipTransactionService(repository).GetAsync("customer", orderId, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("TRANSACTION_NOT_FOUND", result.ErrorCode);
        Assert.Null(result.Data);
        Assert.Equal(("customer", orderId, cancellation.Token), repository.LastDetail);
    }

    [Fact]
    public async Task MissingIdentityNeverQueriesTheRepository()
    {
        var repository = new RepositoryStub();
        var service = new MembershipTransactionService(repository);

        Assert.Equal(401, (await service.ListAsync("", 1, 10, default)).StatusCode);
        Assert.Equal(401, (await service.GetAsync(" ", 1, default)).StatusCode);
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(PaymentMethod.VietQR, 200000, "VietQR")]
    [InlineData(PaymentMethod.Cash, 200000, "Cash")]
    [InlineData(PaymentMethod.NoCharge, 0, "NoCharge")]
    public async Task PaidHistoryPreservesAuthoritativePaymentMethodAndSettledAmounts(
        PaymentMethod method, int amount, string expectedMethod)
    {
        var row = Detail() with { Transaction = Summary() with
            { PaymentMethod = method, AmountDue = amount, AmountReceived = amount } };
        var repository = new RepositoryStub { Detail = row };

        var result = await new MembershipTransactionService(repository).GetAsync("customer", 1, default);

        var detail = Assert.IsType<MembershipTransactionDetailResponse>(result.Data);
        Assert.Equal(expectedMethod, detail.Transaction.PaymentMethod);
        Assert.Equal((decimal)amount, detail.Transaction.AmountDue);
        Assert.Equal((decimal)amount, detail.Transaction.AmountReceived);
        Assert.Equal("Paid", detail.Transaction.PaymentStatus);
        Assert.Equal("INV-ORIGINAL", detail.Invoice!.InvoiceNumber);
        Assert.Equal("Issued", detail.Invoice.InvoiceStatus);
    }

    [Fact]
    public async Task DetailUsesSnapshotItemValuesAndVoucherIdentityAndNotMutableConfiguration()
    {
        var row = Detail();
        var result = await new MembershipTransactionService(new RepositoryStub { Detail = row })
            .GetAsync("customer", 1, default);

        var detail = Assert.IsType<MembershipTransactionDetailResponse>(result.Data);
        Assert.Equal(300000m, detail.Subtotal);
        Assert.Equal(100000m, detail.DiscountAmount);
        Assert.Equal(200000m, detail.Transaction.AmountDue);
        Assert.Equal(200000m, detail.Transaction.AmountReceived);
        Assert.Equal("ORIGINAL100", detail.Voucher!.Code);
        Assert.Equal("Original promotion", detail.Voucher.Name);
        Assert.Equal(100000m, detail.Voucher.DiscountAmount);
        Assert.Equal("Confirmed", detail.Voucher.RedemptionStatus);
        Assert.Equal("Original gym plan", Assert.Single(detail.Items).PlanName);
        Assert.Equal(300000m, Assert.Single(detail.Items).UnitPrice);
        Assert.Equal(new DateOnly(2026, 11, 2), Assert.Single(detail.Items).ActivationStartDate);
        Assert.True(Assert.Single(detail.Items).IsForPurchaser);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), detail.Transaction.CreatedAt);
        Assert.Equal(TimeSpan.Zero, detail.Invoice!.IssuedAt.Offset);
    }

    [Fact]
    public async Task NoVoucherPurchaseShowsFullPriceAndNoVoucher()
    {
        var row = Detail() with { Voucher = null, Transaction = Summary() with
            { AmountDue = 300000m, AmountReceived = 300000m } };
        var result = await new MembershipTransactionService(new RepositoryStub { Detail = row })
            .GetAsync("member", 1, default);

        Assert.Equal(300000m, result.Data!.Subtotal);
        Assert.Equal(0m, result.Data.DiscountAmount);
        Assert.Null(result.Data.Voucher);
    }

    [Fact]
    public async Task RemovedVoucherIsNotPresentedAsTheDiscountOnALaterFullPricePayment()
    {
        var row = Detail() with
        {
            Voucher = Detail().Voucher! with { RedemptionStatus = VoucherRedemptionStatus.Released },
            Transaction = Summary() with { AmountDue = 300000m, AmountReceived = 300000m }
        };
        var result = await new MembershipTransactionService(new RepositoryStub { Detail = row })
            .GetAsync("customer", 1, default);

        Assert.Equal(0m, result.Data!.DiscountAmount);
        Assert.Null(result.Data.Voucher);
    }

    [Theory]
    [InlineData(OrderStatus.Expired, PaymentStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Cancelled)]
    [InlineData(OrderStatus.Pending, PaymentStatus.Failed)]
    [InlineData(OrderStatus.Pending, PaymentStatus.Pending)]
    public async Task HistoryShowsPersistedLifecycleWithoutExpiringOrdersOrCreatingInvoices(
        OrderStatus orderStatus, PaymentStatus paymentStatus)
    {
        var row = Detail() with
        {
            Transaction = Summary() with { OrderStatus = orderStatus, PaymentStatus = paymentStatus,
                AmountReceived = 0, CompletedAt = null, InvoiceNumber = null },
            PaidAt = null, Invoice = null,
            Voucher = Detail().Voucher! with { RedemptionStatus = VoucherRedemptionStatus.Released }
        };
        var repository = new RepositoryStub { Detail = row };

        var result = await new MembershipTransactionService(repository).GetAsync("customer", 1, default);

        Assert.Equal(orderStatus.ToString(), result.Data!.Transaction.OrderStatus);
        Assert.Equal(paymentStatus.ToString(), result.Data.Transaction.PaymentStatus);
        Assert.Null(result.Data.Invoice);
        Assert.Null(result.Data.PaidAt);
        Assert.Equal(0m, result.Data.Transaction.AmountReceived);
        Assert.Same(row, repository.Detail);
        if (orderStatus is OrderStatus.Cancelled or OrderStatus.Expired)
            Assert.Equal("Released", result.Data.Voucher!.RedemptionStatus);
    }

    [Fact]
    public async Task RenewalIsDerivedFromPersistedItemRelationshipAndGiftDoesNotExposeIdentity()
    {
        var row = Detail() with
        {
            Transaction = Summary() with { IsRenewal = true },
            Items = [new("Original gym plan", 300000m, false, new DateOnly(2026, 11, 2), true)]
        };

        var result = await new MembershipTransactionService(new RepositoryStub { Detail = row })
            .GetAsync("customer", 1, default);

        Assert.True(result.Data!.Transaction.IsRenewal);
        Assert.True(Assert.Single(result.Data.Items).IsRenewal);
        Assert.False(Assert.Single(result.Data.Items).IsForPurchaser);
    }

    private static OwnedMembershipTransaction Summary() => new(1, "ORD-ORIGINAL", PurchasedAt, PurchasedAt,
        OrderStatus.Paid, PaymentStatus.Paid, PaymentMethod.VietQR, false, "Original gym plan", 1,
        200000m, 200000m, "INV-ORIGINAL");

    private static OwnedMembershipTransactionDetail Detail() => new(Summary(), PurchasedAt,
        [new("Original gym plan", 300000m, true, new DateOnly(2026, 11, 2), false)],
        new("ORIGINAL100", "Original promotion", 100000m, VoucherRedemptionStatus.Confirmed),
        new("INV-ORIGINAL", PurchasedAt, InvoiceStatus.Issued));

    private sealed class RepositoryStub : IMembershipTransactionRepository
    {
        public IReadOnlyList<OwnedMembershipTransaction> Rows { get; init; } = [];
        public OwnedMembershipTransactionDetail? Detail { get; init; }
        public int Calls { get; private set; }
        public (string Owner, int Page, int PageSize, CancellationToken Token) LastList { get; private set; }
        public (string Owner, int OrderId, CancellationToken Token) LastDetail { get; private set; }

        public Task<(int TotalCount, IReadOnlyList<OwnedMembershipTransaction> Items)> ListAsync(
            string purchaserId, int page, int pageSize, CancellationToken ct)
        {
            Calls++;
            LastList = (purchaserId, page, pageSize, ct);
            return Task.FromResult((Rows.Count, Rows));
        }

        public Task<OwnedMembershipTransactionDetail?> FindAsync(string purchaserId, int orderId, CancellationToken ct)
        {
            Calls++;
            LastDetail = (purchaserId, orderId, ct);
            return Task.FromResult(Detail);
        }
    }
}
