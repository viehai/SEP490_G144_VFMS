using VFMS.Application.Interfaces;
using VFMS.Infrastructure.PersonalTraining;

namespace VFMS.API.Extensions;

public static class PersonalTrainingModule
{
    public static IServiceCollection AddPersonalTrainingModule(this IServiceCollection services)
    {
        services.AddScoped<PersonalTrainingService>();
        services.AddScoped<IPTPaymentFulfillment>(provider => provider.GetRequiredService<PersonalTrainingService>());
        services.AddHostedService<PersonalTrainingWorker>();
        return services;
    }
}
