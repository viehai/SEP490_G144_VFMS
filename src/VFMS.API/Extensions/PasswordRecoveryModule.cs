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
    public const string CustomerSetupPolicy = "customer-account-setup";
    public const string CustomerResendPolicy = "customer-setup-resend";
    public const string CustomerLookupPolicy = "customer-setup-lookup";

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
            options.AddPolicy("password-recovery", context => Limit(context, 10, TimeSpan.FromMinutes(15)));
            options.AddPolicy(CustomerSetupPolicy, context => Limit(context, 10, TimeSpan.FromMinutes(15)));
            options.AddPolicy(CustomerResendPolicy, context => Limit(context, 10, TimeSpan.FromMinutes(15)));
            options.AddPolicy(CustomerLookupPolicy, context => Limit(context, 30, TimeSpan.FromMinutes(15)));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var code = policy switch
                {
                    CustomerSetupPolicy => "AUTH_SETUP_RATE_LIMITED",
                    CustomerResendPolicy => "CUSTOMER_SETUP_RESEND_RATE_LIMITED",
                    CustomerLookupPolicy => "CUSTOMER_SETUP_LOOKUP_RATE_LIMITED",
                    _ => "AUTH_RECOVERY_RATE_LIMITED"
                };
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    ErrorCode = code,
                    ErrorMessage = "Too many attempts. Please try again later."
                }, cancellationToken);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Limit(HttpContext context, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
}
