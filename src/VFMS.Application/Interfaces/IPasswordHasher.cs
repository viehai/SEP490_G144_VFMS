namespace VFMS.Application.Interfaces;

/// <summary>
/// Abstracts BCrypt password hashing so the Application layer has no direct dependency on BCrypt.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plain-text password.</summary>
    string Hash(string password);

    /// <summary>Verifies a plain-text password against a stored hash.</summary>
    bool Verify(string password, string hash);
}
