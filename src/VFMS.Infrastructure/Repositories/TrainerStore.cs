using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class TrainerStore(VfmsDbContext db, IDataProtectionProvider protection) : ITrainerStore
{
    public IQueryable<T> Query<T>() where T : BaseEntity => db.Set<T>().Where(x => !x.IsDeleted);
    public Task<List<T>> List<T>(IQueryable<T> q, CancellationToken ct) => q.ToListAsync(ct);
    public Task<T?> First<T>(IQueryable<T> q, CancellationToken ct) => q.FirstOrDefaultAsync(ct);
    public Task<int> Count<T>(IQueryable<T> q, CancellationToken ct) => q.CountAsync(ct);
    public void Add<T>(T entity) where T : BaseEntity => db.Set<T>().Add(entity);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public TrainerEmail Email(string recipient, string subject, string body) => new()
    {
        Recipient = recipient,
        Subject = subject,
        ProtectedBody = protection.CreateProtector("VFMS.TrainerEmail.v1").Protect(body)
    };
    public async Task<Result<T>> Write<T>(Func<Task<Result<T>>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Recruitment volume is small; one transaction-scoped SQL lock covers cross-identity cooldown and onboarding races.
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'VFMS.TrainerRecruitment', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Recruitment busy', 1;", ct);
            var result = await action();
            if (result.IsSuccess) { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            else { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); }
            return result;
        }
        catch (SqlException ex) when (ex.Number is 51000 or 1205) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); return Result<T>.Failure("Recruitment is busy; retry shortly.", "TRAINER_CONFLICT", 409); }
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); return Result<T>.Failure("Record changed; refresh and retry.", "TRAINER_CONFLICT", 409); }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); return Result<T>.Failure("Unable to save because of a data conflict.", "TRAINER_CONFLICT", 409); }
    }
}
