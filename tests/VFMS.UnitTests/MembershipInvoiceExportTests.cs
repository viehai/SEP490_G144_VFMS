using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class MembershipInvoiceExportTests
{
    private static readonly DateTime Timestamp = new(2026, 11, 15, 4, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(OrderStatus.Paid, PaymentStatus.Paid, InvoiceStatus.Issued, true)]
    [InlineData(OrderStatus.Pending, PaymentStatus.Pending, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Cancelled, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Expired, PaymentStatus.Cancelled, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Paid, PaymentStatus.Failed, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Paid, PaymentStatus.Pending, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Paid, PaymentStatus.Refunded, InvoiceStatus.Issued, false)]
    [InlineData(OrderStatus.Paid, PaymentStatus.Paid, InvoiceStatus.Voided, false)]
    [InlineData(OrderStatus.Paid, PaymentStatus.Paid, null, false)]
    public async Task ExportEligibilityUsesRealPersistedOrderPaymentAndExistingInvoiceStates(
        OrderStatus orderStatus, PaymentStatus paymentStatus, InvoiceStatus? invoiceStatus, bool expected)
    {
        var repository = new TransactionRepositoryStub
        {
            Row = new(new(1, "ORD-1", Timestamp, Timestamp, orderStatus, paymentStatus,
                PaymentMethod.Cash, false, "Gói tập ban đầu", 1, 500000m, 500000m, "INV-1"), Timestamp,
                [new("Gói tập ban đầu", 500000m, true, null, false)], null,
                invoiceStatus is { } status ? new("INV-1", Timestamp, status) : null, "Nguyễn Thị Ánh")
        };

        var result = await new MembershipTransactionService(repository).GetAsync("purchaser", 1, default);

        Assert.Equal(expected, result.Data!.CanExportInvoicePdf);
        Assert.Equal("Nguyễn Thị Ánh", result.Data.PurchaserName);
        var renderer = new RendererStub();
        var exports = new MembershipInvoiceExportService(new TransactionServiceStub { Detail = result }, renderer);
        var export = await exports.ExportAsync("purchaser", 1, default);
        Assert.Equal(expected, export.IsSuccess);
        Assert.Equal(expected ? 1 : 0, renderer.Calls);
        if (!expected)
        {
            Assert.Equal(409, export.StatusCode);
            Assert.Equal("INVOICE_EXPORT_UNAVAILABLE", export.ErrorCode);
        }
    }

    [Theory]
    [InlineData("customer", "VietQR", 400000)]
    [InlineData("member", "Cash", 400000)]
    [InlineData("customer", "NoCharge", 0)]
    public async Task ExportPassesTheExactOwnedFinancialViewToOneRenderer(
        string purchaserId, string method, int finalAmount)
    {
        var detail = Detail(method, finalAmount);
        var transactions = new TransactionServiceStub { Detail = Result<MembershipTransactionDetailResponse>.Success(detail) };
        var renderer = new RendererStub();
        using var cancellation = new CancellationTokenSource();

        var result = await new MembershipInvoiceExportService(transactions, renderer)
            .ExportAsync(purchaserId, 17, cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal((purchaserId, 17, cancellation.Token), transactions.LastQuery);
        Assert.Equal(1, transactions.Calls);
        Assert.Equal(1, renderer.Calls);
        Assert.Same(detail, renderer.Detail);
        Assert.Equal(cancellation.Token, renderer.Token);
        Assert.Same(renderer.Bytes, result.Data!.Content);
        Assert.Equal("VFMS-Invoice-INV-ORIGINAL.pdf", result.Data.FileName);
        Assert.Equal(2, renderer.Detail!.Items.Count);
        Assert.Equal(700000m, renderer.Detail.Subtotal);
        Assert.Equal(700000m - finalAmount, renderer.Detail.DiscountAmount);
        Assert.Equal("ORIGINAL", renderer.Detail.Voucher!.Code);
        Assert.True(renderer.Detail.Transaction.IsRenewal);
        Assert.Equal(method, renderer.Detail.Transaction.PaymentMethod);
    }

    [Theory]
    [InlineData(401, "MSG-11")]
    [InlineData(404, "TRANSACTION_NOT_FOUND")]
    public async Task AuthenticationAndEnumerationSafeOwnershipFailuresNeverReachRenderer(int status, string code)
    {
        var transactions = new TransactionServiceStub
        {
            Detail = Result<MembershipTransactionDetailResponse>.Failure("Denied", code, status)
        };
        var renderer = new RendererStub();

        var result = await new MembershipInvoiceExportService(transactions, renderer).ExportAsync("purchaser", 42, default);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(0, renderer.Calls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task IneligibleOrMissingInvoiceCannotBeManufacturedByExport(bool eligible, bool hasInvoice)
    {
        var detail = Detail() with { CanExportInvoicePdf = eligible, Invoice = hasInvoice ? Detail().Invoice : null };
        var renderer = new RendererStub();
        var transactions = new TransactionServiceStub { Detail = Result<MembershipTransactionDetailResponse>.Success(detail) };

        var result = await new MembershipInvoiceExportService(transactions, renderer).ExportAsync("purchaser", 17, default);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(0, renderer.Calls);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("../../invoice\\unsafe:\r\n", "VFMS-Invoice-invoice-unsafe.pdf")]
    [InlineData("..", "VFMS-Invoice-Order-17.pdf")]
    [InlineData("INV-123_456", "VFMS-Invoice-INV-123_456.pdf")]
    public async Task FilenameIsSafeAndDeterministic(string number, string expected)
    {
        var detail = Detail() with { Invoice = Detail().Invoice! with { InvoiceNumber = number } };
        var transactions = new TransactionServiceStub { Detail = Result<MembershipTransactionDetailResponse>.Success(detail) };

        var result = await new MembershipInvoiceExportService(transactions, new RendererStub())
            .ExportAsync("purchaser", 17, default);

        Assert.Equal(expected, result.Data!.FileName);
        Assert.Matches("^VFMS-Invoice-[A-Za-z0-9_-]+\\.pdf$", result.Data.FileName);
    }

    [Fact]
    public async Task VeryLongInvoiceNumberCannotCreateAnUnboundedDownloadFilename()
    {
        var detail = Detail() with { Invoice = Detail().Invoice! with { InvoiceNumber = new string('A', 500) } };
        var transactions = new TransactionServiceStub { Detail = Result<MembershipTransactionDetailResponse>.Success(detail) };

        var result = await new MembershipInvoiceExportService(transactions, new RendererStub())
            .ExportAsync("purchaser", 17, default);

        Assert.Equal("VFMS-Invoice-" + new string('A', 80) + ".pdf", result.Data!.FileName);
    }

    [Fact]
    public async Task CancelledRequestDoesNotStartPdfRendering()
    {
        var renderer = new RendererStub();
        var transactions = new TransactionServiceStub { Detail = Result<MembershipTransactionDetailResponse>.Success(Detail()) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new MembershipInvoiceExportService(transactions, renderer).ExportAsync("purchaser", 17, cancellation.Token));

        Assert.Equal(0, renderer.Calls);
    }

    private static MembershipTransactionDetailResponse Detail(string method = "VietQR", int finalAmount = 400000) => new(
        new(17, "ORD-ORIGINAL", Timestamp, Timestamp, "Paid", "Paid", method, true,
            "Gói hội viên ban đầu", 2, finalAmount, finalAmount, "INV-ORIGINAL"),
        700000m, 700000m - finalAmount, Timestamp,
        [new("Gói hội viên ban đầu", 500000m, true, new DateOnly(2026, 11, 15), true),
            new("Gói thứ hai", 200000m, false, null, false)],
        new("ORIGINAL", "Ưu đãi ban đầu", 700000m - finalAmount, "Confirmed"),
        new("INV-ORIGINAL", Timestamp, "Issued"), "Nguyễn Thị Ánh", true);

    private sealed class TransactionRepositoryStub : IMembershipTransactionRepository
    {
        public OwnedMembershipTransactionDetail? Row { get; init; }
        public Task<OwnedMembershipTransactionDetail?> FindAsync(string purchaserId, int orderId, CancellationToken ct) => Task.FromResult(Row);
        public Task<(int TotalCount, IReadOnlyList<OwnedMembershipTransaction> Items)> ListAsync(
            string purchaserId, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TransactionServiceStub : IMembershipTransactionService
    {
        public required Result<MembershipTransactionDetailResponse> Detail { get; init; }
        public int Calls { get; private set; }
        public (string Purchaser, int OrderId, CancellationToken Token) LastQuery { get; private set; }
        public Task<Result<MembershipTransactionDetailResponse>> GetAsync(string purchaserId, int orderId, CancellationToken ct)
        {
            Calls++;
            LastQuery = (purchaserId, orderId, ct);
            return Task.FromResult(Detail);
        }
        public Task<Result<PagedResponse<MembershipTransactionSummaryResponse>>> ListAsync(
            string purchaserId, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RendererStub : IMembershipInvoicePdfRenderer
    {
        public byte[] Bytes { get; } = [1, 2, 3];
        public int Calls { get; private set; }
        public MembershipTransactionDetailResponse? Detail { get; private set; }
        public CancellationToken Token { get; private set; }
        public byte[] Render(MembershipTransactionDetailResponse transaction, CancellationToken ct)
        {
            Calls++;
            Detail = transaction;
            Token = ct;
            return Bytes;
        }
    }
}
