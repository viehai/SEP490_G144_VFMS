using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

public static class ConsultationModule
{
    public static IServiceCollection AddConsultationModule(this IServiceCollection services)
    {
        services.AddScoped<IConsultationRepository, ConsultationRepository>();
        services.AddScoped<IConsultationService, ConsultationService>();
        return services;
    }
}
