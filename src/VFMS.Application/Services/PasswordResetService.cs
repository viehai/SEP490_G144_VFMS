using System.Security.Cryptography;
using System.Text;
using VFMS.Application.Interfaces;
using VFMS.Application.Settings;
using VFMS.Contracts.Auth;
using VFMS.Contracts.Auth.Validators;
using VFMS.Contracts.Common;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

public sealed class PasswordResetService(
    IPasswordResetRepository repository,
    IPasswordHasher passwordHasher,
    IEmailService emailService,
    IPasswordResetEmailQueue queue,
    PasswordResetSettings settings,
    TimeProvider clock) : IPasswordResetService
{
    public Task<Result<PasswordResetResponse>> RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!new ForgotPasswordRequestValidator().Validate(request).IsValid)
            return Task.FromResult(Result<PasswordResetResponse>.Failure("A valid email address is required.", "AUTH_INVALID_EMAIL"));

        // Do not query accounts or wait for SMTP on the anonymous HTTP request path.
        if (!settings.IsValid || !emailService.IsConfigured || !queue.TryEnqueue(request.Email.Trim()))
            return Task.FromResult(Result<PasswordResetResponse>.Failure(
                "Password recovery is temporarily unavailable. Please try again later.", "AUTH_RECOVERY_UNAVAILABLE", 503));

        return Task.FromResult(Result<PasswordResetResponse>.Success(new(
            "If an eligible account exists for this email, password reset instructions will be sent."), 202));
    }

    public async Task ProcessEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await repository.FindEligibleUserAsync(email.Trim(), cancellationToken);
        if (user is null) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Fingerprint(rawToken);
        var expiresAt = now.AddMinutes(settings.TokenLifetimeMinutes);
        if (!await repository.TryIssueAsync(user.UserId, user.PasswordHash, tokenHash, expiresAt,
            expiresAt.AddSeconds(-settings.RequestCooldownSeconds), now, cancellationToken)) return;
        try
        {
            // A configured URL avoids Host-header injection. A fragment avoids putting the token in server URL logs.
            var link = $"{settings.ResetUrl}#token={rawToken}";
            await emailService.SendAsync(user.Email, "VICTOR FITNESS - Đặt lại mật khẩu",
                $"Bạn đã yêu cầu đặt lại mật khẩu Victor Fitness.\n\nMở liên kết sau để đặt mật khẩu mới:\n{link}\n\nLiên kết chỉ dùng một lần và hết hạn sau {settings.TokenLifetimeMinutes} phút.\nNếu bạn không yêu cầu, hãy bỏ qua email này.", cancellationToken);
        }
        catch
        {
            // Do not leave a usable credential behind when delivery fails or is cancelled.
            await repository.InvalidateAsync(user.UserId, tokenHash, clock.GetUtcNow().UtcDateTime, CancellationToken.None);
            throw;
        }
    }

    public async Task<Result<PasswordResetResponse>> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (!new ResetPasswordRequestValidator().Validate(request).IsValid)
            return Result<PasswordResetResponse>.Failure("Check the reset token, password strength, and password confirmation.", "AUTH_INVALID_RESET_REQUEST");

        var tokenHash = Fingerprint(request.Token);
        var user = await repository.FindByTokenAsync(tokenHash, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (user is null || !user.ResetTokenExpiresAt.HasValue || user.ResetTokenExpiresAt <= now ||
            user.AccountStatus != AccountStatus.Active || user.IsBanned) return InvalidToken();

        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            return Result<PasswordResetResponse>.Failure("New password must differ from the current password.", "AUTH_PASSWORD_UNCHANGED");

        // The repository atomically consumes the credential, changes the password, and revokes refresh tokens.
        var newPasswordHash = passwordHasher.Hash(request.NewPassword);
        var changed = await repository.TryResetAsync(user.UserId, tokenHash, user.PasswordHash,
            newPasswordHash, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return changed
            ? Result<PasswordResetResponse>.Success(new("Password reset successfully. Please log in with your new password."))
            : InvalidToken();
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Result<PasswordResetResponse> InvalidToken() => Result<PasswordResetResponse>.Failure(
        "The password reset link is invalid, expired, or already used.", "AUTH_INVALID_RESET_TOKEN");
}
