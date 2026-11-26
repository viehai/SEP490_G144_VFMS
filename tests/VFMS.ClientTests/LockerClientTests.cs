using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Lockers;
using VFMS.Contracts.Common;
using Xunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.Lockers;
using VFMS.Client.Components.Operations;

namespace VFMS.ClientTests;

/// <summary>Locker screens: API calls, 204 for "no rental", and the labels and countdown they show.</summary>
public sealed partial class LockerClientTests
{
    private readonly List<HttpRequestMessage> _requests = [];

    [Fact]
    public async Task ExpiredSessionUsesVietnameseNotice()
    {
        var api = Client(HttpStatusCode.Unauthorized);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.GridAsync());
        Assert.Equal("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại. (MSG-11)", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(21)]
    public async Task LockerGridShowsAtMostTenSlotsWithoutDroppingTheTotal(int count)
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var slots = Enumerable.Range(1, count).Select(i => new LockerSlot(i, $"A{i:00}", "A", 1, "Available")).ToArray();
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerGrid.Slots)] = slots
            }))).ToHtmlString());
        Assert.Equal(Math.Min(count, 10), System.Text.RegularExpressions.Regex.Matches(html, "aria-pressed=").Count);
        if (count > 10)
            Assert.Contains($"{count} tủ", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task GridPagerReachesLastLockerAndResetsAfterFiltering()
    {
        var activator = new TrackingActivator();
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var slots = Enumerable.Range(1, 21).Select(i => new LockerSlot(i, $"A{i:00}", "A", 1, "Available")).ToArray();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<LockerGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerGrid.Slots)] = slots
            }));
            await activator.Instances.OfType<Pager>().Single().OnPage.InvokeAsync(3);
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(">A21</span>", html);
            Assert.DoesNotContain(">A01</span>", html);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "aria-pressed=").Count);

            await activator.Instances.OfType<LockerGrid>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LockerGrid.Slots)] = slots.Take(11).ToArray()
            }));
            html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Trang 1 / 2", html);
            Assert.Contains(">A01</span>", html);
            Assert.Equal(10, System.Text.RegularExpressions.Regex.Matches(html, "aria-pressed=").Count);
        });
    }

    private sealed class TrackingActivator : IComponentActivator
    {
        public List<IComponent> Instances { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Instances.Add(component);
            return component;
        }
    }

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
    public async Task RentalHistorySendsPage()
    {
        var page = new PagedResponse<LockerRentalView>(2, 10, 0, []);
        var api = Client(HttpStatusCode.OK, page);
        Assert.Equal(2, (await api.HistoryAsync(2)).Page);
        Assert.Equal("/api/lockers/my-rentals/history?page=2", _requests.Single().RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task RentalHistoryShowsAnEmptyState()
    {
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(Client(HttpStatusCode.OK, new PagedResponse<LockerRentalView>(1, 10, 0, [])))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LockerRentalHistory>()).ToHtmlString());
        Assert.Contains("Chưa có lịch sử thuê tủ.", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task RentalHistoryPagerReachesTheLastContract()
    {
        LockerRentalView Rental(int id) => new(id, $"OLD-{id:00}", new(id, $"A{id:00}", "A", 1, "Available"),
            "Monthly", "Terminated", 250_000, 0, null, null, null, 0, 0, null, false, null, null, null, false, 1, null, 0, null);
        var first = new PagedResponse<LockerRentalView>(1, 10, 11, Enumerable.Range(1, 10).Select(Rental).ToArray());
        var last = new PagedResponse<LockerRentalView>(2, 10, 11, [Rental(11)]);
        var http = new HttpClient(new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(request.RequestUri!.Query.Contains("page=2") ? last : first)
        }))) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "member", "Member", "member@example.test", ["Member"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        var client = new LockerApiClient(new VfmsApiClient(http, new AuthSession(new Storage(stored), new AuthApiClient(http))));
        var activator = new TrackingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(client)
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<LockerRentalHistory>();
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("OLD-01", html);
            Assert.DoesNotContain("OLD-11", html);
            await activator.Instances.OfType<Pager>().Single().OnPage.InvokeAsync(2);
            html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("OLD-11", html);
            Assert.DoesNotContain("OLD-01", html);
            Assert.Contains("/my-locker/history/11?page=2", html);
        });
    }

    [Fact]
    public async Task RentalHistoryDetailsUseTheMemberEndpoint()
    {
        var api = Client(HttpStatusCode.OK, new { id = 7, contractNumber = "OLD-07" });
        var rental = await api.MyRentalDetailsAsync(7);
        Assert.Equal(7, rental.Id);
        Assert.Equal("/api/lockers/my-rentals/7", _requests.Single().RequestUri!.AbsolutePath);
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
