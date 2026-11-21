using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class TrainerStore(VfmsDbContext db, IDataProtectionProvider protection, IConfiguration? configuration = null) : ITrainerStore
{
    private readonly List<string> pendingFiles = [];
    private string StorageRoot => Path.GetFullPath(configuration?["TrainerFiles:Root"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "trainer-files"));
    public IQueryable<T> Query<T>() where T : BaseEntity => db.Set<T>().Where(x => !x.IsDeleted);
    public Task<List<T>> List<T>(IQueryable<T> q, CancellationToken ct) => q.ToListAsync(ct);
    public Task<T?> First<T>(IQueryable<T> q, CancellationToken ct) => q.FirstOrDefaultAsync(ct);
    public Task<int> Count<T>(IQueryable<T> q, CancellationToken ct) => q.CountAsync(ct);
    public void Add<T>(T entity) where T : BaseEntity => db.Set<T>().Add(entity);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<ApplicationUser?> FindEligiblePasswordSetupAsync(string tokenHash, DateTime now, CancellationToken ct) =>
        EligiblePasswordSetupUsers(now).AsNoTracking().FirstOrDefaultAsync(
            user => user.PasswordSetupTokenHash == tokenHash, ct);

    public async Task<bool> TryCompletePasswordSetupAsync(string userId, string tokenHash, string passwordHash, DateTime now, CancellationToken ct) =>
        await EligiblePasswordSetupUsers(now)
            .Where(user => user.UserId == userId && user.PasswordSetupTokenHash == tokenHash)
            .ExecuteUpdateAsync(update => update
                .SetProperty(user => user.PasswordHash, passwordHash)
                .SetProperty(user => user.PasswordSetupTokenHash, (string?)null)
                .SetProperty(user => user.PasswordSetupExpiresAt, (DateTime?)null)
                .SetProperty(user => user.ResetTokenHash, (string?)null)
                .SetProperty(user => user.ResetTokenExpiresAt, (DateTime?)null)
                .SetProperty(user => user.AccountStatus, AccountStatus.Active)
                .SetProperty(user => user.UpdatedAt, now), ct) == 1;

    private IQueryable<ApplicationUser> EligiblePasswordSetupUsers(DateTime now) =>
        db.ApplicationUsers.Where(user => user.AccountStatus == AccountStatus.PendingVerification &&
            !user.IsBanned && user.PasswordSetupExpiresAt > now &&
            user.UserRoles.Any(userRole => userRole.Role.RoleName == nameof(UserRole.PT)) &&
            !user.UserRoles.Any(userRole => userRole.Role.RoleName != nameof(UserRole.PT)) &&
            db.TrainerApplications.Any(application => application.HiredUserId == user.UserId &&
                application.ApplicationType == "Recruitment" && application.ApplicationStatus == TrainerApplicationStatus.Hired));

    public TrainerQueuedEmail Email(string recipient, string subject, string body) => new()
    {
        Recipient = recipient,
        Subject = subject,
        ProtectedBody = protection.CreateProtector("VFMS.TrainerEmail.v1").Protect(body)
    };
    public async Task<TrainerStoredFile> SaveFile(TrainerFileInput file, CancellationToken ct)
    {
        var contentType = TrainerFileRules.ContentType(file) ?? throw new InvalidOperationException("Invalid trainer file.");
        Directory.CreateDirectory(StorageRoot);
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(StorageRoot, key);
        pendingFiles.Add(path);
        await System.IO.File.WriteAllBytesAsync(path, file.Content, ct);
        return new TrainerStoredFile { Id = key, StorageKey = key, FileName = file.FileName, ContentType = contentType };
    }
    public async Task<byte[]?> ReadFile(TrainerStoredFile file, CancellationToken ct)
    {
        if (file.LegacyContentBase64 != null) return Convert.FromBase64String(file.LegacyContentBase64);
        if (!Guid.TryParseExact(file.StorageKey, "N", out _)) return null;
        var path = Path.Combine(StorageRoot, file.StorageKey);
        return System.IO.File.Exists(path) ? await System.IO.File.ReadAllBytesAsync(path, ct) : null;
    }
    public async Task<Result<T>> Write<T>(Func<Task<Result<T>>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Recruitment volume is small; one transaction-scoped SQL lock covers cross-identity cooldown and onboarding races.
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Recruitment busy', 1;", ct);
            var result = await action();
            if (result.IsSuccess)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                pendingFiles.Clear();
            }
            else
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
            return result;
        }
        catch (Exception exception) when (AccountIdentityConflict.SqlErrorNumber(exception) is 51000 or 1205)
        {
            if (AccountIdentityConflict.SqlErrorNumber(exception) == 51000)
                await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result<T>.Failure("Recruitment is busy; retry shortly.", "TRAINER_CONFLICT", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result<T>.Failure("Record changed; refresh and retry.", "TRAINER_CONFLICT", 409);
        }
        catch (DbUpdateException exception) when (AccountIdentityConflict.GetField(exception) is { } field)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return AccountIdentityConflict.ResultFor<T>(field);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result<T>.Failure("Unable to save because of a data conflict.", "TRAINER_CONFLICT", 409);
        }
        finally
        {
            foreach (var path in pendingFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Orphaned files can be removed during storage maintenance.
                }
            }
            pendingFiles.Clear();
        }
    }
}
