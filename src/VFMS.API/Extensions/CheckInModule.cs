using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.API.Security;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 8 (Access Control, Check-in and Biometrics).</summary>
public static class CheckInModule
{
    public static IServiceCollection AddCheckInModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
            keysPath = Path.Combine(environment.ContentRootPath, "keys");
        services.AddDataProtection()
            .SetApplicationName("VFMS")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFaceTemplateProtector, DataProtectionFaceTemplateProtector>();
        services.AddScoped<ICheckInRepository, CheckInRepository>();
        services.AddScoped<IFaceProfileService, FaceProfileService>();
        services.AddScoped<IActiveCheckInService, ActiveCheckInService>();
        return services;
    }
}
