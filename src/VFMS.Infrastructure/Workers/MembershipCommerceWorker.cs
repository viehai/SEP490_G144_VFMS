using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Workers;

public sealed class MembershipCommerceWorker(IServiceScopeFactory scopes, ILogger<MembershipCommerceWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunJobAsync("JOB-01", (service, ct) => service.ExpireOrdersAsync(ct), stoppingToken);
            await RunJobAsync("JOB-02", (service, ct) => service.AdvanceMembershipsAsync(ct), stoppingToken);
        }
    }

    private async Task RunJobAsync(
        string name, Func<IMembershipPurchaseService, CancellationToken, Task> run, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<IMembershipPurchaseService>(), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Membership commerce job {Job} failed and will be retried on the next tick.", name);
        }
    }
}
