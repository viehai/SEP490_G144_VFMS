using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class MembershipFreezePolicyModule
{
    public static IServiceCollection AddMembershipFreezePolicyModule(this IServiceCollection services)
    {
        services.AddScoped<IMembershipFreezePolicyRepository, MembershipFreezePolicyRepository>();
        services.AddScoped<IMembershipFreezePolicyService, MembershipFreezePolicyService>();
        return services;
    }
}
