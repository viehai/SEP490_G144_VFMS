using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class PersonalTrainingModule
{
    public static IServiceCollection AddPersonalTrainingModule(this IServiceCollection services)
    {
        services.AddScoped<IPersonalTrainingStore, PersonalTrainingStore>();
        services.AddScoped<PersonalTrainingService>();
        services.AddScoped<IPTPaymentFulfillment>(provider => provider.GetRequiredService<PersonalTrainingService>());
        services.AddHostedService<PersonalTrainingWorker>();
        return services;
    }
}
