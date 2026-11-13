using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.External;
using VFMS.Infrastructure.Repositories;

namespace VFMS.API.Extensions;

/// <summary>DI registrations for feature group 9 (Personalized Workout Plan, API-02 Gemini).</summary>
public static class WorkoutModule
{
    public static IServiceCollection AddWorkoutModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(configuration.GetSection("Gemini").Get<GeminiSettings>() ?? new GeminiSettings());
        services.AddHttpClient<IWorkoutPlanGenerator, GeminiWorkoutPlanGenerator>();
        services.AddScoped<IWorkoutRepository, WorkoutRepository>();
        services.AddScoped<IWorkoutPlanService, WorkoutPlanService>();
        return services;
    }
}
