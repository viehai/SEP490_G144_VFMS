using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData("Customer", 50_000)]
    [InlineData("Member", 100_000)]
    public async Task ApplyVoucher_SendsOnlyCodeAndReadsAuthoritativeUpdatedSummary(string role, int discount)
    {
        var expected = VoucherPurchase(discount);
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/membership-purchases/ORD%2F%20%3F/voucher", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            var code = Assert.Single(body.EnumerateObject());
            Assert.Equal("voucherCode", code.Name);
            Assert.Equal(" save20 ", code.Value.GetString());
            Assert.False(body.TryGetProperty("discountAmount", out _));
            Assert.False(body.TryGetProperty("subtotal", out _));
            Assert.False(body.TryGetProperty("amountDue", out _));
            Assert.False(body.TryGetProperty("userId", out _));
            return Json(expected);
        }, role: role);

        var summary = await api.ApplyMembershipVoucherAsync("ORD/ ?", new(" save20 "));

        Assert.Equal(expected with { Items = summary.Items }, summary);
        Assert.Equal(expected.Items.ToArray(), summary.Items.ToArray());
        Assert.Equal(500_000m, summary.Subtotal);
        Assert.Equal(discount, summary.DiscountAmount);
        Assert.Equal(500_000m - discount, summary.AmountDue);
        Assert.Equal("SAVE20", summary.AppliedVoucher?.Code);
        Assert.Equal("Reserved", summary.AppliedVoucher?.RedemptionStatus);
        Assert.Equal(discount, summary.AppliedVoucher?.DiscountAmount);
        Assert.Equal("Pending", summary.OrderStatus);
        Assert.Equal(expected.VietQr, summary.VietQr);
    }

    [Theory]
    [InlineData("VOUCHER_NOT_FOUND", "Không tìm thấy mã giảm giá.")]
    [InlineData("VOUCHER_DISABLED", "Mã giảm giá đã bị tắt.")]
    [InlineData("VOUCHER_SCHEDULED", "Mã giảm giá chưa bắt đầu.")]
    [InlineData("VOUCHER_EXPIRED", "Mã giảm giá đã hết hạn.")]
    [InlineData("VOUCHER_QUOTA_EXHAUSTED", "Mã giảm giá đã hết lượt sử dụng.")]
    [InlineData("VOUCHER_CAPACITY_UNAVAILABLE", "Các lượt còn lại đang được giữ cho đơn hàng khác.")]
    [InlineData("VOUCHER_ALREADY_APPLIED", "Mỗi đơn hàng chỉ được áp dụng một mã giảm giá.")]
    [InlineData("MSG-22", "Đơn hàng chưa đạt giá trị tối thiểu.")]
    [InlineData("ORDER_NOT_PENDING", "Đơn hàng không còn chờ thanh toán.")]
    public async Task ApplyVoucher_PreservesSpecificBusinessError(string code, string detail)
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new { code, detail }, HttpStatusCode.BadRequest)), role: "Customer");

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ApplyMembershipVoucherAsync("ORD-TEST", new("SAVE20")));

        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(detail, error.Message);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
    }

    [Fact]
    public async Task RemoveVoucher_UsesDeleteAndReadsRestoredFullPriceSummary()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/membership-purchases/ORD-TEST/voucher", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(VoucherPurchase(0)));
        }, role: "Customer");

        var summary = await api.RemoveMembershipVoucherAsync("ORD-TEST");

        Assert.Null(summary.AppliedVoucher);
        Assert.Equal(0, summary.DiscountAmount);
        Assert.Equal(500_000m, summary.AmountDue);
    }

    [Fact]
    public async Task CompleteNoCharge_SendsExplicitRequestWithoutPaymentOrFinancialClaims()
    {
        var paid = VoucherPurchase(500_000) with
        {
            OrderStatus = "Paid", PaymentStatus = "Paid", PaymentMethod = "NoCharge", VietQr = null,
            AppliedVoucher = new("FREE", "Free campaign", 500_000m, "Confirmed")
        };
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/membership-purchases/ORD-TEST/complete-no-charge", request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(paid));
        }, role: "Member");

        var result = await api.CompleteNoChargeMembershipPurchaseAsync("ORD-TEST");

        Assert.Equal("Paid", result.OrderStatus);
        Assert.Equal("NoCharge", result.PaymentMethod);
        Assert.Equal("Confirmed", result.AppliedVoucher?.RedemptionStatus);
    }

    [Theory]
    [InlineData("subtotal")]
    [InlineData("amountDue")]
    [InlineData("discountAmount")]
    [InlineData("appliedVoucher")]
    public async Task GetPurchase_RejectsMissingFinancialFieldsInsteadOfSilentDefaults(string missing)
    {
        var body = JsonSerializer.SerializeToElement(VoucherPurchase(100_000), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .EnumerateObject().Where(property => property.Name != missing).ToDictionary(property => property.Name, property => property.Value);
        using var api = CreateClient(_ => Task.FromResult(Json(body)), role: "Customer");
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.GetPurchaseAsync("ORD-TEST"));
        Assert.Contains("không hợp lệ", error.Message);
    }

    [Fact]
    public async Task ApplyVoucher_RejectsIncompleteAppliedVoucherSnapshot()
    {
        var body = JsonSerializer.SerializeToElement(VoucherPurchase(100_000), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value);
        body["appliedVoucher"] = new { code = "SAVE20", name = "Incomplete", discountAmount = 100_000m };
        using var api = CreateClient(_ => Task.FromResult(Json(body)), role: "Customer");
        await Assert.ThrowsAsync<AuthApiException>(() => api.ApplyMembershipVoucherAsync("ORD-TEST", new("SAVE20")));
    }

    [Fact]
    public async Task AnonymousVoucherApply_DoesNotSendRequest()
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Unexpected request"), authenticated: false);
        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.ApplyMembershipVoucherAsync("ORD-TEST", new("SAVE20")));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    private static MembershipPurchaseResponse VoucherPurchase(decimal discount = 100_000m)
    {
        var now = DateTimeOffset.UtcNow;
        return new("ORD-TEST", "Pending", "Pending", "VietQR", now, now.AddMinutes(15), now,
            500_000m, 500_000m - discount, [new(7, "Gym monthly", true, null, 500_000m)],
            discount == 500_000m ? null : new("https://qr.example.test/discounted", "BANK", "1234567890", "VICTOR FITNESS", "ORDTEST"),
            null, null, null, discount,
            discount == 0 ? null : new("SAVE20", "November promotion", discount, "Reserved"));
    }
}
