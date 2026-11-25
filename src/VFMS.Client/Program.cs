using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using VFMS.Client;
using VFMS.Client.Auth;
using VFMS.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<MembershipCart>();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped(_ => new AuthApiClient(CreateApiHttpClient()));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
builder.Services.AddScoped(sp => new MembershipPlanApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>(),
    sp.GetRequiredService<IJSRuntime>()));
// 40 s: AI workout plan generation can take longer than the 30 s default.
builder.Services.AddScoped(sp => new VfmsApiClient(CreateApiHttpClient(40), sp.GetRequiredService<AuthSession>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VfmsApiClient>>()));
builder.Services.AddScoped(sp => new SystemAdministrationApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new OperationsApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new CompetitionApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new PromotionApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));

builder.Services.AddScoped(sp => new TrainerApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new PersonalTrainingApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));

var host = builder.Build();
// An open or cached index.html can predate the JavaScript functions this client calls.
var js = host.Services.GetRequiredService<IJSRuntime>();
var clientVersion = typeof(App).Module.ModuleVersionId.ToString("N");
await Task.WhenAll(
    js.InvokeVoidAsync("import", $"./js/auth.js?v={clientVersion}").AsTask(),
    js.InvokeVoidAsync("import", $"./js/membership-updates.js?v={clientVersion}").AsTask());
await host.Services.GetRequiredService<AuthSession>().WatchBrowserSessionAsync();
await host.RunAsync();

HttpClient CreateApiHttpClient(int timeoutSeconds = 30)
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
    return new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(timeoutSeconds)
    };
}
