using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 13 (System config &amp; Analytics).</summary>
public static class SystemAdministrationModule
{
    public static IServiceCollection AddSystemAdministrationModule(this IServiceCollection services)
    {
        services.AddScoped<IBannedWordRepository, BannedWordRepository>();
        services.AddScoped<IBannedWordService, BannedWordService>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IGymSettingsRepository, GymSettingsRepository>();
        services.AddScoped<IGymSettingsService, GymSettingsService>();
        services.AddScoped<IBusinessReportRepository, BusinessReportRepository>();
        services.AddScoped<IBusinessReportService, BusinessReportService>();
        return services;
    }
}
