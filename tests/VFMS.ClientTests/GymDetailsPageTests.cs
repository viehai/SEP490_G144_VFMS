using System.Net;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Pages.Public;
using VFMS.Client.Services;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Memberships;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>UC-70 gym details page: open-now state, hours and plan durations, and the signed-out request.</summary>
public sealed class GymDetailsPageTests
{
    private static GymDetailsResponse Gym(TimeOnly? open, TimeOnly? close) =>
        new("Victor Fitness", null, null, null, open, close, true, [], [], [], null);

    [Theory]
    [InlineData(0, 0, true)]     // 07:00 gym time, opens at 05:00
    [InlineData(21, 59, false)]  // 04:59 gym time, before opening
    [InlineData(15, 0, false)]   // 22:00 gym time, closing time itself is closed
    [InlineData(14, 59, true)]   // 21:59 gym time
    public void OpenStateUsesGymTime(int utcHour, int utcMinute, bool expected)
    {
        var utc = new DateTime(2026, 11, 21, utcHour, utcMinute, 0, DateTimeKind.Utc);

        Assert.Equal(expected, GymDetails.OpenState(Gym(new TimeOnly(5, 0), new TimeOnly(22, 0)), utc));
    }

    [Fact]
    public void NoHoursMeansNoOpenStateAndPendingText()
    {
        Assert.Null(GymDetails.OpenState(Gym(null, null)));
        Assert.Equal("Đang cập nhật", GymDetails.Hours(Gym(null, null)));
        Assert.Equal("05:00 – 22:00", GymDetails.Hours(Gym(new TimeOnly(5, 0), new TimeOnly(22, 0))));
    }

    [Fact]
    public void PlanDurationPrefersMonths()
    {
        Assert.Equal("3 tháng", GymDetails.Duration(Plan(90, 3)));
        Assert.Equal("30 ngày", GymDetails.Duration(Plan(30, null)));
        Assert.Equal("—", GymDetails.Duration(Plan(null, null)));
    }

    [Fact]
    public async Task DetailsLoadWithoutSigningIn()
    {
        HttpRequestMessage? sent = null;
        var http = new HttpClient(new Handler(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Gym(new TimeOnly(5, 0), new TimeOnly(22, 0))),
            };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        using var api = new OperationsApiClient(http, new AuthSession(new NoStorage(), new AuthApiClient(http)));

        var details = await api.GetGymDetailsAsync();

        Assert.Equal("Victor Fitness", details.GymName);
        Assert.Equal("/api/gym-details", sent!.RequestUri!.AbsolutePath);
        Assert.Null(sent.Headers.Authorization);
    }

    private static MembershipPlanCatalogItemResponse Plan(int? days, int? months) =>
        new(1, "Plan", null, days, 500_000m, "Active", months);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    /// <summary>A browser with nothing stored: the visitor is signed out.</summary>
    private sealed class NoStorage : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException(identifier);
    }
}
