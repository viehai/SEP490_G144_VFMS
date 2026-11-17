using VFMS.Contracts.Common;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Contracts.Trainers;

namespace VFMS.Application.Interfaces;

// Queries are composed here without an EF dependency; execution belongs to Infrastructure.
public interface ITrainerStore
{
    IQueryable<T> Query<T>() where T : BaseEntity;
    Task<List<T>> List<T>(IQueryable<T> query, CancellationToken ct);
    Task<T?> First<T>(IQueryable<T> query, CancellationToken ct);
    Task<int> Count<T>(IQueryable<T> query, CancellationToken ct);
    void Add<T>(T entity) where T : BaseEntity;
    Task Save(CancellationToken ct);
    Task<Result<T>> Write<T>(Func<Task<Result<T>>> action, CancellationToken ct);
    TrainerQueuedEmail Email(string recipient, string subject, string body);
    Task<TrainerStoredFile> SaveFile(TrainerFileInput file, CancellationToken ct);
    Task<byte[]?> ReadFile(TrainerStoredFile file, CancellationToken ct);
}
