using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>The equipment list requests send the filters the screens choose.</summary>
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
