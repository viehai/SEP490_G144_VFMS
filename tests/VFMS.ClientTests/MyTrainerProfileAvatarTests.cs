using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client.Auth;
using VFMS.Client.Pages.PT;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Contracts.Users;
using Xunit;

namespace VFMS.ClientTests;

public sealed class MyTrainerProfileAvatarTests
{
    private const string OldAvatar = "https://api.example.test/api/trainers/7/avatar/old";
    private const string NewAvatar = "https://api.example.test/api/trainers/7/avatar/new";
    private const string TypeError = "Chọn ảnh JPG hoặc PNG hợp lệ.";
    private const string ReadError = "Không đọc được ảnh. Dung lượng tối đa là 5 MB.";
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1];

    [Theory]
    [InlineData("type", TypeError)]
    [InlineData("signature", TypeError)]
    [InlineData("size", ReadError)]
    [InlineData("read", ReadError)]
    [InlineData("javascript", ReadError)]
    public async Task InvalidAvatarShowsAccessibleErrorBelowFileAndBlocksSavingBio(string failure, string expected)
    {
        await using var host = new ProfileHost();
        await host.RenderAsync();
        await host.SelectAsync(failure switch
        {
            "type" => new BrowserFile("avatar.gif", Png),
            "signature" => new BrowserFile("avatar.png", [1, 2, 3]),
            "size" => new BrowserFile("avatar.png", Png, 5 * 1024 * 1024 + 1),
            "read" => new BrowserFile("avatar.png", Png, readFailure: new IOException("Read failed")),
            _ => new BrowserFile("avatar.png", Png, readFailure: new JSException("Browser read failed"))
        });

        AssertInlineAvatarError(await host.HtmlAsync(), expected);
        Assert.Contains(OldAvatar, await host.HtmlAsync());
        await host.ChangeBioAsync("Updated biography");
        await host.SaveAsync();

        Assert.Empty(host.Updates);
        Assert.Empty(host.Toasts.Items);
        AssertInlineAvatarError(await host.HtmlAsync(), expected);
        Assert.Contains(OldAvatar, await host.HtmlAsync());
    }

    [Fact]
    public async Task ValidReselectClearsAvatarErrorAndKeepsPublishedImageUntilSaveSucceeds()
    {
        await using var host = new ProfileHost();
        await host.RenderAsync();
        await host.SelectAsync(new BrowserFile("wrong.png", [0]));
        AssertInlineAvatarError(await host.HtmlAsync(), TypeError);

        await host.SelectAsync(new BrowserFile("replacement.png", Png));
        var selected = await host.HtmlAsync();
        Assert.DoesNotContain(TypeError, selected);
        Assert.DoesNotContain("aria-invalid=\"true\"", selected);
        Assert.Contains("Tệp đã chọn: replacement.png", selected);
        Assert.Contains(OldAvatar, selected);
        Assert.DoesNotContain(NewAvatar, selected);
        Assert.Empty(host.Updates);

        await host.SaveAsync();

        var update = Assert.Single(host.Updates);
        Assert.Null(update.Bio);
        Assert.Equal("replacement.png", update.Avatar!.FileName);
        Assert.Equal(Png, update.Avatar.Content);
        var saved = await host.HtmlAsync();
        Assert.Contains(NewAvatar, saved);
        Assert.DoesNotContain(OldAvatar, saved);
        Assert.DoesNotContain("Tệp đã chọn:", saved);
        Assert.Single(host.Toasts.Items);
    }

    [Fact]
    public async Task ApiAvatarFieldValidationIsInlineAndReselectClearsIt()
    {
        const string message = "Ảnh đại diện không hợp lệ. Vui lòng kiểm tra lại.";
        await using var host = new ProfileHost(() => Json(new
        {
            errors = new Dictionary<string, string[]> { ["Avatar.Content"] = [message] }
        }, HttpStatusCode.BadRequest));
        await host.RenderAsync();
        await host.SelectAsync(new BrowserFile("replacement.png", Png));
        await host.SaveAsync();

        AssertInlineAvatarError(await host.HtmlAsync(), message);
        Assert.Contains(OldAvatar, await host.HtmlAsync());
        await host.SaveAsync();
        Assert.Single(host.Updates);

        await host.SelectAsync(new BrowserFile("another.png", Png));
        Assert.DoesNotContain(message, await host.HtmlAsync());
        Assert.DoesNotContain("aria-invalid=\"true\"", await host.HtmlAsync());
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("banned")]
    [InlineData("network")]
    public async Task UnattributedProfileAndBioErrorsStayAboveProfileAndSurviveAvatarReselect(string failure)
    {
        await using var host = new ProfileHost(() => failure switch
        {
            "validation" => Json(new { errorCode = "TRAINER_VALIDATION", errorMessage = "Giới thiệu hoặc ảnh đại diện không hợp lệ." }, HttpStatusCode.BadRequest),
            "banned" => Json(new { errorCode = TrainerContentRules.RejectedCode, errorMessage = TrainerContentRules.Message(["blocked"]) }, HttpStatusCode.BadRequest),
            _ => throw new HttpRequestException("Offline")
        });
        await host.RenderAsync();
        await host.ChangeBioAsync("Updated biography blocked");
        await host.SaveAsync();

        var expected = failure switch
        {
            "validation" => "Giới thiệu hoặc ảnh đại diện không hợp lệ.",
            "banned" => "Giới thiệu có từ ngữ không được phép. Sửa các từ được tô đỏ rồi lưu lại.",
            _ => "Không kết nối được máy chủ. Vui lòng kiểm tra kết nối và thử lại."
        };
        AssertGlobalError(await host.HtmlAsync(), expected);
        await host.SelectAsync(new BrowserFile("replacement.png", Png));
        AssertGlobalError(await host.HtmlAsync(), expected);
    }

    private static void AssertInlineAvatarError(string html, string message)
    {
        var input = Regex.Match(html, "<input[^>]*type=\"file\"[^>]*>").Value;
        Assert.NotEmpty(input);
        Assert.Contains("aria-invalid=\"true\"", input);
        var description = Regex.Match(input, "aria-describedby=\"([^\"]+)\"");
        Assert.True(description.Success, "The avatar input must reference its inline error.");
        var alert = Regex.Match(html, "<p[^>]*id=\"" + Regex.Escape(description.Groups[1].Value) + "\"[^>]*>.*?</p>", RegexOptions.Singleline);
        Assert.True(alert.Success, "The described error must be rendered below the avatar input.");
        Assert.Contains("role=\"alert\"", alert.Value);
        Assert.Contains(message, alert.Value);
        var formStart = html.IndexOf("<form", StringComparison.Ordinal);
        var inputStart = html.IndexOf(input, StringComparison.Ordinal);
        var saveStart = html.IndexOf("Lưu hồ sơ", StringComparison.Ordinal);
        Assert.True(alert.Index > inputStart && alert.Index < saveStart, "The error belongs immediately after the avatar field, before Save.");
        Assert.DoesNotContain(message, html[..formStart]);
        Assert.DoesNotContain(message, html[(alert.Index + alert.Length)..]);
    }

    private static void AssertGlobalError(string html, string message)
    {
        var profileStart = html.IndexOf("id=\"pt-basic-profile\"", StringComparison.Ordinal);
        Assert.Contains(message, html[..profileStart]);
        Assert.DoesNotContain(message, html[profileStart..]);
        Assert.DoesNotContain("aria-invalid=\"true\"", html);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    private sealed class ProfileHost : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly CapturingActivator _activator = new();
        private HtmlRootComponent _view;
        private TrainerPublicDetail _detail = new(new TrainerProfileResponse
        {
            Id = 7, UserId = "trainer-7", FullName = "Coach", Bio = "Original biography", AvatarUrl = OldAvatar,
            TrainerRole = "PT", IsAcceptingNewMembers = true
        }, [], []);
        public List<UpdateTrainerProfileRequest> Updates { get; } = [];
        public ToastService Toasts { get; } = new();
        private MyTrainerProfile Page => _activator.Instances.OfType<MyTrainerProfile>().Single();

        public ProfileHost(Func<HttpResponseMessage>? updateResponse = null)
        {
            var storage = new BrowserRuntime();
            var http = new HttpClient(new Handler(async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/trainers/me" && request.Method == HttpMethod.Put)
                {
                    var update = (await request.Content!.ReadFromJsonAsync<UpdateTrainerProfileRequest>())!;
                    Updates.Add(update);
                    if (updateResponse is not null) return updateResponse();
                    _detail = _detail with { Profile = _detail.Profile with
                    {
                        Bio = update.Bio ?? _detail.Profile.Bio,
                        AvatarUrl = update.Avatar is null ? _detail.Profile.AvatarUrl : NewAvatar
                    } };
                    return Json(true);
                }
                return request.RequestUri.AbsolutePath switch
                {
                    "/api/trainers/me" => Json(_detail),
                    "/api/users/me" => Json(new UserProfileResponse("trainer-7", "Coach", "coach@example.test", "0912345678", OldAvatar, "Active", ["PT"], DateTime.UtcNow)),
                    "/api/trainers/me/applications" => Json(new PagedResponse<TrainerApplicationResponse>(1, 10, 0, [])),
                    _ => throw new InvalidOperationException("Unexpected endpoint: " + request.RequestUri)
                };
            })) { BaseAddress = new Uri("https://api.example.test/") };
            var session = new AuthSession(storage, new AuthApiClient(http));
            _services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime>(storage)
                .AddSingleton(new TrainerApiClient(http, session)).AddSingleton(Toasts)
                .AddSingleton<IComponentActivator>(_activator).BuildServiceProvider();
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public Task RenderAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
            _view = await _renderer.RenderComponentAsync<MyTrainerProfile>());

        public Task SelectAsync(IBrowserFile file) => _renderer.Dispatcher.InvokeAsync(() =>
            _activator.Instances.OfType<InputFile>().Last().OnChange.InvokeAsync(new InputFileChangeEventArgs([file])));

        public Task ChangeBioAsync(string bio) => _renderer.Dispatcher.InvokeAsync(() =>
            typeof(MyTrainerProfile).GetField("_bio", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Page, bio));

        public Task SaveAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var save = typeof(MyTrainerProfile).GetMethod("SaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<Func<Task>>(Page);
            await ((IHandleEvent)Page).HandleEventAsync(new EventCallbackWorkItem(save), null);
        });

        public Task<string> HtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_view.ToHtmlString()));

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class BrowserFile(string name, byte[] content, long? size = null, Exception? readFailure = null) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => size ?? content.Length;
        public string ContentType => "image/png";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize) throw new IOException("File exceeds allowed size.");
            if (readFailure is not null) throw readFailure;
            return new MemoryStream(content, writable: false);
        }
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Instances { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Instances.Add(component);
            return component;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class BrowserRuntime : IJSRuntime
    {
        private readonly string _session = JsonSerializer.Serialize(new StoredAuthSession(
            new LoginResponse("access", "refresh", "Bearer", 1800, "trainer-7", "Coach", "coach@example.test", ["PT"]),
            DateTimeOffset.UtcNow.AddMinutes(20), false));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(identifier == "vfmsAuthStorage.read" ? (TValue)(object)_session : default!);
    }
}
