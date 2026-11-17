using VFMS.Application.Interfaces;
using VFMS.Infrastructure.Lockers;

namespace VFMS.API.Extensions;

public static class LockerModule
{
    public static IServiceCollection AddLockerModule(this IServiceCollection services)
    {
        services.AddScoped<LockerService>();
        services.AddScoped<ILockerPaymentFulfillment>(provider => provider.GetRequiredService<LockerService>());
        services.AddHostedService<LockerWorker>();
        return services;
    }
}
