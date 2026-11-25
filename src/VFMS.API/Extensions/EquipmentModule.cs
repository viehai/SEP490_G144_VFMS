using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 11 (Manage Gym Equipment).</summary>
public static class EquipmentModule
{
    public static IServiceCollection AddEquipmentModule(this IServiceCollection services)
    {
        services.AddScoped<IEquipmentRepository, EquipmentRepository>();
        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<IEquipmentIssueService, EquipmentIssueService>();
        services.AddScoped<IPurchaseProposalRepository, PurchaseProposalRepository>();
        services.AddScoped<IPurchaseProposalService, PurchaseProposalService>();
        services.AddScoped<IGymDetailsRepository, GymDetailsRepository>();
        services.AddScoped<IGymDetailsService, GymDetailsService>();
        services.AddScoped<IUploadedImageRepository, UploadedImageRepository>();
        services.AddScoped<IUploadedImageService, UploadedImageService>();
        return services;
    }
}
