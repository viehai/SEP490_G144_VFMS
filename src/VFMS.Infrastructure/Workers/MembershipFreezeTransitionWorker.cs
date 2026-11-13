using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Workers;

public sealed class MembershipFreezeTransitionWorker(
    IServiceScopeFactory scopes, ILogger<MembershipFreezeTransitionWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
            var afterId = 0;
            const int pageSize = 100;
            while (!ct.IsCancellationRequested)
            {
                IReadOnlyList<int> ids;
                await using (var queryScope = scopes.CreateAsyncScope())
                    ids = await queryScope.ServiceProvider.GetRequiredService<IMembershipFreezeLifecycleRepository>()
                        .ListDueAsync(today, afterId, pageSize, ct);
                foreach (var id in ids)
                {
                    try
                    {
                        // A fresh context prevents a failed or successful prior record from leaving stale tracked data.
                        await using var scope = scopes.CreateAsyncScope();
                        var result = await scope.ServiceProvider.GetRequiredService<IMembershipFreezeLifecycleService>().ProcessAsync(id, ct);
                        if (!result.IsSuccess)
                            logger.LogWarning("JOB-03 record {RequestId} requires reconciliation: {Message}", id, result.ErrorMessage);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger.LogError(ex, "JOB-03 record {RequestId} failed and will be retried on the next run.", id);
                    }
                }
                if (ids.Count < pageSize)
                    break;
                afterId = ids[^1];
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "JOB-03 failed to load due records and will retry on the next run.");
        }
        ct.ThrowIfCancellationRequested();
    }
}
