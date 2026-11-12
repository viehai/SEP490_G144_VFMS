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
    [InlineData(null, "")]
    [InlineData(9, "?excludeRequestId=9")]
    public async Task FreezeEligibility_UsesOwnedMembershipAndOptionalEditContext(int? excludedRequestId, string query)
    {
        var expected = new MembershipFreezeEligibilityResponse(
            FrozenMembership() with { Status = "Active" }, true, null, null,
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), 14, 30,
            new(1, 2, 20, 2, 14, null));
        using var api = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer access-token", request.Headers.Authorization?.ToString());
            Assert.Equal("/api/memberships/7/freeze-eligibility" + query, request.RequestUri!.PathAndQuery);
            Assert.Null(request.Content);
            return Task.FromResult(Json(expected));
        }, role: "Member");

        var result = await api.GetFreezeEligibilityAsync(7, excludedRequestId);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MyFreezeRequests_ReadsEmptyAndPopulatedCollections(bool hasRequests)
    {
        var request = PendingFreezeRequest();
        MembershipFreezeRequestResponse[] expected = hasRequests ? [request] : [];
        using var api = CreateClient(message =>
        {
            Assert.Equal(HttpMethod.Get, message.Method);
            Assert.Equal("Bearer access-token", message.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests", message.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(expected));
        }, role: "Member");

        var results = await api.GetMyFreezeRequestsAsync();

        Assert.Equal(expected.Length, results.Count);
        if (hasRequests)
        {
            var result = Assert.Single(results);
            Assert.Equal(request.FreezeRequestId, result.FreezeRequestId);
            Assert.Equal(request.Reason, result.Reason);
            Assert.Equal(request.SubmittedAt, result.SubmittedAt);
            Assert.Equal("Pending", result.Status);
            Assert.True(result.CanEdit);
            Assert.True(result.CanCancel);
            Assert.False(result.CanRequestEarlyUnfreeze);
        }
    }

    [Fact]
    public async Task GetFreezeRequest_ReadsOwnedRequestAndHistoricalPolicy()
    {
        var expected = PendingFreezeRequest();
        using var api = CreateClient(message =>
        {
            Assert.Equal(HttpMethod.Get, message.Method);
            Assert.Equal("Bearer access-token", message.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests/9", message.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(expected));
        }, role: "Member");

        var result = await api.GetFreezeRequestAsync(9);

        Assert.Equal(expected.StartDate, result.StartDate);
        Assert.Equal(expected.EndDate, result.EndDate);
        Assert.Equal(expected.AppliedPolicy!.Policy, result.AppliedPolicy!.Policy);
        Assert.Equal(expected.AppliedPolicy.Quotas, result.AppliedPolicy.Quotas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFreezeRequest_SendsOnlyEditableValuesAndReadsServerPeriod(bool updating)
    {
        var expected = PendingFreezeRequest();
        using var api = CreateClient(async message =>
        {
            Assert.Equal(updating ? HttpMethod.Put : HttpMethod.Post, message.Method);
            Assert.Equal("Bearer access-token", message.Headers.Authorization?.ToString());
            Assert.Equal(updating ? "/api/membership-freeze-requests/9" : "/api/membership-freeze-requests",
                message.RequestUri!.PathAndQuery);
            var body = await message.Content!.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(4, body.EnumerateObject().Count());
            Assert.Equal(7, body.GetProperty("membershipId").GetInt32());
            Assert.Equal("2026-12-03", body.GetProperty("startDate").GetString());
            Assert.Equal(7, body.GetProperty("requestedDays").GetInt32());
            Assert.Equal("Travel", body.GetProperty("reason").GetString());
            return Json(expected, updating ? HttpStatusCode.OK : HttpStatusCode.Created);
        }, role: "Member");
        var request = new MembershipFreezeRequest(7, expected.StartDate, 7, "Travel");

        var result = updating
            ? await api.UpdateFreezeRequestAsync(9, request)
            : await api.CreateFreezeRequestAsync(request);

        Assert.Equal(new DateOnly(2026, 12, 9), result.EndDate);
        Assert.Equal(7, result.RequestedDays);
        Assert.Equal("Pending", result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreezeActions_SendBodylessCommandsAndReadServerCapabilities(bool earlyUnfreeze)
    {
        var expected = PendingFreezeRequest() with
        {
            Status = earlyUnfreeze ? "Active" : "Cancelled",
            CanEdit = false,
            CanCancel = false,
            EarlyUnfreezeRequestedAt = earlyUnfreeze ? new DateTimeOffset(2026, 12, 4, 0, 0, 0, TimeSpan.Zero) : null,
            EarlyUnfreezeStatus = earlyUnfreeze ? "Pending" : null
        };
        using var api = CreateClient(message =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("Bearer access-token", message.Headers.Authorization?.ToString());
            Assert.Equal("/api/membership-freeze-requests/9/" + (earlyUnfreeze ? "early-unfreeze" : "cancel"),
                message.RequestUri!.PathAndQuery);
            Assert.Null(message.Content);
            return Task.FromResult(Json(expected));
        }, role: "Member");

        var result = earlyUnfreeze
            ? await api.RequestEarlyUnfreezeAsync(9)
            : await api.CancelFreezeRequestAsync(9);

        Assert.Equal(expected.Status, result.Status);
        Assert.Equal(expected.EarlyUnfreezeRequestedAt, result.EarlyUnfreezeRequestedAt);
        Assert.Equal(expected.EarlyUnfreezeStatus, result.EarlyUnfreezeStatus);
        Assert.False(result.CanEdit);
        Assert.False(result.CanCancel);
        Assert.False(result.CanRequestEarlyUnfreeze);
    }

    [Fact]
    public async Task SaveFreezeRequest_PreservesFieldValidationErrors()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { errors = new { StartDate = new[] { "Select a start date." }, Reason = new[] { "Enter a reason." } } },
            HttpStatusCode.BadRequest)), role: "Member");

        var error = await Assert.ThrowsAsync<AuthApiException>(() =>
            api.CreateFreezeRequestAsync(new(7, null, 7, "")));

        Assert.Equal("Select a start date.", Assert.Single(error.Errors["StartDate"]));
        Assert.Equal("Enter a reason.", Assert.Single(error.Errors["Reason"]));
    }

    [Fact]
    public async Task FreezeAction_PreservesStateConflictForPageFeedback()
    {
        using var api = CreateClient(_ => Task.FromResult(Json(
            new { code = "FREEZE_REQUEST_STATE", detail = "The request can no longer be cancelled." },
            HttpStatusCode.Conflict)), role: "Member");

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.CancelFreezeRequestAsync(9));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("FREEZE_REQUEST_STATE", error.ErrorCode);
        Assert.Equal("The request can no longer be cancelled.", error.Message);
    }

    private static MembershipFreezeRequestResponse PendingFreezeRequest() =>
        new(9, 7, "Gym", new DateOnly(2026, 12, 3), new DateOnly(2026, 12, 9), 7, "Travel",
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), "Pending", null, null, null, null,
            true, true, false,
            new(new(1, 2, 20, 2, 14, null), [new(30, MembershipFreezeTierUnits.Days, 30)]));
}
