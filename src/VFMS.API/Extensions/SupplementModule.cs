using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 12 (Commerce, POS &amp; Supplement Store).</summary>
public static class SupplementModule
{
    public static IServiceCollection AddSupplementModule(this IServiceCollection services)
    {
        services.AddScoped<ISupplementRepository, SupplementRepository>();
        services.AddScoped<ISupplementProductService, SupplementProductService>();
        services.AddScoped<ISupplementOrderService, SupplementOrderService>();
        return services;
    }
}
