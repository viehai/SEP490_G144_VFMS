using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Admin", HttpStatusCode.Forbidden)]
    public async Task InvoicePdfRequiresCustomerOrMemberRole(string? actor, string? role, HttpStatusCode expected)
    {
        using var client = Client(actor, role);
        using var response = await client.GetAsync(InvoicePdfRoute(1));
        Assert.Equal(expected, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("buyer", "Customer")]
    [InlineData("member", "Member")]
    public async Task InvoicePdfOwnCompletedSePayPurchaseIsARealA4AttachmentWithHistoricalValues(string actor, string role)
    {
        using var client = Client(actor, role);
        var purchase = await CreateAsync(client, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_001);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        var detail = await TransactionDetailAsync(client, id);
        var pdf = await ReadInvoicePdfAsync(client, id, actor == "buyer" ? "uc21-normal" : null);

        Assert.Equal(1, pdf.PageCount);
        AssertPdfTextContains(pdf.Text, purchase.OrderNumber, detail.Invoice!.InvoiceNumber, "Basic", "VietQR");
        AssertPdfMoneyContains(pdf.Text, detail.Subtotal, detail.DiscountAmount,
            detail.Transaction.AmountDue, detail.Transaction.AmountReceived);
        Assert.DoesNotContain("VNPAY", pdf.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(detail.CanExportInvoicePdf);
        Assert.Equal($"VFMS-Invoice-{detail.Invoice.InvoiceNumber}.pdf", pdf.FileName);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task InvoicePdfEnumerationCannotDownloadForeignOrGiftPurchasersInvoice(string role)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic, "gift@example.test")]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var settlement = await NotifyAsync(applied, 21_002);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        var invoiceNumber = (await TransactionDetailAsync(buyer, id)).Invoice!.InvoiceNumber;

        using var foreignMember = Client("member", role);
        using var beneficiary = Client("gift", role);
        foreach (var client in new[] { foreignMember, beneficiary })
        {
            foreach (var requestedId in new[] { id, int.MaxValue })
            {
                using var response = await client.GetAsync(InvoicePdfRoute(requestedId) + "?userId=buyer&ownerId=buyer&invoiceId=" + id);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(purchase.OrderNumber, body);
                Assert.DoesNotContain(invoiceNumber, body);
                Assert.DoesNotContain("SAVE20", body);
            }
        }
        Assert.NotEmpty((await ReadInvoicePdfAsync(buyer, id)).Text);
    }

    [Theory]
    [InlineData("Pending", "Pending")]
    [InlineData("Cancelled", "Cancelled")]
    [InlineData("Expired", "Cancelled")]
    [InlineData("Pending", "Failed")]
    [InlineData("Paid", "Pending")]
    [InlineData("Paid", "Failed")]
    [InlineData("Paid", "Refunded")]
    public async Task InvoicePdfRejectsIneligiblePersistedLifecycleEvenIfAnInvoiceExists(string orderStatus, string paymentStatus)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_003);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var order = await db.Orders.Include(row => row.PaymentTransaction).SingleAsync(row => row.Id == id);
            // Exercise real enum states against a stale invoice; export cannot trust invoice existence alone.
            order.OrderStatus = Enum.Parse<OrderStatus>(orderStatus);
            order.PaymentTransaction!.PaymentStatus = Enum.Parse<PaymentStatus>(paymentStatus);
            await db.SaveChangesAsync();
        }
        var before = await CommerceSnapshotAsync();
        using var response = await buyer.GetAsync(InvoicePdfRoute(id));
        await AssertInvoicePdfUnavailableAsync(response);
        Assert.Equal(before, await CommerceSnapshotAsync());
        Assert.False((await TransactionDetailAsync(buyer, id)).CanExportInvoicePdf);
    }

    [Fact]
    public async Task InvoicePdfPendingCheckoutWithoutInvoiceIsRejectedWithoutCreatingOrSettlingAnything()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        var before = await CommerceSnapshotAsync();
        using var response = await buyer.GetAsync(InvoicePdfRoute(await TransactionOrderIdAsync(purchase)));
        await AssertInvoicePdfUnavailableAsync(response);
        Assert.Equal(before, await CommerceSnapshotAsync());
    }

    [Fact]
    public async Task InvoicePdfCompletedTransactionWithoutInvoiceIsRejectedWithoutManufacturingOne()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_004);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var invoice = await db.Invoices.SingleAsync();
            invoice.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        var before = await CommerceSnapshotAsync();
        using var response = await buyer.GetAsync(InvoicePdfRoute(id));
        await AssertInvoicePdfUnavailableAsync(response);
        Assert.Equal(before, await CommerceSnapshotAsync());
        Assert.False((await TransactionDetailAsync(buyer, id)).CanExportInvoicePdf);
        await using var verify = _app.Services.CreateAsyncScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<VfmsDbContext>().Invoices.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task InvoicePdfVoidedInvoiceCannotBeExportedAsAnIssuedInvoice()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_005);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Invoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Voided;
            await db.SaveChangesAsync();
        }
        var id = await TransactionOrderIdAsync(purchase);
        var before = await CommerceSnapshotAsync();
        using var response = await buyer.GetAsync(InvoicePdfRoute(id));
        await AssertInvoicePdfUnavailableAsync(response);
        Assert.Equal(before, await CommerceSnapshotAsync());
        Assert.False((await TransactionDetailAsync(buyer, id)).CanExportInvoicePdf);
    }

    [Fact]
    public async Task InvoicePdfVoucherAndPlanFinancialSnapshotsRemainConsistentWithUc20AfterManagerEdits()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        var voucher = await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var settlement = await NotifyAsync(applied, 21_006);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        var before = await ReadInvoicePdfAsync(buyer, id);

        using var manager = Client("manager", "Manager");
        using var updated = await manager.PutAsJsonAsync($"/api/manager/promotions/{voucher.Id}",
            new UpdatePromotionRequest("Edited after purchase", "CHANGED21", PromotionDiscountType.Fixed,
                1m, 99_000_000m, null, 20, _clock.Now.UtcDateTime.AddDays(1), _clock.Now.UtcDateTime.AddDays(2), false));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await UpdateRenewalPlanAsync("New price and name", 999_999m, 4);

        var detail = await TransactionDetailAsync(buyer, id);
        var after = await ReadInvoicePdfAsync(buyer, id, "uc21-voucher");
        Assert.Equal(before.Text, after.Text);
        AssertPdfTextContains(after.Text, detail.Invoice!.InvoiceNumber, detail.Transaction.OrderNumber,
            "Basic", detail.Voucher!.Code!, detail.Voucher.Name!);
        AssertPdfMoneyContains(after.Text, detail.Subtotal, detail.DiscountAmount,
            detail.Transaction.AmountDue, detail.Transaction.AmountReceived);
        Assert.DoesNotContain("CHANGED21", after.Text);
        Assert.DoesNotContain("Edited after purchase", after.Text);
        Assert.DoesNotContain("New price and name", after.Text);
    }

    [Fact]
    public async Task InvoicePdfLegacyVoucherWithoutIdentitySnapshotsKeepsDiscountWithoutBorrowingCurrentCampaignIdentity()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var settlement = await NotifyAsync(applied, 21_012);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var redemption = await db.VoucherRedemptions.SingleAsync();
            redemption.VoucherCodeSnapshot = null;
            redemption.VoucherNameSnapshot = null;
            var current = await db.Vouchers.SingleAsync();
            current.Code = "CURRENT21";
            current.Name = "Current mutable campaign identity";
            current.DiscountValue = 1m;
            await db.SaveChangesAsync();
        }

        var id = await TransactionOrderIdAsync(purchase);
        var detail = await TransactionDetailAsync(buyer, id);
        var before = await CommerceSnapshotAsync();
        var pdf = await ReadInvoicePdfAsync(buyer, id, "uc21-legacy-voucher");
        Assert.Null(detail.Voucher!.Code);
        Assert.Null(detail.Voucher.Name);
        Assert.Equal(100_000m, detail.Voucher.DiscountAmount);
        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal(100_000m, detail.DiscountAmount);
        Assert.Equal(400_000m, detail.Transaction.AmountDue);
        Assert.Equal(400_000m, detail.Transaction.AmountReceived);
        AssertPdfMoneyContains(pdf.Text, 500_000m, 100_000m, 400_000m);
        AssertPdfTextContains(pdf.Text, "Tên và mã giảm giá không được lưu cho giao dịch này.");
        Assert.DoesNotContain("CURRENT21", pdf.Text);
        Assert.DoesNotContain("Current mutable campaign identity", pdf.Text);
        Assert.DoesNotContain("SAVE20", pdf.Text);
        Assert.Equal(before, await CommerceSnapshotAsync());
    }

    [Fact]
    public async Task InvoicePdfPreservesMaximumPersistedDecimalPrecisionWithLongHistoricalItemName()
    {
        const decimal maximumPersistedAmount = 9_999_999_999_999_999.99m;
        const string namePart = "Gói thể hình Đặng Thùy Nguyễn Ánh với quyền lợi dài hạn ";
        var longName = string.Concat(Enumerable.Repeat(namePart, 6))[..256];
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_013);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var item = await db.OrderItems.SingleAsync();
            item.PackageNameSnapshot = longName;
            item.UnitPriceSnapshot = maximumPersistedAmount;
            var payment = await db.PaymentTransactions.SingleAsync();
            payment.AmountDue = maximumPersistedAmount;
            payment.AmountReceived = maximumPersistedAmount;
            // Exercise the existing decimal(18,2) persistence boundary on a completed invoice.
            // Checkout's whole-VND input limit does not narrow the historical database representation.
            await db.SaveChangesAsync();
        }

        var detail = await TransactionDetailAsync(buyer, id);
        var before = await CommerceSnapshotAsync();
        var pdf = await ReadInvoicePdfAsync(buyer, id, "uc21-maxamount");
        Assert.Equal(256, longName.Length);
        Assert.Equal(maximumPersistedAmount, Assert.Single(detail.Items).UnitPrice);
        Assert.Equal(maximumPersistedAmount, detail.Subtotal);
        Assert.Equal(maximumPersistedAmount, detail.Transaction.AmountDue);
        Assert.Equal(maximumPersistedAmount, detail.Transaction.AmountReceived);
        Assert.Equal(0m, detail.DiscountAmount);
        const string amountText = "9.999.999.999.999.999,99 VND";
        // The PDF draws the price column between the first and second lines of the wrapped item name.
        AssertPdfTextContains(pdf.Text.Replace(amountText, string.Empty), longName);
        Assert.Equal(4, Regex.Matches(pdf.Text, Regex.Escape(amountText)).Count);
        Assert.Equal(before, await CommerceSnapshotAsync());
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.Cash)]
    [InlineData(MembershipPaymentMethods.VietQr)]
    public async Task InvoicePdfNoChargeUsesZeroAmountAndActualNoChargeMethod(string checkoutMethod)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], checkoutMethod));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 600_000m);
        await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var completion = await buyer.PostAsync($"{Route}/{purchase.OrderNumber}/complete-no-charge", null);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        var detail = await TransactionDetailAsync(buyer, id);
        var pdf = await ReadInvoicePdfAsync(buyer, id, checkoutMethod == MembershipPaymentMethods.VietQr ? "uc21-nocharge" : null);
        AssertPdfTextContains(pdf.Text, detail.Invoice!.InvoiceNumber, "NoCharge", "SAVE20");
        AssertPdfMoneyContains(pdf.Text, 500_000m, 0m);
        Assert.Equal(0m, detail.Transaction.AmountDue);
        Assert.Equal(0m, detail.Transaction.AmountReceived);
        Assert.DoesNotContain("SePay", pdf.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cash", pdf.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvoicePdfCashCompletionUsesCashRatherThanOnlineGateway()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        using var receipt = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{purchase.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(purchase.AmountDue));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var pdf = await ReadInvoicePdfAsync(buyer, await TransactionOrderIdAsync(purchase), "uc21-cash");
        AssertPdfTextContains(pdf.Text, "Cash", purchase.OrderNumber, "Basic");
        AssertPdfMoneyContains(pdf.Text, 500_000m);
        Assert.DoesNotContain("SePay", pdf.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvoicePdfCompletedRenewalUsesTheSameExportPipeline()
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        using var member = Client("member", "Member");
        var renewal = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id)), HttpStatusCode.Created);
        using var settlement = await NotifyAsync(renewal, 21_007);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(renewal);
        var detail = await TransactionDetailAsync(member, id);
        var pdf = await ReadInvoicePdfAsync(member, id, "uc21-renewal");
        Assert.True(detail.Transaction.IsRenewal);
        AssertPdfTextContains(pdf.Text, detail.Invoice!.InvoiceNumber, renewal.OrderNumber, "Basic", "VietQR");
        AssertPdfMoneyContains(pdf.Text, detail.Transaction.AmountDue);
    }

    [Fact]
    public async Task InvoicePdfIncludesEveryItemAndVietnameseTextUsingPersistedSnapshotNames()
    {
        using var buyer = Client("buyer", "Customer");
        var names = new[] { "Gói Cơ Bản Nguyễn Ánh", "Gói Kết Hợp Thể Hình", "Gói Gia Đình Đặng Thùy" };
        int thirdId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Packages.SingleAsync(row => row.Id == _basic)).Name = names[0];
            (await db.Packages.SingleAsync(row => row.Id == _combo)).Name = names[1];
            var third = Plan(names[2], 250_000m, PackageStatus.Active);
            db.Packages.Add(third);
            await db.SaveChangesAsync();
            thirdId = third.Id;
        }
        var purchase = await CreateAsync(buyer, Purchase([new(_basic), new(_combo), new(thirdId)]));
        using var settlement = await NotifyAsync(purchase, 21_008);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        var detail = await TransactionDetailAsync(buyer, id);
        var pdf = await ReadInvoicePdfAsync(buyer, id, "uc21-multiitem-vietnamese");
        Assert.Equal(3, detail.Items.Count);
        AssertPdfTextContains(pdf.Text, names);
        AssertPdfMoneyContains(pdf.Text, 500_000m, 800_000m, 250_000m, 1_550_000m);
        Assert.DoesNotContain('\uFFFD', pdf.Text);
        Assert.DoesNotContain('\0', pdf.Text);
    }

    [Fact]
    public async Task InvoicePdfFiftyLongVietnameseItemsFlowAcrossA4PagesWithoutDroppingAnyItem()
    {
        using var buyer = Client("buyer", "Customer");
        var plans = Enumerable.Range(1, 50).Select(number => Plan(
            $"Mục {number:00} - Gói thể hình Nguyễn Ánh Đặng Thùy với huấn luyện hướng dẫn và quyền lợi dài hạn tại phòng tập Victor Fitness",
            number * 1_000m, PackageStatus.Active)).ToArray();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            db.Packages.AddRange(plans);
            await db.SaveChangesAsync();
        }
        var purchase = await CreateAsync(buyer, Purchase(plans.Select(plan => new MembershipPurchaseItemRequest(plan.Id)).ToArray()));
        using var settlement = await NotifyAsync(purchase, 21_009);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var pdf = await ReadInvoicePdfAsync(buyer, await TransactionOrderIdAsync(purchase), "uc21-multipage-vietnamese");
        Assert.True(pdf.PageCount > 1, "Fifty long items must paginate instead of clipping on one page.");
        for (var number = 1; number <= 50; number++)
            AssertPdfTextContains(pdf.Text, $"Mục {number:00}");
        AssertPdfMoneyContains(pdf.Text, 1_275_000m);
        Assert.DoesNotContain('\uFFFD', pdf.Text);
    }

    [Fact]
    public async Task InvoicePdfDownloadsAreReadOnlyAndDoNotExposeInternalOrBeneficiarySecrets()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic, "gift@example.test")]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var settlement = await NotifyAsync(applied, 21_010);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var payment = await db.PaymentTransactions.SingleAsync();
            payment.RawGatewayStatus = "PRIVATE-WEBHOOK-RAW-PAYLOAD";
            payment.GatewayTransactionReference = "PRIVATE-SEPAY-REFERENCE";
            (await db.ApplicationUsers.SingleAsync(row => row.UserId == "buyer")).PasswordHash = "PRIVATE-PASSWORD-HASH";
            await db.SaveChangesAsync();
        }
        var before = await CommerceSnapshotAsync();
        var first = await ReadInvoicePdfAsync(buyer, id);
        var second = await ReadInvoicePdfAsync(buyer, id);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(before, await CommerceSnapshotAsync());
        foreach (var secret in new[]
        {
            "PRIVATE-WEBHOOK-RAW-PAYLOAD", "PRIVATE-SEPAY-REFERENCE", "PRIVATE-PASSWORD-HASH", Secret,
            "gift@example.test", "PasswordHash", "RefreshToken", "RawGatewayStatus", "AuditLog", "Data Source="
        })
            Assert.DoesNotContain(secret, first.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvoicePdfFilenameSanitizesPersistedInvoiceCodeAndIsDeterministic()
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
        using var settlement = await NotifyAsync(purchase, 21_011);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.Invoices.SingleAsync()).InvoiceNumber = "../unsafe\\invoice:\r\n21";
            await db.SaveChangesAsync();
        }
        var id = await TransactionOrderIdAsync(purchase);
        var first = await ReadInvoicePdfAsync(buyer, id);
        var repeated = await ReadInvoicePdfAsync(buyer, id);
        Assert.Equal(first.FileName, repeated.FileName);
        Assert.Matches("^VFMS-Invoice-[A-Za-z0-9_-]+\\.pdf$", first.FileName);
        Assert.DoesNotContain("..", first.FileName);
    }

    private static string InvoicePdfRoute(int id) => $"{TransactionsRoute}/{id}/invoice/pdf";

    private static async Task AssertInvoicePdfUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVOICE_EXPORT_UNAVAILABLE", problem.GetProperty("code").GetString());
    }

    private static async Task<ParsedInvoicePdf> ReadInvoicePdfAsync(HttpClient client, int id, string? previewName = null)
    {
        using var response = await client.GetAsync(InvoicePdfRoute(id));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected PDF success, got {response.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition.DispositionType);
        var fileName = (disposition.FileNameStar ?? disposition.FileName)!.Trim('"');
        Assert.Matches("^VFMS-Invoice-[A-Za-z0-9_-]+\\.pdf$", fileName);
        Assert.True(response.Headers.CacheControl?.NoStore ?? false);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes.AsSpan(0, 5)));
        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 0);
        var text = new StringBuilder();
        foreach (var page in document.Pages)
        {
            Assert.InRange(page.Width.Point, 594d, 596d);
            Assert.InRange(page.Height.Point, 841d, 843d);
            text.AppendLine(ExtractInvoicePageText(page));
        }
        var previewDirectory = Environment.GetEnvironmentVariable("VFMS_UC21_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(previewDirectory) && previewName is not null)
        {
            Directory.CreateDirectory(previewDirectory);
            await File.WriteAllBytesAsync(Path.Combine(previewDirectory, previewName + ".pdf"), bytes);
        }
        return new ParsedInvoicePdf(document.PageCount, fileName, text.ToString());
    }

    // This test helper reads PDFsharp's embedded Identity-H font mappings and its parsed text-show
    // operations. It does not inspect renderer inputs or assume that PDF metadata equals page content.
    private static string ExtractInvoicePageText(PdfPage page)
    {
        var fonts = page.Resources.Elements.GetDictionary("/Font")!;
        var maps = new Dictionary<string, Dictionary<int, string>>();
        foreach (var name in fonts.Elements.Keys)
        {
            var font = fonts.Elements.GetDictionary(name)!;
            var unicode = font.Elements.GetDictionary("/ToUnicode");
            if (unicode is null) continue;
            var cmap = Encoding.ASCII.GetString(unicode.Stream.UnfilteredValue);
            var map = new Dictionary<int, string>();
            foreach (Match range in Regex.Matches(cmap, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]{4,8})>"))
            {
                var first = int.Parse(range.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var last = int.Parse(range.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var unicodeValue = int.Parse(range.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                for (var code = first; code <= last; code++)
                    map[code] = char.ConvertFromUtf32(unicodeValue + code - first);
            }
            maps[name] = map;
        }
        Assert.NotEmpty(maps);
        Dictionary<int, string>? activeMap = null;
        var text = new StringBuilder();
        foreach (var operation in ContentReader.ReadContent(page).OfType<COperator>())
        {
            if (operation.Name == "Tf")
                activeMap = maps[((CName)operation.Operands[0]).Name];
            if (operation.Name is not ("Tj" or "TJ")) continue;
            Assert.NotNull(activeMap);
            var values = operation.Name == "TJ" ? (CSequence)operation.Operands[0] : operation.Operands;
            foreach (var value in values.OfType<CString>())
            {
                var encoded = value.Value;
                Assert.Equal(0, encoded.Length % 2);
                for (var index = 0; index < encoded.Length; index += 2)
                {
                    var glyph = (encoded[index] << 8) | encoded[index + 1];
                    Assert.True(activeMap.TryGetValue(glyph, out var character), $"Unmapped PDF glyph {glyph}.");
                    text.Append(character);
                }
            }
            text.AppendLine();
        }
        return text.ToString();
    }

    private static void AssertPdfTextContains(string text, params string[] expected)
    {
        var normalized = Regex.Replace(text.Normalize(), @"\s+", " ");
        foreach (var value in expected)
            Assert.Contains(Regex.Replace(value.Normalize(), @"\s+", " "), normalized);
    }

    private static void AssertPdfMoneyContains(string text, params decimal[] amounts)
    {
        foreach (var amount in amounts)
            AssertPdfTextContains(text, amount.ToString("#,0.##", CultureInfo.GetCultureInfo("vi-VN")));
    }

    private sealed record ParsedInvoicePdf(int PageCount, string FileName, string Text);
}
