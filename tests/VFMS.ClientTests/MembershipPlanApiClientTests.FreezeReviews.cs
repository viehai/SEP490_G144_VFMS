using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(MembershipFreezeReviewTypes.Freeze, "&type=Freeze")]
    [InlineData(MembershipFreezeReviewTypes.EarlyUnfreeze, "&type=EarlyUnfreeze")]
    public async Task FreezeReviewQueue_SendsManagerSessionAndPaging(string? type, string typeQuery)
    {
        var summary = FreezeReviewDetail().Request;
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/manager/membership-freeze-requests?page=2&pageSize=10" + typeQuery,
                request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(new PagedResponse<MembershipFreezeReviewSummaryResponse>(2, 10, 11, [summary])));
        });

        var result = await api.ListFreezeReviewsAsync(new() { Type = type, Page = 2, PageSize = 10 });

        Assert.Equal(11, result.TotalCount);
        Assert.Equal(summary, Assert.Single(result.Items));
    }

    [Theory]
    [InlineData(MembershipFreezeReviewTypes.Freeze)]
    [InlineData(MembershipFreezeReviewTypes.EarlyUnfreeze)]
    public async Task FreezeReviewDetail_ReadsMembershipQuotaAndRequestTimePolicy(string type)
    {
        var expected = FreezeReviewDetail(type);
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/manager/membership-freeze-requests/9?type=" + type, request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(expected));
        });

        var result = await api.GetFreezeReviewAsync(9, type);

        Assert.Equal(expected.Request, result.Request);
        Assert.Equal(expected.Membership, result.Membership);
        Assert.Equal(expected.RemainingQuota, result.RemainingQuota);
        Assert.Equal(expected.AppliedPolicy!.Policy, result.AppliedPolicy!.Policy);
        Assert.Equal(expected.AppliedPolicy.Quotas, result.AppliedPolicy.Quotas);
        Assert.Equal(expected.ActualFrozenDays, result.ActualFrozenDays);
        Assert.True(result.CanApprove);
        Assert.True(result.CanReject);
    }

    [Theory]
    [InlineData(MembershipFreezeReviewTypes.Freeze, "/approve")]
    [InlineData(MembershipFreezeReviewTypes.EarlyUnfreeze, "/early-unfreeze/approve")]
    public async Task ApproveFreezeReview_SendsBodylessDecisionAndReadsFinalState(string type, string actionPath)
    {
        var expected = FreezeReviewDetail(type) with { CanApprove = false, CanReject = false };
        expected = expected with { Request = expected.Request with { Status = "Approved" } };
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/manager/membership-freeze-requests/9" + actionPath, request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(expected));
        });

        var result = await api.ApproveFreezeReviewAsync(9, type);

        Assert.Equal("Approved", result.Request.Status);
        Assert.False(result.CanApprove);
        Assert.False(result.CanReject);
    }

    [Theory]
    [InlineData(MembershipFreezeReviewTypes.Freeze, "/reject")]
    [InlineData(MembershipFreezeReviewTypes.EarlyUnfreeze, "/early-unfreeze/reject")]
    public async Task RejectFreezeReview_SendsOnlyManagerReason(string type, string actionPath)
    {
        var expected = FreezeReviewDetail(type) with { CanApprove = false, CanReject = false };
        expected = expected with { Request = expected.Request with { Status = "Rejected" } };
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/manager/membership-freeze-requests/9" + actionPath, request.RequestUri!.PathAndQuery);
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            var field = Assert.Single(body.EnumerateObject());
            Assert.Equal("reason", field.Name);
            Assert.Equal("Insufficient evidence", field.Value.GetString());
            return Json(expected);
        });

        var result = await api.RejectFreezeReviewAsync(9, type, new("Insufficient evidence"));

        Assert.Equal("Rejected", result.Request.Status);
    }

    [Fact]
    public async Task RejectFreezeReview_PreservesReasonValidationErrors()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { errors = new { Reason = new[] { "Vui lòng nhập lý do từ chối." } } }, HttpStatusCode.BadRequest)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() =>
            api.RejectFreezeReviewAsync(9, MembershipFreezeReviewTypes.Freeze, new("")));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Vui lòng nhập lý do từ chối.", Assert.Single(error.Errors["Reason"]));
    }

    [Fact]
    public async Task FreezeReview_PreservesConcurrentDecisionConflict()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { code = "FREEZE_REQUEST_STATE", detail = "The request has already been processed." },
            HttpStatusCode.Conflict)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() =>
            api.ApproveFreezeReviewAsync(9, MembershipFreezeReviewTypes.Freeze));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("FREEZE_REQUEST_STATE", error.ErrorCode);
        Assert.Equal("The request has already been processed.", error.Message);
    }

    [Fact]
    public async Task MemberFreezeTracking_ReadsEarlyUnfreezeDecision()
    {
        var expected = PendingFreezeRequest() with
        {
            EarlyUnfreezeStatus = "Rejected",
            EarlyUnfreezeReviewNote = "The current freeze must continue.",
            EarlyUnfreezeReviewedAt = new DateTimeOffset(2026, 12, 4, 0, 0, 0, TimeSpan.Zero)
        };
        using var api = CreateClient(_ => Task.FromResult(Json(new[] { expected })), role: "Member");

        var result = Assert.Single(await api.GetMyFreezeRequestsAsync());

        Assert.Equal(expected.EarlyUnfreezeStatus, result.EarlyUnfreezeStatus);
        Assert.Equal(expected.EarlyUnfreezeReviewNote, result.EarlyUnfreezeReviewNote);
        Assert.Equal(expected.EarlyUnfreezeReviewedAt, result.EarlyUnfreezeReviewedAt);
    }

    private static MembershipFreezeReviewDetailResponse FreezeReviewDetail(string type = MembershipFreezeReviewTypes.Freeze)
    {
        var early = type == MembershipFreezeReviewTypes.EarlyUnfreeze;
        var request = PendingFreezeRequest();
        var membership = FrozenMembership() with { Status = early ? "Frozen" : "Active" };
        var summary = new MembershipFreezeReviewSummaryResponse(9, type, "Member", "member@example.test",
            membership.PlanName, request.StartDate, request.EndDate, request.RequestedDays, request.SubmittedAt, "Pending");
        return new(summary, membership, request.Reason, null, null, null, null, 30,
            request.AppliedPolicy, early ? 7 : null, new DateOnly(2026, 12, 4), early ? 2 : null,
            true, true, null);
    }
}
