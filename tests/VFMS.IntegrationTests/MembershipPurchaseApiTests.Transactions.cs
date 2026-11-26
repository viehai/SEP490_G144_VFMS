using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Promotions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string TransactionsRoute = "/api/membership-transactions";

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("staff", "Staff", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Manager", HttpStatusCode.Forbidden)]
    [InlineData("buyer", "PT", HttpStatusCode.Forbidden)]
    [InlineData("manager", "Admin", HttpStatusCode.Forbidden)]
    public async Task TransactionHistoryRequiresCustomerOrMemberRole(string? actor, string? role, HttpStatusCode expected)
    {
        using var client = Client(actor, role);
        using var list = await client.GetAsync(TransactionsRoute);
        using var detail = await client.GetAsync($"{TransactionsRoute}/1");
        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, detail.StatusCode);
    }

    [Theory]
    [InlineData("buyer", "Customer")]
    [InlineData("member", "Member")]
    public async Task TransactionHistoryUsesPurchaserIdentityWithoutRequiringActiveMembership(string actor, string role)
    {
        using var client = Client(actor, role);
        using var stranger = Client("gift", "Customer");
        var own = await CreateAsync(client, Purchase([new(_basic)]));
        var other = await CreateAsync(stranger, Purchase([new(_combo)]));

        var page = await ListTransactionsAsync(client, "?userId=gift&purchaserUserId=gift");
        var row = Assert.Single(page.Items);
        Assert.Equal(own.OrderNumber, row.OrderNumber);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Pending", row.OrderStatus);
        Assert.Equal("Pending", row.PaymentStatus);
        Assert.Equal("VietQR", row.PaymentMethod);
        Assert.Equal(500_000m, row.AmountDue);
        Assert.Equal(0m, row.AmountReceived);
        Assert.Null(row.InvoiceNumber);
        var detail = await TransactionDetailAsync(client, row.OrderId);
        Assert.Equal(row, detail.Transaction);
        Assert.Null(detail.Invoice);
        Assert.Null(detail.Voucher);
        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal(0m, detail.DiscountAmount);
        Assert.Null(detail.PaidAt);

        var otherId = await TransactionOrderIdAsync(other);
        foreach (var id in new[] { otherId, int.MaxValue })
        {
            using var response = await client.GetAsync($"{TransactionsRoute}/{id}?userId=gift");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(problem.TryGetProperty("transaction", out _));
        }

        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().MemberMemberships.ToListAsync());
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Member")]
    public async Task TransactionHistoryDoesNotExposeGiftPurchasersPaymentOrInvoiceToBeneficiary(string beneficiaryRole)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic, "gift@example.test")]));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var settled = await NotifyAsync(applied, 20_001);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        var id = await TransactionOrderIdAsync(purchase);

        using var beneficiary = Client("gift", beneficiaryRole);
        var page = await ListTransactionsAsync(beneficiary);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        using var enumerated = await beneficiary.GetAsync($"{TransactionsRoute}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, enumerated.StatusCode);
        var deniedBody = await enumerated.Content.ReadAsStringAsync();
        Assert.DoesNotContain(purchase.OrderNumber, deniedBody);
        Assert.DoesNotContain("SAVE20", deniedBody);

        var legitimate = await TransactionDetailAsync(buyer, id);
        Assert.False(Assert.Single(legitimate.Items).IsForPurchaser);
        Assert.NotNull(legitimate.Invoice);
        var json = JsonSerializer.Serialize(legitimate);
        Assert.DoesNotContain("gift@example.test", json);
        Assert.DoesNotContain("GatewayTransactionReference", json);
        Assert.DoesNotContain("RawGatewayStatus", json);
        Assert.DoesNotContain("PasswordHash", json);
    }

    [Fact]
    public async Task TransactionHistoryReturnsTenOrdersPerPageByDefault()
    {
        using var buyer = Client("buyer", "Customer");
        var orders = new List<string>();
        for (var i = 0; i < 11; i++)
        {
            var purchase = await CreateAsync(buyer, Purchase([new(_basic)]));
            orders.Add(purchase.OrderNumber);
        }
        orders.Reverse();

        var first = await ListTransactionsAsync(buyer);
        var second = await ListTransactionsAsync(buyer, "?page=2");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(11, first.TotalCount);
        Assert.Equal(orders.Take(10), first.Items.Select(item => item.OrderNumber));
        Assert.Equal(orders[10], Assert.Single(second.Items).OrderNumber);
    }

    [Fact]
    public async Task TransactionHistoryIsPagedNewestFirstWithDeterministicIdTieBreakerAndDatabaseOwnership()
    {
        using var buyer = Client("buyer", "Customer");
        using var other = Client("member", "Member");
        var first = await CreateAsync(buyer, Purchase([new(_basic)]));
        var second = await CreateAsync(buyer, Purchase([new(_basic)]));
        var third = await CreateAsync(buyer, Purchase([new(_combo)]));
        var foreign = await CreateAsync(other, Purchase([new(_basic)]));
        var ids = new[]
        {
            await TransactionOrderIdAsync(first), await TransactionOrderIdAsync(second), await TransactionOrderIdAsync(third)
        };
        var tied = _clock.Now.UtcDateTime.AddMinutes(-2);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            await db.Orders.Where(row => ids.Contains(row.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, tied));
            await db.Orders.Where(row => row.Id == ids[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, tied.AddMinutes(1)));
            // Incomplete internal rows are not user-facing payment history.
            db.Orders.Add(new Order { PurchaserUserId = "buyer", OrderNumber = "INTERNAL-NO-PAYMENT" });
            await db.SaveChangesAsync();
        }

        var page1 = await ListTransactionsAsync(buyer, "?page=1&pageSize=2");
        var page2 = await ListTransactionsAsync(buyer, "?page=2&pageSize=2");
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([ids[0], ids[2]], page1.Items.Select(row => row.OrderId));
        Assert.Equal(ids[1], Assert.Single(page2.Items).OrderId);
        Assert.DoesNotContain(page1.Items.Concat(page2.Items), row => row.OrderNumber == foreign.OrderNumber);
        Assert.Empty((await ListTransactionsAsync(buyer, "?page=3&pageSize=2")).Items);

        using var internalDetail = await buyer.GetAsync($"{TransactionsRoute}/{await InternalOrderIdAsync()}");
        Assert.Equal(HttpStatusCode.NotFound, internalDetail.StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=2147483647&pageSize=100")]
    public async Task TransactionHistoryRejectsInvalidPagination(string query)
    {
        using var client = Client("buyer", "Customer");
        using var response = await client.GetAsync(TransactionsRoute + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransactionHistoryEmptyPageIsSuccessful()
    {
        using var client = Client("buyer", "Customer");
        var page = await ListTransactionsAsync(client);
        Assert.Equal(1, page.Page);
        Assert.InRange(page.PageSize, 1, 100);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task TransactionHistoryRetainsPaidPurchasesForCustomerWhoseMembershipHasExpired()
    {
        var expiredMembership = await SeedMembershipAsync(Today.AddDays(-10), MembershipStatus.Expired, "buyer", "Customer");
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var memberRoles = await db.ApplicationUserRoles.Include(row => row.Role)
                .Where(row => row.UserId == "buyer" && row.Role.RoleName == "Member").ToListAsync();
            db.ApplicationUserRoles.RemoveRange(memberRoles);
            var customerRole = await db.ApplicationRoles.SingleAsync(row => row.RoleName == "Customer");
            db.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = "buyer", RoleId = customerRole.Id });
            await db.SaveChangesAsync();
            Assert.DoesNotContain("Member", await RolesAsync(db, "buyer"));
            Assert.Equal(MembershipStatus.Expired,
                (await db.MemberMemberships.SingleAsync(row => row.Id == expiredMembership.Id)).MembershipStatus);
        }

        using var customer = Client("buyer", "Customer");
        var row = Assert.Single((await ListTransactionsAsync(customer)).Items);
        Assert.Equal("Paid", row.PaymentStatus);
        var detail = await TransactionDetailAsync(customer, row.OrderId);
        Assert.NotNull(detail.Invoice);
        Assert.Equal(500_000m, detail.Transaction.AmountReceived);
    }

    [Fact]
    public async Task TransactionHistorySettledFinancialAndVoucherSnapshotsSurviveManagerCatalogAndPromotionEdits()
    {
        using var buyer = Client("buyer", "Customer");
        var original = await CreateAsync(buyer, Purchase([new(_basic), new(_combo)]));
        var voucher = await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, original);
        using var settlement = await NotifyAsync(applied, 20_002);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var id = await TransactionOrderIdAsync(original);
        var before = await TransactionDetailAsync(buyer, id);
        var basic = Assert.Single(await ListOwnedMembershipsAsync(buyer), membership => membership.MembershipPlanId == _basic);
        await ActivateMembershipAsync(buyer, basic.MembershipId, Today.AddDays(1));

        using var manager = Client("manager", "Manager");
        using var promotionUpdate = await manager.PutAsJsonAsync($"/api/manager/promotions/{voucher.Id}",
            new UpdatePromotionRequest("Replacement campaign", "REPLACED", PromotionDiscountType.Fixed,
                1m, 99_000_000m, null, 20, _clock.Now.UtcDateTime.AddDays(1), _clock.Now.UtcDateTime.AddDays(2), false));
        Assert.Equal(HttpStatusCode.OK, promotionUpdate.StatusCode);
        await UpdateRenewalPlanAsync("Basic later", 42m, 5);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var combo = await db.Packages.SingleAsync(plan => plan.Id == _combo);
            combo.Name = "Combo later";
            combo.Price = 1m;
            await db.SaveChangesAsync();
        }

        var after = await TransactionDetailAsync(buyer, id);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(1_300_000m, after.Subtotal);
        Assert.Equal(100_000m, after.DiscountAmount);
        Assert.Equal(1_200_000m, after.Transaction.AmountDue);
        Assert.Equal(1_200_000m, after.Transaction.AmountReceived);
        Assert.Equal("Paid", after.Transaction.OrderStatus);
        Assert.Equal("Paid", after.Transaction.PaymentStatus);
        Assert.Equal("VietQR", after.Transaction.PaymentMethod);
        Assert.NotNull(after.PaidAt);
        Assert.Collection(after.Items,
            item => { Assert.Equal("Basic", item.PlanName); Assert.Equal(500_000m, item.UnitPrice); Assert.Null(item.ActivationStartDate); },
            item => { Assert.Equal("Combo", item.PlanName); Assert.Equal(800_000m, item.UnitPrice); });
        Assert.Equal("SAVE20", after.Voucher!.Code);
        Assert.Equal("Checkout campaign", after.Voucher.Name);
        Assert.Equal("Confirmed", after.Voucher.RedemptionStatus);
        Assert.Equal(100_000m, after.Voucher.DiscountAmount);
        Assert.Equal("Issued", after.Invoice!.InvoiceStatus);
        Assert.Equal(after.Transaction.InvoiceNumber, after.Invoice.InvoiceNumber);
        Assert.False(after.Transaction.IsRenewal);
        Assert.Equal(after.Transaction, Assert.Single((await ListTransactionsAsync(buyer)).Items));

        await using var verify = _app.Services.CreateAsyncScope();
        var storedInvoice = await verify.ServiceProvider.GetRequiredService<VfmsDbContext>().Invoices.SingleAsync();
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(storedInvoice.IssuedAt, DateTimeKind.Utc)), after.Invoice.IssuedAt);
    }

    [Fact]
    public async Task TransactionHistoryCashReceiptDisplaysPersistedPaidAmountAndInvoiceWithoutVoucher()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        using var confirmation = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{purchase.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(purchase.AmountDue));
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);

        var detail = await TransactionDetailAsync(buyer, await TransactionOrderIdAsync(purchase));
        Assert.Equal("Cash", detail.Transaction.PaymentMethod);
        Assert.Equal("Paid", detail.Transaction.PaymentStatus);
        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal(0m, detail.DiscountAmount);
        Assert.Equal(500_000m, detail.Transaction.AmountDue);
        Assert.Equal(500_000m, detail.Transaction.AmountReceived);
        Assert.Null(detail.Voucher);
        Assert.NotNull(detail.Invoice);
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.Cash)]
    [InlineData(MembershipPaymentMethods.VietQr)]
    public async Task TransactionHistoryNoChargeCompletionDoesNotPretendExternalPaymentOccurred(string checkoutMethod)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], checkoutMethod));
        await SeedCheckoutVoucherAsync(discountType: DiscountType.FIXED, discountValue: 600_000m);
        await ApplyCheckoutVoucherAsync(buyer, purchase);
        using var completion = await buyer.PostAsync($"{Route}/{purchase.OrderNumber}/complete-no-charge", null);
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);

        var detail = await TransactionDetailAsync(buyer, await TransactionOrderIdAsync(purchase));
        Assert.Equal("NoCharge", detail.Transaction.PaymentMethod);
        Assert.Equal("Paid", detail.Transaction.OrderStatus);
        Assert.Equal("Paid", detail.Transaction.PaymentStatus);
        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal(500_000m, detail.DiscountAmount);
        Assert.Equal(0m, detail.Transaction.AmountDue);
        Assert.Equal(0m, detail.Transaction.AmountReceived);
        Assert.Equal("Confirmed", detail.Voucher!.RedemptionStatus);
        Assert.NotNull(detail.Invoice);
    }

    [Fact]
    public async Task TransactionHistoryRenewalIsIdentifiedFromExistingOrderItemRelationship()
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        using var member = Client("member", "Member");
        var purchase = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute, new RenewMembershipRequest(original.Id)), HttpStatusCode.Created);
        using var settlement = await NotifyAsync(purchase, 20_003);
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);

        var detail = await TransactionDetailAsync(member, await TransactionOrderIdAsync(purchase));
        Assert.True(detail.Transaction.IsRenewal);
        var item = Assert.Single(detail.Items);
        Assert.True(item.IsRenewal);
        Assert.True(item.IsForPurchaser);
        Assert.Equal(original.EndDate.AddDays(1), item.ActivationStartDate);
        Assert.Equal(2, (await ListTransactionsAsync(member)).TotalCount);
        Assert.NotNull(detail.Invoice);
    }

    [Theory]
    [InlineData(MembershipPaymentMethods.VietQr)]
    [InlineData(MembershipPaymentMethods.Cash)]
    public async Task TransactionHistoryDisplaysJobExpirationAndReleasedVoucherWithoutCreatingInvoice(string method)
    {
        using var buyer = Client("buyer", "Customer");
        var purchase = await CreateAsync(buyer, Purchase([new(_basic)], method));
        await SeedCheckoutVoucherAsync();
        var applied = await ApplyCheckoutVoucherAsync(buyer, purchase);
        _clock.Now = applied.ExpiresAt.AddSeconds(1);
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));

        var detail = await TransactionDetailAsync(buyer, await TransactionOrderIdAsync(purchase));
        Assert.Equal("Expired", detail.Transaction.OrderStatus);
        Assert.Equal("Cancelled", detail.Transaction.PaymentStatus);
        Assert.Equal(method, detail.Transaction.PaymentMethod);
        Assert.Equal(500_000m, detail.Subtotal);
        Assert.Equal(100_000m, detail.DiscountAmount);
        Assert.Equal(400_000m, detail.Transaction.AmountDue);
        Assert.Equal(0m, detail.Transaction.AmountReceived);
        Assert.Equal("Released", detail.Voucher!.RedemptionStatus);
        Assert.Null(detail.Invoice);
        Assert.Null(detail.Transaction.InvoiceNumber);
        Assert.Null(detail.PaidAt);
    }

    [Fact]
    public async Task TransactionHistoryDisplaysCancelledSupersededRenewalWithoutImplyingPaymentSucceeded()
    {
        var original = await SeedMembershipAsync(Today.AddDays(3), MembershipStatus.Active);
        using var member = Client("member", "Member");
        var request = new RenewMembershipRequest(original.Id);
        var first = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute, request), HttpStatusCode.Created);
        var firstTime = _clock.Now.AddMinutes(-16);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            await db.Orders.Where(row => row.OrderNumber == first.OrderNumber)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, firstTime.UtcDateTime));
        }
        await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
        var replacement = await ReadAsync(await member.PostAsJsonAsync(RenewalsRoute, request), HttpStatusCode.Created);
        using var delayed = await NotifyAsync(first, 20_004, at: firstTime.AddMinutes(1));
        Assert.Equal(HttpStatusCode.OK, delayed.StatusCode);

        var detail = await TransactionDetailAsync(member, await TransactionOrderIdAsync(replacement));
        Assert.True(detail.Transaction.IsRenewal);
        Assert.Equal("Cancelled", detail.Transaction.OrderStatus);
        Assert.Equal("Cancelled", detail.Transaction.PaymentStatus);
        Assert.Equal(0m, detail.Transaction.AmountReceived);
        Assert.Null(detail.Invoice);
        Assert.Equal(3, (await ListTransactionsAsync(member)).TotalCount);
    }

    [Fact]
    public async Task TransactionHistoryAndInvoiceQueriesDoNotMutateOverduePendingOrSettledCommerceRecords()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        var paid = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        using var receipt = await staff.PostAsJsonAsync($"/api/staff/membership-purchases/{paid.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(paid.AmountDue));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var overdue = await CreateAsync(buyer, Purchase([new(_combo)]));
        await SeedCheckoutVoucherAsync();
        await ApplyCheckoutVoucherAsync(buyer, overdue);
        _clock.Now = overdue.ExpiresAt.AddDays(1);
        var before = await CommerceSnapshotAsync();

        for (var repeat = 0; repeat < 2; repeat++)
        {
            var page = await ListTransactionsAsync(buyer);
            Assert.Equal(2, page.TotalCount);
            foreach (var row in page.Items)
                await TransactionDetailAsync(buyer, row.OrderId);
        }
        Assert.Equal(before, await CommerceSnapshotAsync());
        var unchanged = await TransactionDetailAsync(buyer, await TransactionOrderIdAsync(overdue));
        Assert.Equal("Pending", unchanged.Transaction.OrderStatus);
        Assert.Equal("Pending", unchanged.Transaction.PaymentStatus);
        Assert.Equal("Reserved", unchanged.Voucher!.RedemptionStatus);
        Assert.Null(unchanged.Invoice);
    }

    private static async Task<PagedResponse<MembershipTransactionSummaryResponse>> ListTransactionsAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResponse<MembershipTransactionSummaryResponse>>(TransactionsRoute + query))!;

    private static async Task<MembershipTransactionDetailResponse> TransactionDetailAsync(HttpClient client, int orderId) =>
        (await client.GetFromJsonAsync<MembershipTransactionDetailResponse>($"{TransactionsRoute}/{orderId}"))!;

    private async Task<int> TransactionOrderIdAsync(MembershipPurchaseResponse purchase)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Orders
            .Where(row => row.OrderNumber == purchase.OrderNumber).Select(row => row.Id).SingleAsync();
    }

    private async Task<int> InternalOrderIdAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Orders
            .Where(row => row.OrderNumber == "INTERNAL-NO-PAYMENT").Select(row => row.Id).SingleAsync();
    }

    private async Task<string> CommerceSnapshotAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        return JsonSerializer.Serialize(new
        {
            Orders = await db.Orders.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.OrderStatus, row.CompletedAt, row.CreatedAt, row.UpdatedAt }).ToListAsync(),
            Items = await db.OrderItems.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.PackageNameSnapshot, row.UnitPriceSnapshot, row.ComponentTermsSnapshot, row.ActivationStartDate, row.UpdatedAt }).ToListAsync(),
            Payments = await db.PaymentTransactions.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.PaymentStatus, row.PaymentMethod, row.AmountDue, row.AmountReceived, row.PaidAt, row.UpdatedAt }).ToListAsync(),
            Redemptions = await db.VoucherRedemptions.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.RedemptionStatus, row.DiscountAmount, row.VoucherCodeSnapshot, row.VoucherNameSnapshot, row.UpdatedAt }).ToListAsync(),
            Vouchers = await db.Vouchers.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.VoucherStatus, row.UpdatedAt }).ToListAsync(),
            Memberships = await db.MemberMemberships.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.MembershipStatus, row.StartDate, row.EndDate, row.UpdatedAt }).ToListAsync(),
            Invoices = await db.Invoices.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.InvoiceNumber, row.IssuedAt, row.InvoiceStatus, row.UpdatedAt }).ToListAsync(),
            Audits = await db.AuditLogs.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.ActorUserId, row.CreatedAt, row.UpdatedAt }).ToListAsync(),
            Roles = await db.ApplicationUserRoles.AsNoTracking().OrderBy(row => row.UserId).ThenBy(row => row.RoleId)
                .Select(row => new { row.UserId, row.RoleId }).ToListAsync()
        });
    }
}
