using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Pages.Member;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    private const string DisabledFreezeSubmit = "<button[^>]*type=\"submit\"[^>]*\\sdisabled(?=[\\s=>])";

    [Fact]
    public async Task FreezeForm_PresentsServerMaximumAndDisablesEmptySubmission()
    {
        var html = await RenderFreezeFormAsync(DateRangeEligibility());

        Assert.Contains("20 ngày bảo lưu", html);
        Assert.Contains("từ 1 đến 20 ngày liên tiếp", html);
        Assert.Contains("Từ ngày <span", html);
        Assert.Contains("Đến hết ngày <span", html);
        Assert.Contains("Lý do bảo lưu <span", html);
        Assert.Contains("Tài liệu minh chứng", html);
        Assert.DoesNotContain("không bắt buộc", html);
        Assert.Matches("<input[^>]*id=\"freeze-7-0-document\"[^>]* required", html);
        Assert.Contains("accept=\".pdf,.png,.jpg,.jpeg\"", html);
        Assert.Contains("min=\"2026-11-26\" max=\"2026-12-20\"", html);
        Assert.Matches(DisabledFreezeSubmit, html);
        Assert.DoesNotContain("Thông tin bảo lưu dự kiến", html);
    }

    [Fact]
    public async Task FreezeForm_DistinguishesRemainingAllowanceFromMaximumPerRequest()
    {
        var html = await RenderFreezeFormAsync(DateRangeEligibility() with
        {
            GrantedFreezeAllowanceDays = 120, RemainingQuota = 120, MaximumAvailableDays = 90,
            Policy = new(1, 30, 90, 1, 1, null)
        });

        Assert.Contains("120 ngày bảo lưu", html);
        Assert.Contains("từ 30 đến 90 ngày liên tiếp", html);
        Assert.Contains("Nếu được duyệt 90 ngày, bạn sẽ còn 30 ngày bảo lưu.", html);
    }

    [Theory]
    [InlineData(1, "27/11/2026", "21/12/2026", 19)]
    [InlineData(10, "06/12/2026", "30/12/2026", 10)]
    public async Task FreezeForm_PrefillsSavedInclusiveRangeAndPreviewsEntitlement(int days, string returnText,
        string expiryText, int remaining)
    {
        var start = new DateOnly(2026, 11, 26);
        var edit = PendingFreezeRequest() with { StartDate = start, EndDate = start.AddDays(days - 1), RequestedDays = days };
        var html = await RenderFreezeFormAsync(DateRangeEligibility(), edit);

        Assert.Contains($"value=\"{edit.EndDate:yyyy-MM-dd}\"", html);
        Assert.Contains("min=\"2026-11-26\" max=\"2026-12-15\"", html);
        Assert.Contains("Thông tin bảo lưu dự kiến", html);
        Assert.Contains($"+{days} ngày", html);
        Assert.Contains(returnText, html);
        Assert.Contains(expiryText, html);
        Assert.Contains($"{remaining} ngày", html);
        Assert.DoesNotMatch(DisabledFreezeSubmit, html);
        Assert.Contains("Lưu thay đổi", html);
    }

    [Fact]
    public async Task FreezeForm_CapsInclusiveEndBeforeAnotherRequestAndBlocksOverlap()
    {
        var start = new DateOnly(2026, 11, 26);
        var edit = PendingFreezeRequest() with { StartDate = start, EndDate = start.AddDays(4), RequestedDays = 5 };
        var policy = DateRangeEligibility() with
        {
            OverlappingPeriods = [new(42, start.AddDays(4), start.AddDays(7), "Approved")]
        };
        var html = await RenderFreezeFormAsync(policy, edit);

        Assert.Contains("min=\"2026-11-26\" max=\"2026-11-29\"", html);
        Assert.Contains("29/11/2026", html);
        Assert.Contains("yêu cầu đã duyệt", html);
        Assert.Matches(DisabledFreezeSubmit, html);
        Assert.DoesNotContain("Thông tin bảo lưu dự kiến", html);
    }

    [Fact]
    public async Task FreezeForm_ExplainsIneligibilityAndOmitsControls()
    {
        var html = await RenderFreezeFormAsync(DateRangeEligibility() with
        {
            Eligible = false,
            Message = "Bạn còn 3 ngày bảo lưu, nhưng mỗi yêu cầu cần ít nhất 7 ngày."
        });

        Assert.Contains("Bạn còn 3 ngày bảo lưu, nhưng mỗi yêu cầu cần ít nhất 7 ngày.", html);
        Assert.DoesNotContain("type=\"submit\"", html);
        Assert.DoesNotContain("Tài liệu minh chứng", html);
    }

    [Fact]
    public async Task FreezeEntitlement_DisplaysCompletedAndCommittedDaysSeparately()
    {
        var freeze = new MembershipFreezeSummaryResponse(13, 2, [], 20, 4, 13, 7);
        var html = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MembershipFreezeEntitlement>(freeze,
            parameters: new() { [nameof(MembershipFreezeEntitlement.Freeze)] = freeze }));

        Assert.Contains("Tổng số ngày được bảo lưu", html);
        Assert.Contains("20 ngày", html);
        Assert.Contains("4 ngày", html);
        Assert.Contains("9 ngày đã được duyệt hoặc đang bảo lưu.", html);
        Assert.Contains("7 ngày", html);
        Assert.Contains("Số lần bảo lưu: 2", html);
        Assert.Contains("value=\"13\" max=\"20\"", html);
    }

    [Fact]
    public async Task GymFreezeEntitlementShowsRemainingVisitsAndTheFullLimitForEachVisit()
    {
        var freeze = new MembershipFreezeSummaryResponse(120, 1, [], GrantedFreezeCount: 2,
            GrantedMinFreezeDays: 30, GrantedMaxFreezeDays: 120);
        var html = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MembershipFreezeEntitlement>(freeze,
            parameters: new() { [nameof(MembershipFreezeEntitlement.Freeze)] = freeze }));

        Assert.Contains("Còn 1 / 2 lượt bảo lưu", html);
        Assert.Contains("Mỗi lượt từ 30 đến 120 ngày liên tiếp.", html);
        Assert.DoesNotContain("Tổng số ngày được bảo lưu", html);
    }

    [Fact]
    public async Task FreezeHistory_PrioritizesActiveAndPendingAndDisplaysHistoricalDetails()
    {
        var pending = PendingFreezeRequest() with { FreezeRequestId = 10, PlanName = "Gói chờ duyệt" };
        var active = pending with { FreezeRequestId = 11, PlanName = "Gói đang bảo lưu", Status = "Active", CanEdit = false,
            CanCancel = false, CanRequestEarlyUnfreeze = true };
        var completed = pending with { FreezeRequestId = 12, PlanName = "Gói kết thúc sớm", Status = "Completed",
            CanEdit = false, CanCancel = false, ActualEndDate = new DateOnly(2026, 12, 5) };
        var rejected = pending with { FreezeRequestId = 13, PlanName = "Gói đã từ chối", Status = "Rejected", ReviewNote = "Trùng yêu cầu khác.",
            CanEdit = false, CanCancel = false, SubmittedAt = new DateTimeOffset(2026, 12, 2, 20, 0, 0, TimeSpan.Zero) };
        var html = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MyFreezeRequests>(
            new[] { rejected, completed, active, pending }, role: "Member"));

        Assert.Contains("Lọc yêu cầu theo trạng thái", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("Yêu cầu kết thúc bảo lưu sớm", html);
        Assert.Contains("Chỉnh sửa", html);
        Assert.Contains("Hủy yêu cầu", html);
        Assert.Contains("Lý do từ chối", html);
        Assert.Contains("Trùng yêu cầu khác.", html);
        Assert.Contains("Ngày kết thúc thực tế", html);
        Assert.Contains("05/12/2026", html);
        Assert.Contains("03/12/2026", html);
        Assert.True(html.IndexOf(active.PlanName, StringComparison.Ordinal) < html.IndexOf(pending.PlanName, StringComparison.Ordinal));
        Assert.True(html.IndexOf(pending.PlanName, StringComparison.Ordinal) < html.IndexOf(rejected.PlanName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FreezeHistory_DistinguishesEmptyAndFailedLoads()
    {
        var empty = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MyFreezeRequests>(
            Array.Empty<MembershipFreezeRequestResponse>(), role: "Member"));
        var failed = WebUtility.HtmlDecode(await RenderTransactionPageAsync<MyFreezeRequests>(
            new { detail = "Không thể tải yêu cầu bảo lưu." }, HttpStatusCode.ServiceUnavailable, role: "Member"));

        Assert.Contains("Bạn chưa có yêu cầu bảo lưu nào.", empty);
        Assert.DoesNotContain("Lọc yêu cầu theo trạng thái", empty);
        Assert.Contains("role=\"alert\"", failed);
        Assert.Contains("Hệ thống tạm thời chưa sẵn sàng. Vui lòng thử lại sau.", failed);
        Assert.Contains("Thử lại", failed);
    }

    private static MembershipFreezeEligibilityResponse DateRangeEligibility() =>
        new(FrozenMembership() with { Status = "Active", EndDate = new DateOnly(2026, 12, 20) },
            true, null, null, new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 26), 20, 20,
            new(1, 1, 60, 1, 20, null), MaximumAvailableDays: 20, LatestStartDate: new DateOnly(2026, 12, 20));

    private static async Task<string> RenderFreezeFormAsync(MembershipFreezeEligibilityResponse eligibility,
        MembershipFreezeRequestResponse? existing = null)
    {
        using var api = CreateClient(_ => throw new InvalidOperationException("Rendering inputs must not send a request."), role: "Member");
        await using var services = TransactionPageServices(api);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(MembershipFreezeRequestForm.Eligibility)] = eligibility,
            [nameof(MembershipFreezeRequestForm.ExistingRequest)] = existing
        });
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MembershipFreezeRequestForm>(parameters)).ToHtmlString()));
    }
}
