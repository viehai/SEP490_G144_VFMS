using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Workers;

public sealed class LockerWorker(IServiceScopeFactory scopes, ILogger<LockerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ILockerService>();
                await service.Expire(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Locker background job will retry. Error type: {ErrorType}", ex.GetType().Name);
            }
        }
    }
}
