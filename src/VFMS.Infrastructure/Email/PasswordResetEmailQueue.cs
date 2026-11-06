using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Email;

public sealed class PasswordResetEmailQueue(IServiceScopeFactory scopeFactory, ILogger<PasswordResetEmailQueue> logger)
    : BackgroundService, IPasswordResetEmailQueue
{
    private readonly Channel<string> _requests = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public bool TryEnqueue(string email) => _requests.Writer.TryWrite(email);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IPasswordResetService>().ProcessEmailAsync(email, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // SMTP exceptions may contain recipients/credentials; do not log their messages or token-bearing bodies.
                logger.LogError("Password recovery email processing failed ({FailureType}).", error.GetType().Name);
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _requests.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }
}
