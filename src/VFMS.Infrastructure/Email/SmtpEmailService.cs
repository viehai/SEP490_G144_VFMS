using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Email;

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Victor Fitness";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535 &&
        Security is "StartTls" or "SslOnConnect" && MailboxAddress.TryParse(FromEmail, out _) &&
        (string.IsNullOrEmpty(Username) == string.IsNullOrEmpty(Password));
}

public sealed class SmtpEmailService(IOptions<SmtpSettings> options) : IEmailService
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) throw new InvalidOperationException("SMTP settings are incomplete.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(settings.Host, settings.Port,
            settings.Security == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception error) when (error is IOException or SmtpCommandException or SmtpProtocolException or OperationCanceledException)
        {
            // SMTP already accepted the message; a failed QUIT must not invalidate its reset link.
        }
    }
}
