using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.PersonalTraining;

public sealed class PersonalTrainingWorker(IServiceScopeFactory scopes, ILogger<PersonalTrainingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PersonalTrainingService>();
                await service.ExpirePending(ct);
                await service.SendPendingEmail(scope.ServiceProvider.GetRequiredService<IEmailService>(), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("PT background task will retry. Error type: {ErrorType}", ex.GetType().Name);
            }
        }
    }
}
