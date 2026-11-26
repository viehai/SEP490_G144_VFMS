using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class PersonalTrainingModule
{
    public static IServiceCollection AddPersonalTrainingModule(this IServiceCollection services)
    {
        services.AddScoped<IPersonalTrainingRepository, PersonalTrainingRepository>();
        // One scoped instance serves both the PT use cases and SePay fulfillment.
        services.AddScoped<PersonalTrainingService>();
        services.AddScoped<IPersonalTrainingService>(provider => provider.GetRequiredService<PersonalTrainingService>());
        services.AddScoped<IPTPaymentFulfillment>(provider => provider.GetRequiredService<PersonalTrainingService>());
        services.AddHostedService<PersonalTrainingWorker>();
        return services;
    }
}
