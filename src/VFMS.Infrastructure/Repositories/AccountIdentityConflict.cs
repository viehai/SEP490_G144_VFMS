using Microsoft.Data.SqlClient;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;

namespace VFMS.Infrastructure.Repositories;

internal static class AccountIdentityConflict
{
    public static string? GetField(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                if (current.Message.Contains("'IX_ApplicationUsers_Email'", StringComparison.Ordinal))
                    return nameof(ApplicationUser.Email);
                if (current.Message.Contains("'IX_ApplicationUsers_PhoneNumber'", StringComparison.Ordinal))
                    return nameof(ApplicationUser.PhoneNumber);
            }

            // SQLite is used by the relational integration tests.
            if (current.Message.Contains("UNIQUE constraint failed: ApplicationUsers.Email'", StringComparison.Ordinal))
                return nameof(ApplicationUser.Email);
            if (current.Message.Contains("UNIQUE constraint failed: ApplicationUsers.PhoneNumber'", StringComparison.Ordinal))
                return nameof(ApplicationUser.PhoneNumber);
        }

        return null;
    }

    public static Result<T> ResultFor<T>(string field) => field == nameof(ApplicationUser.Email)
        ? Result<T>.Failure("Email is already in use.", "AUTH_EMAIL_EXISTS", 409)
        : Result<T>.Failure("A user with this phone number already exists.", "USER_PHONE_EXISTS", 409);

    public static int? SqlErrorNumber(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sqlException)
                return sqlException.Number;
        return null;
    }

    public static Result<T> WriteConflict<T>() =>
        Result<T>.Failure("Account details changed concurrently. Please retry.", "USER_PROVISIONING_CONFLICT", 409);
}
