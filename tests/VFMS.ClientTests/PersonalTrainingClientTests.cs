using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.PersonalTraining;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PersonalTrainingClientTests
{
    [Fact]
    public async Task HoldAndPaymentAreSeparateAuthenticatedRequests()
    {
        var paths = new List<string>();
        var booking = new PTBookingRequest("trainer", 7, new(2026, 11, 17), [3, 4]);
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer access", request.Headers.Authorization?.ToString());
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (paths.Count == 1)
            {
                var sent = await request.Content!.ReadFromJsonAsync<PTBookingRequest>();
                Assert.Equal(booking.TemplateIds, sent!.TemplateIds);
                Assert.Equal(booking.StartDate, sent.StartDate);
            }
            else
                Assert.Equal("PROMO", (await request.Content!.ReadFromJsonAsync<PTPaymentRequest>())!.VoucherCode);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PTCheckoutResponse(1, "ORD-1", "Pending", 500000,
                    DateTime.UtcNow.AddMinutes(15), null, null, DateTime.UtcNow.AddMinutes(20)))
            };
        }))
        { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "member", "Member", "member@example.test", ["Member"]);
        var session = new AuthSession(new Storage(JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false))),
            new AuthApiClient(http));
        using var api = new PersonalTrainingApiClient(http, session);

        var order = await api.HoldAsync(booking);
        await api.StartPaymentAsync(order.OrderNumber, new("PROMO"));
        Assert.Equal(new[] { "/api/pt/purchases", "/api/pt/purchases/ORD-1/payment" }, paths);
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
