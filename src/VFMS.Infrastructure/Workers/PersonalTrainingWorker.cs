using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Workers;

public sealed class PersonalTrainingWorker(IServiceScopeFactory scopes, ILogger<PersonalTrainingWorker> logger)
    : BackgroundService
{
    private DateOnly? lastTrainerSweep;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPersonalTrainingService>();
                await service.ExpirePending(ct);
                await service.ProcessAttendance(ct);
                await service.RemindPendingFreezes(ct);
                var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
                if (lastTrainerSweep != today)
                {
                    await service.ExpireInactiveTrainers(today, ct);
                    lastTrainerSweep = today;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("PT background task will retry. Error type: {ErrorType}", ex.GetType().Name);
            }
        }
    }
}
