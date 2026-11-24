using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Pages.Public.Trainers;
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
            return Task.FromResult(Json(new TrainerPublicDetail(new(), [], [], authenticated)));
        }, authenticated);

        Assert.Equal(authenticated, (await api.DetailAsync(7)).HasActivePTContract);
    }

    [Fact]
    public async Task RecruitmentSendsContactDetailsWithoutCitizenId()
    {
        var application = new SubmitTrainerApplicationRequest("Trainer", "trainer@example.test", "0912345678",
            new(1990, 1, 1), "Other", "Strength", 3, "Trainer biography", true, []);
        using var api = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/trainers/applications", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("trainer@example.test", payload.RootElement.GetProperty("email").GetString());
            Assert.Equal("0912345678", payload.RootElement.GetProperty("phoneNumber").GetString());
            Assert.DoesNotContain(payload.RootElement.EnumerateObject(),
                property => property.Name.Equals("citizenId", StringComparison.OrdinalIgnoreCase));
            return Json(new TrainerApplicationReceipt("TR-1", "Pending", 1), HttpStatusCode.Created);
        });

        Assert.Equal(1, (await api.SubmitAsync(application)).SubmissionAttempt);
    }

    [Fact]
    public async Task ProfileUpdateRequiresSessionAndSendsCertificateReplacementId()
    {
        var application = new SubmitTrainerApplicationRequest("Trainer", "trainer@example.test", "0912345678",
            new(1990, 1, 1), "Other", "Strength", 3, "Trainer biography", true,
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
    public async Task ApplicantPrefillNeedsASignedInAccount()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal("/api/trainers/applicant-prefill", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer access", request.Headers.Authorization?.ToString());
            return Task.FromResult(Json(new TrainerApplicantPrefill("Nguyễn Văn An", "an@example.vn", "0912345678", new DateOnly(1995, 5, 6), "Male")));
        }, authenticated: true);
        Assert.Equal("0912345678", (await api.ApplicantPrefillAsync()).PhoneNumber);

        using var guest = CreateClient(_ => throw new InvalidOperationException("Unexpected API request."));
        var error = await Assert.ThrowsAsync<AuthApiException>(guest.ApplicantPrefillAsync);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task TrackingLookupIsPublicAndEncodesTheCode()
    {
        using var api = CreateClient(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.EndsWith("/api/trainers/applications/track/TR-a%2Fb", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(new TrainerApplicationTracking("TR-a/b", "Recruitment", "Rejected", DateTime.UtcNow, "PT", "Thiếu chứng chỉ")));
        });
        var tracking = await api.TrackAsync(" TR-a/b ");
        Assert.Equal(("Rejected", "Thiếu chứng chỉ"), (tracking.Status, tracking.RejectionReason));
    }

    [Fact]
    public async Task ManagerListFiltersByTrackingCode()
    {
        using var api = CreateClient(request =>
        {
            Assert.Equal("?TrackingCode=TR-abc&Page=1", request.RequestUri!.Query);
            return Task.FromResult(Json(new PagedResponse<TrainerApplicationResponse>(1, 10, 0, [])));
        }, authenticated: true);
        Assert.Empty((await api.ApplicationsAsync(new(TrackingCode: "TR-abc"))).Items);
    }

    [Theory]
    [InlineData("You can submit again after 2026-11-13T18:09:23.2543535.")]
    [InlineData("Bạn có thể nộp lại hồ sơ sau 2026-11-13T18:09:23.2543535.")]
    public async Task SubmissionCooldownDisplaysVietnameseTimeForUtcDatabaseValue(string message)
    {
        var application = new SubmitTrainerApplicationRequest("Trainer", "trainer@example.test", "0912345678",
            new(1990, 1, 1), "Other", "Strength", 3, "Trainer biography", true, []);
        using var api = CreateClient(_ => Task.FromResult(Json(new
        {
            errorCode = "RECRUITMENT_COOLDOWN",
            errorMessage = message
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

    // UC 3.4 step 7 / GBR-104: 'Explore Packages' only for a PT accepting new members; schedule link for the member's package.
    [Theory]
    [InlineData("PT", true, false, true, false)]
    [InlineData("PT", false, false, false, false)]
    [InlineData("Yoga", true, false, false, false)]
    [InlineData("PT", false, true, false, true)]
    public async Task TrainerDetailShowsPackageAndScheduleButtonsOnlyWhenAllowed(string role, bool accepting, bool hasContract,
        bool explore, bool schedule)
    {
        var detail = new TrainerPublicDetail(new TrainerProfileResponse { UserId = "pt", FullName = "Coach" } with
        {
            TrainerRole = role,
            IsAcceptingNewMembers = accepting
        }, [], [], hasContract);
        var html = WebUtility.HtmlDecode(await RenderTrainerDetailAsync(detail));
        Assert.Equal(explore, html.Contains("Khám phá gói PT"));
        Assert.Equal(schedule, html.Contains("Xem lịch PT của tôi"));
    }

    // UC 3.4 step 3 / MSG-01: no matching trainers shows the empty-state notice.
    [Fact]
    public async Task TrainerDirectoryShowsTheNoResultsNoticeWhenNothingMatches()
    {
        var http = new HttpClient(new FakeHandler(_ => Task.FromResult(Json(new PagedResponse<TrainerProfileResponse>(1, 20, 0, [])))))
            { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(new FakeStorage(null), new AuthApiClient(http));
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(new TrainerApiClient(http, session)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TrainerDirectory>()).ToHtmlString()));
        Assert.Contains("Không tìm thấy dữ liệu phù hợp.", html);
    }

    [Fact]
    public void ToastsShowAndDismissWithChangeNotice()
    {
        var toasts = new ToastService();
        var changes = 0;
        toasts.Changed += () => changes++;
        toasts.Show("Đã cập nhật hồ sơ huấn luyện viên thành công.");
        var toast = Assert.Single(toasts.Items);
        toasts.Dismiss(toast.Id);
        toasts.Dismiss(toast.Id);
        Assert.Empty(toasts.Items);
        Assert.Equal(2, changes);
    }

    private static async Task<string> RenderTrainerDetailAsync(TrainerPublicDetail detail)
    {
        var http = new HttpClient(new FakeHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/trainers/7"
            ? Json(detail) : Json(Array.Empty<object>())))) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(new FakeStorage(null), new AuthApiClient(http));
        var services = new ServiceCollection().AddLogging()
            .AddSingleton(new TrainerApiClient(http, session))
            .AddSingleton(new PersonalTrainingApiClient(http, session));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TrainerDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TrainerDetail.Id)] = 7
            }))).ToHtmlString());
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
