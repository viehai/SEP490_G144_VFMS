using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class MembershipFreezeLifecycleModule
{
    public static IServiceCollection AddMembershipFreezeLifecycleModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMembershipFreezeLifecycleRepository, MembershipFreezeLifecycleRepository>();
        services.AddScoped<IMembershipFreezeLifecycleService, MembershipFreezeLifecycleService>();
        services.AddScoped<IMembershipAccessRepository, MembershipAccessRepository>();
        services.AddScoped<IMembershipAccessService, MembershipAccessService>();
        services.AddHostedService<MembershipFreezeTransitionWorker>();
        return services;
    }
}
