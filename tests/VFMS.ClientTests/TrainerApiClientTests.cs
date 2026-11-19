using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using Xunit;

namespace VFMS.ClientTests;

public sealed class TrainerApiClientTests
{
    [Fact]
    public async Task DirectoryUsesPublicEndpointAndEncodesFilters()
    {
        using var api = CreateClient(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("/api/trainers", request.RequestUri!.AbsolutePath);
            Assert.Equal("?Search=PT%20%26%20Gym&MinimumRating=4.5&Page=2", request.RequestUri.Query);
            return Task.FromResult(Json(new PagedResponse<TrainerProfileResponse>(2, 20, 0, [])));
        });

        Assert.Empty((await api.SearchAsync(new(Search: "PT & Gym", MinimumRating: 4.5m, Page: 2))).Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailUsesOptionalMemberTokenAndReadsScheduleFlag(bool authenticated)
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal("/api/trainers/7", request.RequestUri!.AbsolutePath);
            Assert.Equal(authenticated ? "Bearer access" : null, request.Headers.Authorization?.ToString());
            return Task.FromResult(Json(new TrainerPublicDetail(new(), [], [], [], authenticated)));
        }, authenticated);

        Assert.Equal(authenticated, (await api.DetailAsync(7)).HasActivePTContract);
    }

    [Fact]
    public async Task ProfileUpdateRequiresSessionAndSendsCertificateReplacementId()
    {
        var application = new SubmitTrainerApplicationRequest("Trainer", "trainer@example.test", "0912345678",
            "012345678901", new(1990, 1, 1), "Other", "Strength", 3, "Trainer biography", true,
            [new("certificate.pdf", [1, 2], "previous")], "ProfileUpdate");
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/trainers/applications", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer access", request.Headers.Authorization?.ToString());
            var sent = await request.Content!.ReadFromJsonAsync<SubmitTrainerApplicationRequest>();
            Assert.Equal("ProfileUpdate", sent!.ApplicationType);
            Assert.Equal("previous", Assert.Single(sent.Certificates).ReplacesCertificateId);
            return Json(new TrainerApplicationReceipt("TR-1", "Pending", 2), HttpStatusCode.Created);
        }, authenticated: true);

        Assert.Equal(2, (await api.SubmitAsync(application)).SubmissionAttempt);
        using var anonymous = CreateClient(_ => throw new InvalidOperationException("Unexpected API request."));
        var error = await Assert.ThrowsAsync<AuthApiException>(() => anonymous.SubmitAsync(application));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task SubmissionCooldownDisplaysVietnameseTimeForUtcDatabaseValue()
    {
        var application = new SubmitTrainerApplicationRequest("Trainer", "trainer@example.test", "0912345678",
            "012345678901", new(1990, 1, 1), "Other", "Strength", 3, "Trainer biography", true, []);
        using var api = CreateClient(_ => Task.FromResult(Json(new
        {
            errorCode = "RECRUITMENT_COOLDOWN",
            errorMessage = "You can submit again after 2026-11-13T18:09:23.2543535."
        }, HttpStatusCode.Conflict)));

        var error = await Assert.ThrowsAsync<AuthApiException>(() => api.SubmitAsync(application));
        Assert.Equal("Bạn có thể nộp lại hồ sơ sau 01:09:23 ngày 14/11/2026 (giờ Việt Nam).", error.Message);
    }

    [Fact]
    public async Task SetupUsesOnlyTheEmailTokenAndNewPassword()
    {
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/trainers/setup-password", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(new TrainerSetupRequest("setup-token", "Example01!"),
                await request.Content!.ReadFromJsonAsync<TrainerSetupRequest>());
            return Json(true);
        }, authenticated: true);

        Assert.True(await api.SetupAsync(new("setup-token", "Example01!")));
    }

    private static TrainerApiClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, bool authenticated = false)
    {
        var http = new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("https://api.example.test/") };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "user", "Test User", "test@example.test", ["Member"]);
        var stored = authenticated
            ? JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false))
            : null;
        return new TrainerApiClient(http, new AuthSession(new FakeStorage(stored), new AuthApiClient(http)));
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }

    private sealed class FakeStorage(string? value) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "vfmsAuthStorage.read" ? ValueTask.FromResult((TValue)(object?)value!) : throw new InvalidOperationException(identifier);
    }
}
