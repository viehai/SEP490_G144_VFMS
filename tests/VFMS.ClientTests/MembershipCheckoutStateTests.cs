using System.Net;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Fact]
    public async Task Checkout_DuplicateApplyAndConcurrentRefreshSendOnlyOneMutation()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = 0;
        var gets = 0;
        using var api = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Get) { gets++; return Task.FromResult(Json(VoucherPurchase(0))); }
            posts++;
            return pending.Task;
        }, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        var apply = state.ApplyVoucherAsync("SAVE20");

        Assert.True(state.IsApplying);
        Assert.True(state.IsBusy);
        await state.ApplyVoucherAsync("SAVE20");
        await state.RefreshAsync();
        Assert.Equal(1, posts);
        Assert.Equal(1, gets);

        pending.SetResult(Json(VoucherPurchase()));
        await apply;

        Assert.False(state.IsBusy);
        Assert.Equal(400_000m, state.Purchase?.AmountDue);
        Assert.NotNull(state.VoucherNotice);
    }

    [Fact]
    public async Task Checkout_PollInFlightBlocksApplyUntilItsResponseHasBeenAccepted()
    {
        var refresh = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var api = CreateClient(request =>
        {
            calls++;
            return calls == 1 ? refresh.Task : Task.FromResult(Json(VoucherPurchase()));
        }, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        var read = state.RefreshAsync();
        await state.ApplyVoucherAsync("SAVE20");
        Assert.Equal(1, calls);
        refresh.SetResult(Json(VoucherPurchase(0)));
        await read;
        await state.ApplyVoucherAsync("SAVE20");
        Assert.Equal(2, calls);
        Assert.Equal(400_000m, state.Purchase?.AmountDue);
    }

    [Fact]
    public async Task Checkout_LeavingOrderIgnoresLateMutationResponse()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var api = CreateClient(request => request.Method == HttpMethod.Get
            ? Task.FromResult(Json(VoucherPurchase(0))) : pending.Task, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        var apply = state.ApplyVoucherAsync("SAVE20");
        state.Dispose();
        pending.SetResult(Json(VoucherPurchase()));
        await apply;
        Assert.Equal(500_000m, state.Purchase?.AmountDue);
        Assert.Null(state.VoucherNotice);
    }

    [Theory]
    [InlineData("Paid", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Pending", true)]
    public async Task Checkout_PaidCancelledAndExpiredOrdersCannotApply(string status, bool expired)
    {
        var calls = 0;
        var response = VoucherPurchase(0) with { OrderStatus = status, ExpiresAt = expired ? DateTimeOffset.UtcNow.AddSeconds(-5) : DateTimeOffset.UtcNow.AddMinutes(10) };
        using var api = CreateClient(_ => { calls++; return Task.FromResult(Json(response)); }, role: "Member");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync("SAVE20");
        Assert.Equal(1, calls);
        Assert.False(state.CanManageVoucher);
        Assert.NotNull(state.VoucherError);
    }

    [Fact]
    public async Task Checkout_BlankCodeCannotSendMutation()
    {
        var calls = 0;
        using var api = CreateClient(_ => { calls++; return Task.FromResult(Json(VoucherPurchase(0))); }, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync("   ");
        Assert.Equal(1, calls);
        Assert.Contains("nhập mã", state.VoucherError);
    }

    [Fact]
    public async Task Checkout_EmptyOrderCannotSendVoucherMutation()
    {
        var calls = 0;
        using var api = CreateClient(_ => { calls++; return Task.FromResult(Json(VoucherPurchase(0) with { Items = [] })); }, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync("SAVE20");
        Assert.Equal(1, calls);
        Assert.False(state.CanManageVoucher);
        Assert.NotNull(state.VoucherError);
    }

    [Fact]
    public async Task Checkout_RequestValidationErrorRemainsSpecificAndKeepsTotals()
    {
        using var api = CreateClient(request => Task.FromResult(request.Method == HttpMethod.Get ? Json(VoucherPurchase(0))
            : Json(new { errors = new { VoucherCode = new[] { "Mã giảm giá không được vượt quá 50 ký tự." } } }, HttpStatusCode.BadRequest)), role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync(new string('X', 51));
        Assert.Equal("Mã giảm giá không được vượt quá 50 ký tự.", state.VoucherError);
        Assert.Equal(500_000m, state.Purchase?.AmountDue);
    }

    [Fact]
    public async Task Checkout_InvalidVoucherKeepsOriginalFinancialStateAndShowsSpecificError()
    {
        using var api = CreateClient(request => Task.FromResult(request.Method == HttpMethod.Get
            ? Json(VoucherPurchase(0)) : Json(new { code = "MSG-22", detail = "Đơn hàng chưa đạt giá trị tối thiểu." }, HttpStatusCode.BadRequest)), role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync("SAVE20");
        Assert.Equal(500_000m, state.Purchase?.AmountDue);
        Assert.Null(state.Purchase?.AppliedVoucher);
        Assert.Null(state.VoucherNotice);
        Assert.Equal("Đơn hàng chưa đạt giá trị tối thiểu.", state.VoucherError);
    }

    [Fact]
    public async Task Checkout_RemoveRestoresFullPriceAndAllowsReplacement()
    {
        using var api = CreateClient(request => Task.FromResult(Json(request.Method == HttpMethod.Delete ? VoucherPurchase(0) : VoucherPurchase())), role: "Member");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.RemoveVoucherAsync();
        Assert.Equal(500_000m, state.Purchase?.AmountDue);
        Assert.Null(state.Purchase?.AppliedVoucher);
        await state.ApplyVoucherAsync("NEXT");
        Assert.Equal(400_000m, state.Purchase?.AmountDue);
    }

    [Fact]
    public async Task Checkout_FullDiscountStaysPendingUntilExplicitFreeOrderCompletion()
    {
        var paid = VoucherPurchase(500_000) with { OrderStatus = "Paid", PaymentStatus = "Paid", PaymentMethod = "NoCharge",
            AppliedVoucher = new("FREE", "Free campaign", 500_000m, "Confirmed") };
        using var api = CreateClient(request => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("complete-no-charge", StringComparison.Ordinal)
            ? paid : request.Method == HttpMethod.Get ? VoucherPurchase(0) : VoucherPurchase(500_000))), role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.ApplyVoucherAsync("FREE");
        Assert.Equal("Pending", state.Purchase?.OrderStatus);
        Assert.Equal("Reserved", state.Purchase?.AppliedVoucher?.RedemptionStatus);
        Assert.Equal(0, state.Purchase?.AmountDue);
        Assert.Null(state.Purchase?.VietQr);
        await state.CompleteNoChargeAsync();
        Assert.Equal("Paid", state.Purchase?.OrderStatus);
        Assert.Equal("Confirmed", state.Purchase?.AppliedVoucher?.RedemptionStatus);
    }

    [Fact]
    public async Task Checkout_NonzeroOrderCannotUseFreeCompletion()
    {
        var calls = 0;
        using var api = CreateClient(_ => { calls++; return Task.FromResult(Json(VoucherPurchase(0))); }, role: "Customer");
        using var state = new MembershipCheckoutState(api, "ORD-TEST");
        await state.RefreshAsync();
        await state.CompleteNoChargeAsync();
        Assert.Equal(1, calls);
        Assert.Equal("Pending", state.Purchase?.OrderStatus);
    }
}
