namespace VFMS.Contracts.Auth;

public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);
public sealed record PasswordResetResponse(string Message);
