using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class LockerModule
{
    public static IServiceCollection AddLockerModule(this IServiceCollection services)
    {
        services.AddScoped<ILockerRepository, LockerRepository>();
        // One scoped instance serves both the Locker use cases and SePay fulfillment.
        services.AddScoped<LockerService>();
        services.AddScoped<ILockerService>(provider => provider.GetRequiredService<LockerService>());
        services.AddScoped<ILockerPaymentFulfillment>(provider => provider.GetRequiredService<LockerService>());
        services.AddHostedService<LockerWorker>();
        return services;
    }
}
