using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using VFMS.Client;
using VFMS.Client.Auth;
using VFMS.Client.Components.Users;
using VFMS.Client.Pages.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Users;
using Xunit;
using ManagerCreateCustomerPage = VFMS.Client.Pages.Manager.CreateCustomer;
using StaffCreateCustomerPage = VFMS.Client.Pages.Staff.CreateCustomer;

namespace VFMS.ClientTests;

public sealed class CreateCustomerAccountPageTests
{
    private static readonly DateTime SampleExpiryUtc = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Pages_ExposeExpectedRoutesAndStaffManagerAuthorization()
    {
        var staffRoute = Assert.Single(typeof(StaffCreateCustomerPage).GetCustomAttributes<RouteAttribute>(true));
        var staffAuth = Assert.Single(typeof(StaffCreateCustomerPage).GetCustomAttributes<AuthorizeAttribute>(true));
        Assert.Equal("/staff/customers/create", staffRoute.Template);
        Assert.Equal("Staff,Manager", staffAuth.Roles);

        var managerRoute = Assert.Single(typeof(ManagerCreateCustomerPage).GetCustomAttributes<RouteAttribute>(true));
        var managerAuth = Assert.Single(typeof(ManagerCreateCustomerPage).GetCustomAttributes<AuthorizeAttribute>(true));
        Assert.Equal("/manager/customers/create", managerRoute.Template);
        Assert.Equal("Staff,Manager", managerAuth.Roles);
    }

    [Theory]
    [InlineData("Staff", "/staff/customers/create", true)]
    [InlineData("Manager", "/manager/customers/create", true)]
    [InlineData("Manager", "/staff/customers/create", true)]
    [InlineData("Admin", "/staff/customers/create", false)]
    [InlineData("PT", "/staff/customers/create", false)]
    [InlineData("Member", "/staff/customers/create", false)]
    [InlineData("Customer", "/staff/customers/create", false)]
    [InlineData("Admin", "/manager/customers/create", false)]
    [InlineData("Customer", "/manager/customers/create", false)]
    [InlineData(null, "/staff/customers/create", false)]
    public async Task RouterAuthorization_AllowsOnlyStaffAndManager(string? role, string route, bool expectedAllowed)
    {
        await using var host = CreateRouterHost(role, route, _ => Task.FromResult(JsonResponse(new { })));
        var rendered = await host.Renderer.Dispatcher.InvokeAsync(async () => await host.Renderer.RenderComponentAsync<App>());
        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));

        if (expectedAllowed)
        {
            Assert.Contains("Tạo tài khoản khách hàng", html);
            Assert.Contains("id=\"customer-full-name\"", html);
            Assert.DoesNotContain("BẢN XEM TRƯỚC", html);
        }
        else
        {
            Assert.DoesNotContain("id=\"customer-full-name\"", html);
            Assert.DoesNotContain("Nhập thông tin tại quầy lễ tân", html);
        }
    }

    [Fact]
    public async Task FormRendering_ContainsRequiredFieldsOnlyAndNoRoleOrPasswordInputs()
    {
        await using var host = CreateFormHost(_ => Task.FromResult(JsonResponse(new { })));
        var html = await host.RenderHtmlAsync();

        Assert.Contains("id=\"customer-full-name\"", html);
        Assert.Contains("id=\"customer-email\"", html);
        Assert.Contains("id=\"customer-phone\"", html);
        Assert.DoesNotContain("<select", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tài khoản được tạo với vai trò Customer.", html);
        Assert.Contains("48 giờ", html);
    }

    [Fact]
    public async Task SubmitValidForm_PostsExactPayloadShowsSuccessAndPreservesStaffSession()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var expectedResponse = new WalkInCustomerResponse(
            "cust-001", "Nguyễn Văn An", "an.nguyen@example.com", "0912345678",
            "PendingVerification", ["Customer"], SampleExpiryUtc, true,
            "Customer account created.");

        await using var host = CreateFormHost(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(expectedResponse, HttpStatusCode.Created);
        });

        var initialSessionJson = host.Storage.Json;
        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, "Nguyễn Văn An", "an.nguyen@example.com", "0912345678");
        await InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/users/walk-in-customer", capturedRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("staff-access-token", capturedRequest.Headers.Authorization?.Parameter);

        using var payloadDoc = JsonDocument.Parse(capturedBody!);
        var payloadProps = payloadDoc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["email", "fullName", "phoneNumber"], payloadProps);

        Assert.Contains("Tạo tài khoản khách hàng thành công", html);
        Assert.Contains("Nguyễn Văn An", html);
        Assert.Contains("an.nguyen@example.com", html);
        Assert.Contains("0912345678", html);
        Assert.Contains("Customer", html);
        Assert.Contains("Chờ thiết lập mật khẩu", html);
        Assert.DoesNotContain("Đang hoạt động", html);
        Assert.Contains("Liên kết thiết lập mật khẩu đã được gửi đến email của khách hàng. Liên kết có hiệu lực trong 48 giờ.", html);

        // Staff session remains completely unchanged.
        Assert.Equal(initialSessionJson, host.Storage.Json);
        var authState = await host.Session.GetAuthenticationStateAsync();
        Assert.True(authState.User.Identity?.IsAuthenticated);
        Assert.True(authState.User.IsInRole("Staff"));
        Assert.Equal("Staff Front Desk", authState.User.Identity?.Name);
    }

    [Fact]
    public async Task DuplicateEmail409_ShowsVietnameseErrorAndKeepsFormValues()
    {
        await using var host = CreateFormHost(_ => Task.FromResult(JsonResponse(
            new { status = 409, code = "AUTH_EMAIL_EXISTS", detail = "Email is already in use." },
            HttpStatusCode.Conflict)));

        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, "Nguyễn Văn An", "dup@example.com", "0912345678");
        await InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("Email này đã được sử dụng bởi một tài khoản khác.", html);
        Assert.DoesNotContain("Tạo tài khoản khách hàng thành công", html);
        Assert.Contains("dup@example.com", html);
    }

    [Fact]
    public async Task DuplicatePhone409_ShowsVietnameseErrorAndKeepsFormValues()
    {
        await using var host = CreateFormHost(_ => Task.FromResult(JsonResponse(
            new { status = 409, code = "USER_PHONE_EXISTS", detail = "A user with this phone number already exists." },
            HttpStatusCode.Conflict)));

        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, "Nguyễn Văn An", "unique@example.com", "0912-345-678");
        await InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("Số điện thoại này đã được sử dụng bởi một tài khoản khác.", html);
        Assert.DoesNotContain("Tạo tài khoản khách hàng thành công", html);
        Assert.Contains("0912-345-678", html);
    }

    [Fact]
    public async Task SetupEmailUnavailable503_ShowsErrorWithoutFakeSuccess()
    {
        await using var host = CreateFormHost(_ => Task.FromResult(JsonResponse(
            new { status = 503, code = "CUSTOMER_SETUP_EMAIL_UNAVAILABLE", detail = "SMTP not configured." },
            HttpStatusCode.ServiceUnavailable)));

        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, "Nguyễn Văn An", "an@example.com", "0912345678");
        await InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("Dịch vụ gửi email thiết lập mật khẩu hiện chưa khả dụng", html);
        Assert.DoesNotContain("Tạo tài khoản khách hàng thành công", html);
        Assert.Contains("an@example.com", html);
    }

    [Fact]
    public async Task EmailFailureAfterCreation_ShowsResendAndHandlesResendSuccessAndFailure()
    {
        var resendCalls = 0;
        var resendShouldFail = true;
        var createdResponse = new WalkInCustomerResponse(
            "cust-retry-1", "Trần Bảo", "bao@example.com", "0988112233",
            "PendingVerification", ["Customer"], null, false,
            "Created without email.");

        await using var host = CreateFormHost(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/users/walk-in-customer")
                return Task.FromResult(JsonResponse(createdResponse, HttpStatusCode.Created));

            if (request.RequestUri.AbsolutePath == "/api/users/cust-retry-1/resend-setup-email")
            {
                resendCalls++;
                if (resendShouldFail)
                {
                    return Task.FromResult(JsonResponse(
                        new { status = 503, code = "CUSTOMER_SETUP_EMAIL_FAILED", detail = "Delivery failed." },
                        HttpStatusCode.ServiceUnavailable));
                }

                return Task.FromResult(JsonResponse(new ResendSetupEmailResponse(
                    "cust-retry-1", "bao@example.com", SampleExpiryUtc, "Resent.")));
            }

            throw new InvalidOperationException("Unexpected route: " + request.RequestUri.AbsolutePath);
        });

        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, "Trần Bảo", "bao@example.com", "0988112233");
        await InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        var createdHtml = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("Tạo tài khoản khách hàng thành công", createdHtml);
        Assert.Contains("chưa gửi được email thiết lập mật khẩu", createdHtml);
        Assert.Contains("Gửi lại email thiết lập mật khẩu", createdHtml);

        // Resend fails -> shows error alert
        await InvokeNonPublicAsync(host.Renderer, host.Form, "ResendSetupEmailAsync");
        var failedResendHtml = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Equal(1, resendCalls);
        Assert.Contains("Không thể gửi email thiết lập mật khẩu lúc này", failedResendHtml);

        // Resend succeeds -> shows green confirmation
        resendShouldFail = false;
        await InvokeNonPublicAsync(host.Renderer, host.Form, "ResendSetupEmailAsync");
        var successResendHtml = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Equal(2, resendCalls);
        Assert.Contains("Đã gửi lại liên kết thiết lập mật khẩu mới", successResendHtml);
    }

    [Fact]
    public async Task DoubleSubmitWhileInFlight_SendsOnlyOneCreateRequest()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        await using var host = CreateFormHost(_ =>
        {
            Interlocked.Increment(ref callCount);
            return gate.Task;
        });

        await host.RenderComponentAsync();
        SetModel(host.Form, "Lê Minh", "minh@example.com", "0911223344");

        var first = InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");
        var second = InvokeNonPublicAsync(host.Renderer, host.Form, "SubmitAsync");

        gate.SetResult(JsonResponse(new WalkInCustomerResponse(
            "cust-once", "Lê Minh", "minh@example.com", "0911223344",
            "PendingVerification", ["Customer"], SampleExpiryUtc, true, "Created."), HttpStatusCode.Created));

        await Task.WhenAll(first, second);
        Assert.Equal(1, callCount);
    }

    [Theory]
    [InlineData("an@example.com", null)]
    [InlineData(null, "0912-345-678")]
    public async Task FreshForm_FindsPendingCustomerAndOnlyResendsAfterExplicitAction(string? email, string? phone)
    {
        var paths = new List<string>();
        await using var host = CreateFormHost(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path == "/api/users/find-pending-customer")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("staff-access-token", request.Headers.Authorization?.Parameter);
                Assert.Equal(new FindPendingCustomerRequest(email, phone),
                    await request.Content!.ReadFromJsonAsync<FindPendingCustomerRequest>());
                return JsonResponse(new WalkInCustomerResponse(
                    "recovered", "Nguyễn Văn An", "an@example.com", "0912345678",
                    "PendingVerification", ["Customer"], null, false, "Found."));
            }

            Assert.Equal("/api/users/recovered/resend-setup-email", path);
            return JsonResponse(new ResendSetupEmailResponse(
                "recovered", "an@example.com", SampleExpiryUtc, "Resent."));
        });

        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, string.Empty, email ?? string.Empty, phone ?? string.Empty);
        await InvokeNonPublicAsync(host.Renderer, host.Form, "FindPendingCustomerAsync");
        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));

        Assert.Single(paths);
        Assert.Contains("Đã tìm thấy tài khoản chờ thiết lập mật khẩu", html);
        Assert.Contains("Gửi lại email thiết lập mật khẩu", html);
        Assert.DoesNotContain("Tạo tài khoản khách hàng thành công", html);
        Assert.DoesNotContain("chưa gửi được email thiết lập mật khẩu", html);

        await InvokeNonPublicAsync(host.Renderer, host.Form, "ResendSetupEmailAsync");
        html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));

        Assert.Equal(2, paths.Count);
        Assert.Contains("Đã gửi lại liên kết thiết lập mật khẩu mới", html);
    }

    [Fact]
    public async Task FreshForm_IneligibleLookupDoesNotOfferResend()
    {
        await using var host = CreateFormHost(request =>
        {
            Assert.Equal("/api/users/find-pending-customer", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(new { code = "USER_SETUP_RESEND_NOT_ELIGIBLE" }, HttpStatusCode.NotFound));
        });
        var rendered = await host.RenderComponentAsync();
        SetModel(host.Form, string.Empty, "an@example.com", string.Empty);
        await InvokeNonPublicAsync(host.Renderer, host.Form, "FindPendingCustomerAsync");

        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("Không tìm thấy tài khoản khách hàng đang chờ thiết lập mật khẩu.", html);
        Assert.DoesNotContain("Gửi lại email thiết lập mật khẩu", html);
    }

    [Theory]
    [InlineData(null, false, "không còn trong phiên này")]
    [InlineData(null, true, "không còn trong phiên này")]
    [InlineData("#token=malformed", false, "không hợp lệ")]
    public async Task SetupPage_MissingQueryOnlyOrMalformedLinkHidesPasswordFields(
        string? fragment, bool includeQuery, string expectedMessage)
    {
        var route = "/auth/setup-password";
        if (includeQuery)
            route += "?token=" + new string('A', 64);

        await using var host = CreateRouterHost(null, route,
            _ => throw new InvalidOperationException("An unusable link must not send an API request."), fragment);
        var rendered = await host.Renderer.Dispatcher.InvokeAsync(() => host.Renderer.RenderComponentAsync<App>());
        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));

        Assert.Contains(expectedMessage, html);
        Assert.Contains("mở lại liên kết từ email", html);
        Assert.DoesNotContain("id=\"setup-password\"", html);
    }

    [Fact]
    public async Task SetupPage_ConsumedLinkHidesPasswordFieldsAndSupportsRecovery()
    {
        var token = new string('A', 64);
        await using var host = CreateRouterHost(null, "/auth/setup-password", async request =>
        {
            Assert.Equal("/api/auth/setup-password", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(token, (await request.Content!.ReadFromJsonAsync<SetupPasswordRequest>())?.Token);
            return JsonResponse(new { code = "AUTH_INVALID_SETUP_TOKEN" }, HttpStatusCode.BadRequest);
        }, "#token=" + token);
        var rendered = await host.Renderer.Dispatcher.InvokeAsync(() => host.Renderer.RenderComponentAsync<App>());
        var html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("id=\"setup-password\"", html);
        Assert.DoesNotContain(token, html);

        var setup = host.Activator.Instances.OfType<SetupPassword>().Single();
        var model = typeof(SetupPassword).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(setup)!;
        model.GetType().GetProperty("NewPassword")!.SetValue(model, "Selected1!");
        model.GetType().GetProperty("ConfirmPassword")!.SetValue(model, "Selected1!");
        await InvokeNonPublicAsync(host.Renderer, setup, "SubmitAsync");

        html = WebUtility.HtmlDecode(await host.Renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Contains("đã được sử dụng", html);
        Assert.Contains("liên hệ quầy lễ tân", html);
        Assert.DoesNotContain("id=\"setup-password\"", html);
        Assert.DoesNotContain(token, html);
    }

    private static void SetModel(CreateCustomerAccountForm form, string fullName, string email, string phoneNumber)
    {
        var modelField = typeof(CreateCustomerAccountForm).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var model = modelField.GetValue(form)!;
        var modelType = model.GetType();
        modelType.GetProperty("FullName")!.SetValue(model, fullName);
        modelType.GetProperty("Email")!.SetValue(model, email);
        modelType.GetProperty("PhoneNumber")!.SetValue(model, phoneNumber);
    }

    private static Task InvokeNonPublicAsync(HtmlRenderer renderer, ComponentBase instance, string methodName) =>
        renderer.Dispatcher.InvokeAsync(async () =>
        {
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(instance, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, null);
        });

    private static HttpResponseMessage JsonResponse(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(payload) };

    private static FormTestHost CreateFormHost(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var identity = new LoginResponse("staff-access-token", "staff-refresh-token", "Bearer", 1800,
            "staff-1", "Staff Front Desk", "staff@vfms.test", ["Staff"]);
        var storage = new TestStorage(JsonSerializer.Serialize(
            new StoredAuthSession(identity, DateTimeOffset.UtcNow.AddMinutes(20), true)));
        var authHttp = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(new { }))))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var apiHttp = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var session = new AuthSession(storage, new AuthApiClient(authHttp));
        var api = new VfmsApiClient(apiHttp, session);
        var activator = new CapturingActivator();

        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorizationCore()
            .AddSingleton(session)
            .AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton<IJSRuntime>(storage)
            .AddSingleton(api)
            .AddSingleton<IComponentActivator>(activator)
            .BuildServiceProvider();

        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return new FormTestHost(services, renderer, activator, session, storage, authHttp, apiHttp);
    }

    private static RouterTestHost CreateRouterHost(
        string? role,
        string route,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler,
        string? setupFragment = null)
    {
        var json = role is null
            ? null
            : JsonSerializer.Serialize(new StoredAuthSession(
                new LoginResponse("token", "refresh", "Bearer", 1800, "actor-1", "Actor", "actor@vfms.test", [role]),
                DateTimeOffset.UtcNow.AddMinutes(20), true));
        var storage = new TestStorage(json, setupFragment);
        var authHttp = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var apiHttp = new HttpClient(new StubHandler(request => request.RequestUri!.AbsolutePath == "/api/dashboards/notifications"
            ? Task.FromResult(JsonResponse(new { unread = 0, recent = Array.Empty<object>() }))
            : handler(request)))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var authApi = new AuthApiClient(authHttp);
        var session = new AuthSession(storage, authApi);
        var api = new VfmsApiClient(apiHttp, session);
        var navigation = new TestNavigation(route);
        var activator = new CapturingActivator();

        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorizationCore()
            .AddSingleton(authApi)
            .AddSingleton(session)
            .AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton<IJSRuntime>(storage)
            .AddSingleton(api)
            .AddSingleton(new ToastService())
            .AddSingleton<NavigationManager>(navigation)
            .AddSingleton<INavigationInterception>(new NoNavigationInterception())
            .AddSingleton<IScrollToLocationHash>(new NoScrollToLocationHash())
            .AddSingleton<IComponentActivator>(activator)
            .BuildServiceProvider();

        var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return new RouterTestHost(services, renderer, activator, authHttp, apiHttp);
    }

    private sealed class FormTestHost(
        ServiceProvider services,
        HtmlRenderer renderer,
        CapturingActivator activator,
        AuthSession session,
        TestStorage storage,
        HttpClient authHttp,
        HttpClient apiHttp) : IAsyncDisposable
    {
        public HtmlRenderer Renderer => renderer;
        public AuthSession Session => session;
        public TestStorage Storage => storage;
        public CreateCustomerAccountForm Form => activator.Instances.OfType<CreateCustomerAccountForm>().Single();

        public Task<Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent> RenderComponentAsync() =>
            renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<CreateCustomerAccountForm>());

        public async Task<string> RenderHtmlAsync()
        {
            var rendered = await RenderComponentAsync();
            return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        }

        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
            authHttp.Dispose();
            apiHttp.Dispose();
        }
    }

    private sealed class RouterTestHost(
        ServiceProvider services,
        HtmlRenderer renderer,
        CapturingActivator activator,
        HttpClient authHttp,
        HttpClient apiHttp) : IAsyncDisposable
    {
        public HtmlRenderer Renderer => renderer;
        public CapturingActivator Activator => activator;

        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
            authHttp.Dispose();
            apiHttp.Dispose();
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

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(string path) => Initialize("https://client.example.test/", "https://client.example.test" + path);
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }
    }

    private sealed class NoNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    private sealed class NoScrollToLocationHash : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class TestStorage(string? json, string? setupFragment = null) : IJSRuntime
    {
        public string? Json { get; private set; } = json;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "vfmsCustomerSetup.takeFragment")
            {
                var fragment = setupFragment;
                setupFragment = null;
                return ValueTask.FromResult((TValue)(object?)fragment!);
            }

            if (identifier == "vfmsAuthStorage.read")
                return ValueTask.FromResult((TValue)(object?)Json!);
            if (identifier == "vfmsAuthStorage.write")
                Json = args?[0]?.ToString();
            else if (identifier == "vfmsAuthStorage.clear")
                Json = null;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
