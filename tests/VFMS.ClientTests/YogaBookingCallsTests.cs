using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

public sealed class YogaBookingCallsTests
{
    [Fact]
    public async Task LateCancelAsksFirstThenSendsTheConfirmation()
    {
        var bodies = new List<string>();
        var api = CreateClient(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            bodies.Add(body);
            Assert.Equal("/api/yoga/bookings/5/cancel", request.RequestUri!.PathAndQuery);
            return body.Contains("true")
                ? Json(new YogaBookingResponse(5, 1, "Hatha", "Cancelled", 3, null, "Cancelled"))
                : Json(new { status = 409, detail = "late", code = YogaBookingCalls.LateCancellationCode }, HttpStatusCode.Conflict);   // the API's ProblemDetails shape
        });

        Assert.False(await api.CancelBookingAsync(5, confirmLate: false));
        Assert.True(await api.CancelBookingAsync(5, confirmLate: true));
        Assert.Equal(2, bodies.Count);
        Assert.Contains("\"confirmLateCancellation\":false", bodies[0]);
        Assert.Contains("\"confirmLateCancellation\":true", bodies[1]);
    }

    [Fact]
    public async Task OtherErrorsAreNotTreatedAsALateCancel()
    {
        var api = CreateClient(_ => Task.FromResult(Json(new { status = 409, detail = "No upcoming session.", code = "NO_UPCOMING_SESSION" }, HttpStatusCode.Conflict)));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.CancelBookingAsync(5, confirmLate: false));
        Assert.Equal("NO_UPCOMING_SESSION", error.ErrorCode);
        Assert.Equal("No upcoming session.", error.Message);
    }

    internal static VfmsApiClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, ILogger<VfmsApiClient>? logger = null)
    {
        var http = new HttpClient(new FakeHandler(handler)) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access-token", "refresh-token", "Bearer", 1800, "member", "Member", "member@example.com", ["Member"]);
        var stored = JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false));
        return new(http, new AuthSession(new ReadOnlyStorage(stored), new AuthApiClient(http)), logger);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }

    private sealed class ReadOnlyStorage(string? value) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((TValue)(object?)value!) : throw new InvalidOperationException(identifier);
    }
}
