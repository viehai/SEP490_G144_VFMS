using VFMS.Contracts.Auth;
using VFMS.Contracts.Common;

namespace VFMS.Application.Interfaces;

public interface IPasswordResetService
{
    Task<Result<PasswordResetResponse>> RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<Result<PasswordResetResponse>> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task ProcessEmailAsync(string email, CancellationToken cancellationToken = default);
}

public interface IPasswordResetEmailQueue
{
    bool TryEnqueue(string email);
}

public interface IEmailService
{
    bool IsConfigured { get; }
    Task SendAsync(string recipient, string subject, string textBody, CancellationToken cancellationToken = default);
}
