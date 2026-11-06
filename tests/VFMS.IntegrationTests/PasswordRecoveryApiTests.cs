using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VFMS.API.Controllers;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Identity;

namespace VFMS.IntegrationTests;

public sealed class PasswordRecoveryApiTests
{
    [Fact]
    public async Task AnonymousRequestIsAcceptedWithoutDatabaseLookupAndIsRateLimited()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        string? firstBody = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest(attempt % 2 == 0 ? "registered@example.com" : "unknown@example.com"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadAsStringAsync();
            firstBody ??= body;
            Assert.Equal(firstBody, body);
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        }
        using var limited = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("unknown@example.com"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task ApiRejectsInvalidEmailAndWeakResetPasswordBeforeExecutingWorkflow()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var invalidEmail = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("invalid"));
        using var invalidPassword = await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(new string('A', 64), "weak", "weak"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPassword.StatusCode);
        Assert.Contains("NewPassword", await invalidPassword.Content.ReadAsStringAsync());
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordReset:ResetUrl"] = "https://fitness.example/auth/reset-password"
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(PasswordRecoveryController).Assembly);
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<ForgotPasswordRequestValidator>();
        builder.Services.AddPasswordRecovery(builder.Configuration);
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddScoped<IPasswordResetRepository, NoDatabaseRepository>();
        builder.Services.AddSingleton<IEmailService, NoSendEmail>();
        builder.Services.AddSingleton<IPasswordResetEmailQueue, NoSendQueue>();
        var app = builder.Build();
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return next(context); });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class NoSendEmail : IEmailService
    {
        public bool IsConfigured => true;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Real email must not be sent by HTTP tests.");
    }
    private sealed class NoSendQueue : IPasswordResetEmailQueue { public bool TryEnqueue(string email) => true; }
    private sealed class NoDatabaseRepository : IPasswordResetRepository
    {
        private static Exception UnexpectedAccess() => new InvalidOperationException("The request path must not look up an account.");
        public Task<ApplicationUser?> FindEligibleUserAsync(string email, CancellationToken ct) => throw UnexpectedAccess();
        public Task<bool> TryIssueAsync(string userId, string oldHash, string hash, DateTime expiresAt, DateTime cutoff, DateTime now, CancellationToken ct) => throw UnexpectedAccess();
        public Task<ApplicationUser?> FindByTokenAsync(string hash, CancellationToken ct) => throw UnexpectedAccess();
        public Task InvalidateAsync(string userId, string hash, DateTime now, CancellationToken ct) => throw UnexpectedAccess();
        public Task<bool> TryResetAsync(string userId, string hash, string expectedHash, string newHash, DateTime now, CancellationToken ct) => throw UnexpectedAccess();
    }
}
