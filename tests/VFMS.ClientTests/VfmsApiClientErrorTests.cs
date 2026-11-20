using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using VFMS.Client.Services;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Every failed call must say which endpoint failed and how, so a failure can be traced to the right change.</summary>
public sealed class VfmsApiClientErrorTests
{
    [Fact]
    public async Task ApiErrorNamesTheCallStatusAndCodeAndIsLoggedOnce()
    {
        var log = new ListLogger();
        var api = YogaBookingCallsTests.CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { status = 409, detail = "Already booked.", code = "ALREADY_BOOKED" })
        }), log);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.PostAsync<YogaBookingResponse>("api/yoga/bookings", new { }));

        Assert.Equal("Already booked.", error.Message);   // user-facing text is unchanged
        Assert.Equal(("POST", "api/yoga/bookings", HttpStatusCode.Conflict, "ALREADY_BOOKED"), (error.Method, error.Path, error.StatusCode, error.ErrorCode));
        Assert.Contains("POST api/yoga/bookings -> 409 Conflict ALREADY_BOOKED: Already booked.", error.ToString());
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("api/yoga/bookings", entry.Text);
    }

    [Fact]
    public async Task NetworkFailureKeepsTheCauseAndTheEndpoint()
    {
        var log = new ListLogger();
        var api = YogaBookingCallsTests.CreateClient(_ => throw new HttpRequestException("TLS handshake failed"), log);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<MyYogaBookingsResponse>("api/yoga/bookings/mine"));

        Assert.Equal(("NETWORK_ERROR", "GET", "api/yoga/bookings/mine"), (error.ErrorCode, error.Method, error.Path));
        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Contains("HttpRequestException: TLS handshake failed", error.Describe());
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task ChangedResponseShapeBecomesAnApiExceptionNamingTheExpectedType()
    {
        var api = YogaBookingCallsTests.CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[1, 2, 3]", Encoding.UTF8, "application/json")
        }));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<MyYogaBookingsResponse>("api/yoga/bookings/mine"));

        Assert.Equal(("INVALID_RESPONSE", nameof(MyYogaBookingsResponse)), (error.ErrorCode, error.ResponseType));
        Assert.NotNull(error.InnerException);
        Assert.Contains("GET api/yoga/bookings/mine -> 200 OK INVALID_RESPONSE (expected MyYogaBookingsResponse)", error.Describe());
    }

    private sealed class ListLogger : ILogger<VfmsApiClient>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
