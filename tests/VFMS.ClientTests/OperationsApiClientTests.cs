using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Operations;
using VFMS.Contracts.Supplements;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>The equipment and issue list requests send the filters the screens choose.</summary>
public sealed class OperationsApiClientTests
{
    private readonly List<Uri> _requests = [];

    private OperationsApiClient Client(object body)
    {
        var http = new HttpClient(new Handler(request =>
        {
            _requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "manager", "Manager", "manager@example.test", ["Manager"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        return new OperationsApiClient(http, new AuthSession(new Storage(stored), new AuthApiClient(http)));
    }

    [Theory]
    [InlineData("/api/images/0f8fad5bd9cb469fa165708f3a5e1c22", "https://api.example.test/api/images/0f8fad5bd9cb469fa165708f3a5e1c22", "https://api.example.test/api/images/0f8fad5bd9cb469fa165708f3a5e1c22?download=true")]
    [InlineData("https://cdn.example.com/whey.png", "https://cdn.example.com/whey.png", "https://cdn.example.com/whey.png")]
    [InlineData(null, null, null)]
    public void UploadedImagesAreLoadedFromTheApi(string? stored, string? src, string? download)
    {
        var api = Client(new { });
        Assert.Equal(src, api.ImageSrc(stored));
        Assert.Equal(download, api.ImageDownloadHref(stored));
    }

    [Fact]
    public async Task EquipmentListSendsBrandAndSort()
    {
        using var api = Client(new PagedResponse<EquipmentResponse>(2, 20, 0, []));

        await api.GetEquipmentAsync(new EquipmentListRequest(Search: "TM", Page: 2, Brand: "Life Fitness", Sort: "purchase_date"));

        var query = Uri.UnescapeDataString(_requests.Single().Query);
        Assert.Equal("/api/equipment", _requests.Single().AbsolutePath);
        Assert.Contains("brand=Life Fitness", query);
        Assert.Contains("sort=purchase_date", query);
        Assert.Contains("search=TM", query);
        Assert.Contains("page=2", query);
    }

    [Fact]
    public async Task EquipmentListLeavesOutEmptyFilters()
    {
        using var api = Client(new PagedResponse<EquipmentResponse>(1, 20, 0, []));

        await api.GetEquipmentAsync(new EquipmentListRequest());

        Assert.DoesNotContain("brand=", _requests.Single().Query);
        Assert.DoesNotContain("sort=", _requests.Single().Query);
    }

    [Fact]
    public async Task BrandsComeFromTheBrandsEndpoint()
    {
        using var api = Client(new[] { "Hammer Strength", "Life Fitness" });

        Assert.Equal(["Hammer Strength", "Life Fitness"], await api.GetEquipmentBrandsAsync());
        Assert.Equal("/api/equipment/brands", _requests.Single().AbsolutePath);
    }

    [Fact]
    public async Task IssueListSendsSearchAndSeveritySort()
    {
        using var api = Client(new PagedResponse<EquipmentIssueResponse>(1, 20, 0, []));

        await api.GetIssuesAsync(new EquipmentIssueListRequest(Status: "Reported", Search: "TM-0001", Sort: "severity"));

        var query = Uri.UnescapeDataString(_requests.Single().Query);
        Assert.Equal("/api/equipment-issues", _requests.Single().AbsolutePath);
        Assert.Contains("search=TM-0001", query);
        Assert.Contains("sort=severity", query);
        Assert.Contains("status=Reported", query);
    }

    [Fact]
    public async Task IssueSummaryComesFromTheEquipmentItself()
    {
        using var api = Client(new EquipmentIssueSummaryResponse(7, 4, 1, 3, 1, DateTime.UtcNow, null, true));

        var summary = await api.GetEquipmentIssueSummaryAsync(7);

        Assert.Equal("/api/equipment/7/issue-summary", _requests.Single().AbsolutePath);
        Assert.True(summary.RepeatedIssues);
        Assert.Equal(3, summary.RecentIssues);
    }

    [Fact]
    public async Task HistoryListAsksForOneMachineNewestFirst()
    {
        using var api = Client(new PagedResponse<EquipmentIssueResponse>(1, 5, 0, []));

        await api.GetIssuesAsync(new EquipmentIssueListRequest(null, 7, 1, 5, Sort: "newest"));

        var query = Uri.UnescapeDataString(_requests.Single().Query);
        Assert.Contains("equipmentId=7", query);
        Assert.Contains("sort=newest", query);
        Assert.Contains("pageSize=5", query);
    }

    [Fact]
    public async Task StockChangeIsPostedToTheProduct()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var http = new HttpClient(new Handler(async request =>
        {
            sent = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SupplementProductResponse(4, "Whey", null, null, null, 1m, "Active", true, DateTime.UtcNow, DateTime.UtcNow, StockQuantity: 12))
            };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "manager", "Manager", "manager@example.test", ["Manager"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        using var api = new OperationsApiClient(http, new AuthSession(new Storage(stored), new AuthApiClient(http)));

        var saved = await api.AdjustProductStockAsync(4, new SupplementStockRequest(SupplementStock.Add, 12, "Lô 19/11"));

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("/api/supplement-products/4/stock", sent.RequestUri!.AbsolutePath);
        Assert.Contains("\"action\":\"Add\"", body);
        Assert.Contains("\"quantity\":12", body);
        Assert.Equal(12, saved.StockQuantity);
    }

    [Fact]
    public async Task LowStockFilterIsSentOnlyWhenOn()
    {
        using var api = Client(new PagedResponse<SupplementProductResponse>(1, 20, 0, []));

        await api.GetProductsAsync(new SupplementProductListRequest(LowStock: true), manage: true);
        await api.GetProductsAsync(new SupplementProductListRequest(), manage: true);

        Assert.Contains("lowStock=true", _requests[0].Query);
        Assert.DoesNotContain("lowStock", _requests[1].Query);
    }

    [Theory]
    [InlineData(null, "Tất cả (6)")]
    [InlineData("Reported", "Mới báo (4)")]
    [InlineData("Resolved", "Đã xử lý (0)")]
    public void StatusOptionsShowTheirCount(string? status, string expected)
    {
        var counts = new StatusCountsResponse(new Dictionary<string, int> { ["Reported"] = 4, ["Escalated"] = 2, ["Resolved"] = 0 });
        Assert.Equal(expected, OperationsText.CountLabel(status, counts));
    }

    [Fact]
    public void WithoutCountsTheOptionIsJustTheLabel() =>
        Assert.Equal("Mới báo", OperationsText.CountLabel("Reported", null));

    [Theory]
    [InlineData("issues", "/api/equipment-issues/counts")]
    [InlineData("orders", "/api/supplement-orders/counts")]
    [InlineData("mine", "/api/purchase-proposals/mine/counts")]
    [InlineData("review", "/api/purchase-proposals/counts")]
    public async Task CountsComeFromEachBoard(string board, string path)
    {
        using var api = Client(new StatusCountsResponse(new Dictionary<string, int> { ["Pending"] = 2 }));

        var counts = board switch
        {
            "issues" => await api.GetIssueCountsAsync(),
            "orders" => await api.GetOrderCountsAsync(),
            "mine" => await api.GetProposalCountsAsync(mine: true),
            _ => await api.GetProposalCountsAsync(mine: false),
        };

        Assert.Equal(path, _requests.Single().AbsolutePath);
        Assert.Equal(2, counts.Total);
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
