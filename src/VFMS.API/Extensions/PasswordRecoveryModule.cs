using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Infrastructure.Email;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class PasswordRecoveryModule
{
    public static IServiceCollection AddPasswordRecovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection("PasswordReset").Get<PasswordResetSettings>() ?? new());
        services.AddSingleton(TimeProvider.System);
        services.Configure<SmtpSettings>(configuration.GetSection("Smtp"));
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddSingleton<PasswordResetEmailQueue>();
        services.AddSingleton<IPasswordResetEmailQueue>(provider => provider.GetRequiredService<PasswordResetEmailQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<PasswordResetEmailQueue>());
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    ErrorCode = "AUTH_RECOVERY_RATE_LIMITED",
                    ErrorMessage = "Too many recovery attempts. Please try again later."
                }, cancellationToken);
            };
        });
        return services;
    }
}
