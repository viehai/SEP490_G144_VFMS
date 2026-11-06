using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;
using System.Text.Json;

namespace VFMS.Infrastructure.Identity;

public sealed class TrainerEmailWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<TrainerEmailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (config.GetValue<bool>("TrainerMail:Enabled")) await Deliver(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { log.LogWarning("Trainer mail delivery unavailable; messages remain queued."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task Deliver(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("VFMS.TrainerEmail.v1");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same lock as service writes: prevents overwriting newly queued emails or token reissues.
        // Deliver one message per tick so the lock has a bounded SMTP workload.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51001, 'Mail worker busy', 1;", ct);
        var now = DateTime.UtcNow;
        var application = await db.TrainerApplications.FromSqlRaw("SELECT TOP (1) a.* FROM TrainerApplications a WHERE a.IsDeleted=0 AND EXISTS (SELECT 1 FROM OPENJSON(a.EmailQueueJson) WITH (SentAt datetime2 '$.SentAt', NextAttemptAt datetime2 '$.NextAttemptAt', IsDeleted bit '$.IsDeleted') e WHERE ISNULL(e.IsDeleted,0)=0 AND e.SentAt IS NULL AND e.NextAttemptAt <= SYSUTCDATETIME()) ORDER BY a.Id").FirstOrDefaultAsync(ct);
        if (application == null) { await tx.CommitAsync(ct); return; }
        var emails = JsonSerializer.Deserialize<List<TrainerQueuedEmail>>(application.EmailQueueJson) ?? [];
        var batch = emails.Where(x => !x.IsDeleted && x.SentAt == null && x.NextAttemptAt <= now).Take(1);
        foreach (var item in batch)
        {
            try
            {
                var body = protector.Unprotect(item.ProtectedBody);
                if (body.StartsWith("SETUP:", StringComparison.Ordinal))
                {
                    var url = config["TrainerMail:SetupPageUrl"];
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("Configure HTTPS setup page.");
                    body = "Set up your trainer password within 24 hours: " + url + (url.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(body[6..]);
                }
                var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse(config["TrainerMail:From"]!)); message.To.Add(MailboxAddress.Parse(item.Recipient));
                message.Subject = item.Subject; message.MessageId = $"trainer-{item.Id}@vfms.local"; message.Body = new TextPart("plain") { Text = body };
                using var smtp = new SmtpClient { Timeout = 15000 };
                await smtp.ConnectAsync(config["TrainerMail:Host"] ?? throw new InvalidOperationException("SMTP host missing."), config.GetValue("TrainerMail:Port", 587), SecureSocketOptions.StartTls, ct);
                var username = config["TrainerMail:Username"];
                if (!string.IsNullOrEmpty(username)) await smtp.AuthenticateAsync(username, config["TrainerMail:Password"] ?? throw new InvalidOperationException("SMTP password missing."), ct);
                await smtp.SendAsync(message, ct); await smtp.DisconnectAsync(true, ct); item.SentAt = DateTime.UtcNow; item.ProtectedBody = string.Empty;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { item.Attempts++; item.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(1440, Math.Pow(2, Math.Min(item.Attempts, 10)))); log.LogWarning("Trainer email {EmailId} failed; queued for retry.", item.Id); }
        }
        application.EmailQueueJson = JsonSerializer.Serialize(emails);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
