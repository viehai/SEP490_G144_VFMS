using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class MembershipFreezeReviewModule
{
    public static IServiceCollection AddMembershipFreezeReviewModule(this IServiceCollection services)
    {
        services.AddScoped<IMembershipFreezeReviewRepository, MembershipFreezeReviewRepository>();
        services.AddScoped<IMembershipFreezeReviewService, MembershipFreezeReviewService>();
        return services;
    }
}
