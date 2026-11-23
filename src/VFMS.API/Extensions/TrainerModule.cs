using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Application.Settings;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class TrainerModule
{
    public static IServiceCollection AddTrainerModule(this IServiceCollection services)
    {
        services.AddScoped<ITrainerRepository, TrainerRepository>();
        services.AddScoped<ITrainerService, TrainerService>();
        // Read per scope so configuration reloads reach the next email.
        services.AddScoped(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return new TrainerSetupLinkSettings
            {
                SetupPageUrl = configuration["TrainerMail:SetupPageUrl"],
                ResetUrl = configuration["PasswordReset:ResetUrl"]
            };
        });
        services.AddHostedService<TrainerEmailWorker>();
        return services;
    }
}
