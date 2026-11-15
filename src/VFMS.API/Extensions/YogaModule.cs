using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 7 (Yoga and Group Class Management).</summary>
public static class YogaModule
{
    public static IServiceCollection AddYogaModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IYogaRepository, YogaRepository>();
        services.AddScoped<IYogaStudioService, YogaStudioService>();
        services.AddScoped<IYogaClassService, YogaClassService>();
        services.AddScoped<IYogaBookingRepository, YogaBookingRepository>();
        services.AddScoped<IYogaBookingService, YogaBookingService>();
        services.AddScoped<IYogaAttendanceService, YogaAttendanceService>();
        return services;
    }
}
