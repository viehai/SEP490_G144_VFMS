using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Pages.Manager;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Fact]
    public async Task ManagerPolicy_RestoresMonthInputAndDisplaysCanonicalDays()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(new MembershipFreezePolicyResponse(
            new(1, 1, 60, 1, 20, null, DefaultMaxFreezeCount: 0, MaximumFreezeDaysUnit: MembershipDurationUnit.Month)))));

        var html = await RenderManagerPageAsync<MembershipFreezePolicy>(api);

        Assert.Contains("Điều kiện bảo lưu", html);
        Assert.Contains("Giới hạn mỗi lần bảo lưu", html);
        Assert.Contains("Quyền lợi bảo lưu mặc định", html);
        Assert.Contains("2 tháng tương đương 60 ngày bảo lưu.", html);
        Assert.Contains("freeze-default-count", html);
        Assert.DoesNotContain("freeze-default-days", html);
        Assert.DoesNotContain("Thêm hạn ngạch", html);
    }

    [Fact]
    public async Task ManagerPolicy_DistinguishesUnconfiguredPolicyAndOffersRetryOnApiError()
    {
        using var empty = CreateClient(_ => Task.FromResult(Json(new MembershipFreezePolicyResponse(null))));
        using var failed = CreateClient(_ => Task.FromResult(Json(new { detail = "Chưa tải được chính sách." }, HttpStatusCode.ServiceUnavailable)));

        Assert.Contains("Chưa thiết lập quyền lợi mặc định.", await RenderManagerPageAsync<MembershipFreezePolicy>(empty));
        var error = await RenderManagerPageAsync<MembershipFreezePolicy>(failed);
        Assert.Contains("Hệ thống tạm thời chưa sẵn sàng.", error);
        Assert.Contains("Tải lại", error);
        Assert.DoesNotContain("freeze-default-days", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ManagerPlan_CreateSendsSelectedDurationAndDistinguishesBlankFromZero(int? freezeCount)
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, body.GetProperty("durationValue").GetInt32());
            Assert.Equal((int)MembershipDurationUnit.Month, body.GetProperty("durationUnit").GetInt32());
            var count = body.GetProperty("maxFreezeCount");
            if (freezeCount is { } value)
                Assert.Equal(value, count.GetInt32());
            else
                Assert.Equal(JsonValueKind.Null, count.ValueKind);
            Assert.False(body.TryGetProperty("freezeAllowanceOverrideValue", out _));
            Assert.Equal("Tập thường xuyên", body.GetProperty("targetAudience").GetString());
            return Json(new MembershipPlanResponse(1, "Gym 3 tháng", null, null, 500000, MembershipPlanStatuses.Active,
                0, 3)
            {
                MaxFreezeCount = freezeCount
            });
        });

        var saved = await api.CreateAsync(new()
        {
            Name = "Gym 3 tháng",
            TargetAudience = "Tập thường xuyên",
            DurationValue = 3,
            DurationUnit = MembershipDurationUnit.Month,
            MaxFreezeCount = freezeCount,
            Price = 500000,
            Status = MembershipPlanStatuses.Active
        });

        Assert.Equal(freezeCount, saved.MaxFreezeCount);
        Assert.Equal(3, saved.DurationValue);
        Assert.Equal(MembershipDurationUnit.Month, saved.DurationUnit);
    }

    [Fact]
    public async Task ManagerLists_RequestTenRowsPerPage()
    {
        using var plans = CreateClient(request =>
        {
            Assert.Equal("/api/manager/membership-plans?page=1&pageSize=10&status=ACTIVE", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(new PagedResponse<MembershipPlanResponse>(1, 10, 0, [])));
        }, role: "Manager");
        using var reviews = CreateClient(request =>
        {
            Assert.Equal("/api/manager/membership-freeze-requests?page=1&pageSize=10", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(new PagedResponse<MembershipFreezeReviewSummaryResponse>(1, 10, 0, [])));
        }, role: "Manager");

        await RenderManagerPageAsync<MembershipPlans>(plans);
        await RenderManagerPageAsync<MembershipFreezeReviews>(reviews);
    }

    [Fact]
    public async Task ManagerPlans_ListSendsDurationAndFreezeFiltersWithExistingSearch()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal("/api/manager/membership-plans?page=1&pageSize=20&search=Gym%203&status=ACTIVE&serviceType=GYM&durationUnit=Month&freezeAllowanceFilter=Disabled",
                request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(new PagedResponse<MembershipPlanResponse>(1, 20, 0, [])));
        });

        await api.ListAsync(new()
        {
            Search = "Gym 3",
            Status = MembershipPlanStatuses.Active,
            ServiceType = MembershipPlanServiceTypes.Gym,
            DurationUnit = MembershipDurationUnit.Month,
            FreezeAllowanceFilter = MembershipFreezeAllowanceFilter.Disabled
        });
    }

    private static async Task<string> RenderManagerPageAsync<TPage>(MembershipPlanApiClient api) where TPage : IComponent
    {
        await using var provider = TransactionPageServices(api);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync<TPage>()).ToHtmlString()));
    }
}
