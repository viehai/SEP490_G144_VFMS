using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 6 (Competitions).</summary>
public static class CompetitionModule
{
    public static IServiceCollection AddCompetitionModule(this IServiceCollection services)
    {
        services.AddScoped<ICompetitionRepository, CompetitionRepository>();
        services.AddScoped<ICompetitionService, CompetitionService>();
        return services;
    }
}
