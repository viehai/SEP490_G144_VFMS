using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Lockers;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Locker screens: API calls, 204 for "no rental", and the labels and countdown they show.</summary>
public sealed class LockerClientTests
{
    private readonly List<HttpRequestMessage> _requests = [];

    private LockerApiClient Client(HttpStatusCode status, object? body = null)
    {
        var http = new HttpClient(new Handler(request =>
        {
            _requests.Add(request);
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = JsonContent.Create(body);
            return Task.FromResult(response);
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "member", "Member", "member@example.test", ["Member"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        var session = new AuthSession(new Storage(stored), new AuthApiClient(http));
        return new LockerApiClient(new VfmsApiClient(http, session));
    }

    [Fact]
    public async Task NoCurrentRentalIsAnEmptyStateNotAnError()
    {
        var api = Client(HttpStatusCode.NoContent);

        Assert.Null(await api.MyRentalAsync());
        Assert.Equal("/api/lockers/my-rental", _requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ProceedToPaymentPostsToTheOrder()
    {
        var checkout = new LockerCheckout(1, "ORD 1", "Pending", 350_000, DateTime.UtcNow, null, null, 0, null, null);
        var api = Client(HttpStatusCode.OK, checkout);

        var result = await api.ProceedAsync("ORD 1");

        Assert.Equal("ORD 1", result.OrderNumber);
        Assert.Equal(HttpMethod.Post, _requests.Single().Method);
        Assert.Equal("/api/lockers/orders/ORD%201/proceed", _requests.Single().RequestUri!.AbsolutePath.Replace(" ", "%20"));
    }

    [Fact]
    public async Task RegistrySendsSearchAndOverdueFilter()
    {
        var api = Client(HttpStatusCode.OK, Array.Empty<LockerStaffRental>());

        await api.RegistryAsync("A 01", true);

        var query = _requests.Single().RequestUri!.Query;
        Assert.Contains("search=A%2001", query);
        Assert.Contains("overdueOnly=true", query);
    }

    [Theory]
    [InlineData("Available", "Còn trống", "Available")]
    [InlineData("Reserved", "Đang giữ", "Pending")]
    [InlineData("Overdue", "Quá hạn", "Overdue")]
    [InlineData("Maintenance", "Bảo trì", "Inactive")]
    public void SlotLabelsAndTonesMatchTheGridLegend(string status, string label, string tone)
    {
        Assert.Equal(label, LockerText.Slot(status));
        Assert.Equal(tone, LockerText.SlotTone(status));
    }

    [Fact]
    public void RemainingCountsDownAndStopsAtZero()
    {
        var now = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal("9:30", LockerText.Remaining(now.AddMinutes(9).AddSeconds(30), now));
        Assert.Equal("0:00", LockerText.Remaining(now.AddSeconds(-1), now));
    }

    [Fact]
    public void LastDayShowsTheDayBeforeTheMidnightExpiry()
    {
        // 15/01 plus one month ends on 14/02: the expiry instant is 15/02 00:00 gym time (UTC+7).
        var expiry = new DateTime(2026, 2, 14, 17, 0, 0, DateTimeKind.Utc);

        Assert.Equal("14/02/2026", LockerText.LastDay(expiry));
        Assert.Equal("00:00 15/02/2026", LockerText.Local(expiry));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class Storage(string value) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((T)(object)value) : throw new InvalidOperationException(identifier);
    }
}
