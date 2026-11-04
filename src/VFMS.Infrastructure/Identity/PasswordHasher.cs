using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.Identity;

/// <summary>
/// BCrypt-backed password hasher implementation (GBR-26).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
