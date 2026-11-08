using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class MembershipPlanModule
{
    public static IServiceCollection AddMembershipPlanModule(this IServiceCollection services)
    {
        services.AddScoped<IMembershipPlanRepository, MembershipPlanRepository>();
        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        return services;
    }
}
