using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using VFMS.Client;
using VFMS.Client.Auth;
using VFMS.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped(_ => new AuthApiClient(CreateApiHttpClient()));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
builder.Services.AddScoped(sp => new MembershipPlanApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new SystemAdministrationApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new OperationsApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));
builder.Services.AddScoped(sp => new CompetitionApiClient(CreateApiHttpClient(), sp.GetRequiredService<AuthSession>()));

await builder.Build().RunAsync();

HttpClient CreateApiHttpClient()
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
    return new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };
}
