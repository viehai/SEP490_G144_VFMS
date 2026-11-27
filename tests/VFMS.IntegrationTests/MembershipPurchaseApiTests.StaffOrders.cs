using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    private const string StaffPackageOrdersRoute = "/api/staff/membership-purchases";

    [Fact]
    public async Task StaffListOrdersUsesGymScopeAndPutsPendingBeforePagingWithStatusAndTypeFilters()
    {
        using var buyer = Client("buyer", "Customer");
        using var staff = Client("staff", "Staff");
        using var otherGym = Client("other-staff", "Staff");
        var cash = await CreateAsync(buyer, Purchase([new(_basic)], MembershipPaymentMethods.Cash));
        var paidCombo = await CreateAsync(buyer, Purchase([new(_combo)]));
        using var confirmation = await NotifyAsync(paidCombo, 880);
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var overdue = await CreateAsync(buyer, Purchase([new(_basic)]));

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var stored = await db.Orders.SingleAsync(order => order.OrderNumber == overdue.OrderNumber);
            stored.CreatedAt = _clock.Now.UtcDateTime.AddDays(-2);
            db.Orders.Add(new Order
            {
                PurchaserUserId = "buyer", OrderNumber = "TEST-PAYMENT-WITHOUT-PACKAGE", Channel = OrderChannel.Online,
                PaymentTransaction = new PaymentTransaction { GatewayName = "SePay", AmountDue = 100_000m }
            });
            await db.SaveChangesAsync();
        }

        using var forbidden = await buyer.GetAsync(StaffPackageOrdersRoute);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty((await StaffOrdersAsync(otherGym)).Items);

        var first = await StaffOrdersAsync(staff, "?pageSize=1");
        Assert.Equal(3, first.TotalCount);
        var firstOrder = Assert.Single(first.Items);
        Assert.Equal(cash.OrderNumber, firstOrder.OrderNumber);
        Assert.Equal(("Pending", "Pending", "Cash", true), (firstOrder.Status, firstOrder.PaymentStatus, firstOrder.Method, firstOrder.CanConfirmCash));
        Assert.Equal(cash.AmountDue, firstOrder.AmountDue);
        Assert.Equal(cash.ExpiresAt, firstOrder.PaymentDeadline);

        var second = Assert.Single((await StaffOrdersAsync(staff, "?page=2&pageSize=1")).Items);
        Assert.Equal(paidCombo.OrderNumber, second.OrderNumber);
        Assert.Equal(["GYM", "YOGA"], second.ServiceTypes);
        Assert.Equal(("Paid", "Paid", "VietQR", false), (second.Status, second.PaymentStatus, second.Method, second.CanConfirmCash));
        Assert.NotNull(second.InvoiceNumber);

        var day = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var yoga = Assert.Single((await StaffOrdersAsync(staff,
            $"?search=comBo&serviceType=YOGA&status=Paid&method=VietQR&paymentStatus=Paid&from={day}&to={day}")).Items);
        Assert.Equal(second.OrderNumber, yoga.OrderNumber);
        Assert.Equal("Combo", Assert.Single(yoga.Items).Name);

        var expired = Assert.Single((await StaffOrdersAsync(staff, "?status=Expired&paymentStatus=Cancelled")).Items);
        Assert.Equal(overdue.OrderNumber, expired.OrderNumber);
        Assert.False(expired.CanConfirmCash);
        await using (var verify = _app.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(OrderStatus.Pending, (await db.Orders.SingleAsync(order => order.OrderNumber == overdue.OrderNumber)).OrderStatus);
        }

        using var paid = await staff.PostAsJsonAsync($"{StaffPackageOrdersRoute}/{cash.OrderNumber}/confirm-cash",
            new ConfirmMembershipCashRequest(cash.AmountDue));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var updated = Assert.Single((await StaffOrdersAsync(staff, $"?search={cash.OrderNumber}&status=Paid&method=Cash")).Items);
        Assert.False(updated.CanConfirmCash);
        Assert.Equal(cash.AmountDue, updated.AmountReceived);
        Assert.NotNull(updated.InvoiceNumber);

        using var invalid = await staff.GetAsync($"{StaffPackageOrdersRoute}?from={day}&to=2000-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<PagedResponse<StaffPackageOrderResponse>> StaffOrdersAsync(HttpClient staff, string query = "")
    {
        using var response = await staff.GetAsync(StaffPackageOrdersRoute + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffPackageOrderResponse>>())!;
    }
}
