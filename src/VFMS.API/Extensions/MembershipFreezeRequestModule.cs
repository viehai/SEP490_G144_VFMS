using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class MembershipFreezeRequestModule
{
    public static IServiceCollection AddMembershipFreezeRequestModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IMembershipFreezeRequestRepository, MembershipFreezeRequestRepository>();
        services.AddScoped<IMembershipFreezeRequestService, MembershipFreezeRequestService>();
        return services;
    }
}
