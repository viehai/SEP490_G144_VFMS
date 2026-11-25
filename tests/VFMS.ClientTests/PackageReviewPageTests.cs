using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Auth;
using VFMS.Client.Pages.Public;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Feedback;
using VFMS.Contracts.Packages;
using Xunit;

namespace VFMS.ClientTests;

public sealed partial class MembershipPlanApiClientTests
{
    [Theory]
    [InlineData("GYM", false, false, null, "Đăng nhập để viết đánh giá")]
    [InlineData("YOGA", true, false, null, "được điểm danh có mặt")]
    [InlineData("PT", true, true, null, "Gửi đánh giá")]
    [InlineData("GYM", true, false, "Approved", "Bạn đã gửi đánh giá. Đánh giá được hiển thị công khai.")]
    public async Task PackageDetailShowsReviewsBeforeSuggestionsAndTheAppropriateWritingState(
        string type, bool signedIn, bool eligible, string? myStatus, string expected)
    {
        var package = new PackageCatalogItemResponse(7, "Gói tập", "Mô tả", 300_000, "ACTIVE", type,
            30, null, null, null, null, null, null, false, null, null, null, null);   // yoga level instead of the removed class-type pair
        var reviews = new PackageReviewsResponse(1, 4, new(1, 5, 1,
            [new(9, "Mai", 4, "Khung giờ thuận tiện.", new(2026, 11, 20), null)]))
            { CanReview = eligible, MyReviewStatus = myStatus };
        var login = new LoginResponse("access", "refresh", "Bearer", 1800, "mai", "Mai", "mai@example.test", ["Member"]);
        var storage = new ReadOnlyStorage(signedIn
            ? JsonSerializer.Serialize(new StoredAuthSession(login, DateTimeOffset.UtcNow.AddMinutes(10), false)) : null);
        using var http = new HttpClient(new FakeHandler(request =>
        {
            Assert.Equal(signedIn ? "Bearer access" : null, request.Headers.Authorization?.ToString());
            return Task.FromResult(request.RequestUri!.PathAndQuery switch
            {
                "/api/packages/7/reviews?page=1" => Json(reviews),
                var path when path == $"/api/packages/{type.ToLowerInvariant()}/7" =>
                    Json(new PackageDetailResponse(package, [package with { Id = 8 }])),
                var path => throw new InvalidOperationException(path)
            });
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var session = new AuthSession(storage, new AuthApiClient(http));
        await using var provider = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton<AuthenticationStateProvider>(session).AddCascadingAuthenticationState()
            .AddSingleton(new MembershipCart()).AddSingleton(new MembershipPlanApiClient(http, session))
            .AddSingleton(new SystemAdministrationApiClient(http, session)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PackageDetail>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PackageDetail.Type)] = type, [nameof(PackageDetail.PackageId)] = package.Id
            }))).ToHtmlString()));
        Assert.Contains("Đánh giá của hội viên", html);
        Assert.Contains("Khung giờ thuận tiện.", html);
        Assert.Contains("4/5 sao", html);
        Assert.True(html.IndexOf("id=\"package-reviews\"", StringComparison.Ordinal) < html.IndexOf("Gói gợi ý", StringComparison.Ordinal));
        Assert.Contains(expected, html);
        Assert.Equal(eligible, html.Contains("name=\"package-rating\"", StringComparison.Ordinal));
    }
}
