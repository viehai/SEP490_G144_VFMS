using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Workers;

public sealed class TrainerEmailWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<TrainerEmailWorker> log) : BackgroundService
{
    private DateTime _nextExpiryCheck = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Setup links only reach applicants when mail is configured, so only then can one expire unused.
                if (await Deliver(stoppingToken) && DateTime.UtcNow >= _nextExpiryCheck)
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<TrainerService>().NotifyExpiredSetupLinks(stoppingToken);
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
    /// <returns>Whether mail delivery is configured.</returns>
    private async Task<bool> Deliver(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        if (!email.IsConfigured)
            return false;

        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("VFMS.TrainerEmail.v1");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same lock as service writes: prevents overwriting newly queued emails or token reissues.
        // Deliver one message per tick so the lock has a bounded SMTP workload.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51001, 'Mail worker busy', 1;", ct);
        var now = DateTime.UtcNow;
        const string setupSubject = "Set up your trainer account";
        var application = await db.TrainerApplications.FromSqlInterpolated($"""
            SELECT TOP (1) a.*
            FROM TrainerApplications a
            CROSS APPLY (
                SELECT MIN(CASE WHEN e.Subject = {setupSubject} THEN 0 ELSE 1 END) AS Priority
                FROM OPENJSON(a.EmailQueueJson) WITH (
                    Subject nvarchar(256) '$.Subject', SentAt datetime2 '$.SentAt',
                    NextAttemptAt datetime2 '$.NextAttemptAt', IsDeleted bit '$.IsDeleted'
                ) e
                WHERE ISNULL(e.IsDeleted,0)=0 AND e.SentAt IS NULL AND e.NextAttemptAt <= {now}
            ) ready
            WHERE a.IsDeleted=0 AND ready.Priority IS NOT NULL
            ORDER BY ready.Priority, a.Id
            """).FirstOrDefaultAsync(ct);
        if (application == null)
        {
            await tx.CommitAsync(ct);
            return true;
        }
        var emails = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(application.EmailQueueJson) ?? [];
        var batch = emails.Where(x => !x.IsDeleted && x.SentAt == null && x.NextAttemptAt <= now)
            .OrderByDescending(x => x.Subject == setupSubject).Take(1);
        foreach (var item in batch)
        {
            try
            {
                var body = protector.Unprotect(item.ProtectedBody);
                if (body.StartsWith("SETUP:", StringComparison.Ordinal))
                    body = "Set up your trainer password within 48 hours: " + SetupLink(body[6..]);

                await email.SendAsync(item.Recipient, item.Subject, body, ct);
                item.SentAt = DateTime.UtcNow;
                item.ProtectedBody = string.Empty;
                // GBR-04: a setup link expires 48 hours after it is sent.
                if (item.Subject == setupSubject && await db.ApplicationUsers
                        .FirstOrDefaultAsync(u => u.UserId == application.HiredUserId, ct) is { } hired)
                    hired.PasswordSetupExpiresAt = item.SentAt.Value.AddHours(48);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                item.Attempts++;
                item.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(1440, Math.Pow(2, Math.Min(item.Attempts, 10))));
                log.LogWarning("Trainer email {EmailId} failed; queued for retry.", item.Id);
            }
        }
        application.EmailQueueJson = JsonSerializer.Serialize(emails);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private string SetupLink(string token)
    {
        var setupPage = config["TrainerMail:SetupPageUrl"];
        var useResetUrl = string.IsNullOrWhiteSpace(setupPage);
        var url = useResetUrl ? config["PasswordReset:ResetUrl"] : setupPage;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Configure the trainer password setup page URL.");

        var link = new UriBuilder(uri) { Fragment = string.Empty };
        if (useResetUrl)
        {
            link.Path = "/auth/trainer-setup";
            link.Query = string.Empty;
        }
        var existingQuery = link.Query.TrimStart('?');
        var tokenQuery = "token=" + Uri.EscapeDataString(token);
        link.Query = existingQuery.Length == 0 ? tokenQuery : existingQuery + "&" + tokenQuery;
        return link.Uri.AbsoluteUri;
    }
}
