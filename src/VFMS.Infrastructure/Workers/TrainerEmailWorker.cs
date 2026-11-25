using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Workers;

/// <summary>Delivers queued trainer emails and reports expired setup links (GBR-04).</summary>
public sealed class TrainerEmailWorker(IServiceScopeFactory scopes, ILogger<TrainerEmailWorker> log) : BackgroundService
{
    private DateTime _nextExpiryCheck = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool configured;
                using (var scope = scopes.CreateScope())
                    configured = await scope.ServiceProvider.GetRequiredService<ITrainerService>()
                        .DeliverQueuedEmail(scope.ServiceProvider.GetRequiredService<IEmailService>(), stoppingToken);
                // Setup links only reach applicants when mail is configured, so only then can one expire unused.
                if (configured && DateTime.UtcNow >= _nextExpiryCheck)
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ITrainerService>().NotifyExpiredSetupLinks(stoppingToken);
                    _nextExpiryCheck = DateTime.UtcNow.AddMinutes(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                log.LogWarning("Trainer mail delivery unavailable; messages remain queued.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
