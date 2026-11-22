using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VFMS.Domain.Entities;

namespace VFMS.IntegrationTests;

/// <summary>
/// ApplicationUsers.PhoneNumber is unique (IX_ApplicationUsers_PhoneNumber). Older test seeds leave it empty, so every
/// seeded user would share "". This gives each such user its own test number when it is saved; explicit numbers stay.
/// </summary>
internal sealed class UniqueTestPhoneInterceptor : SaveChangesInterceptor
{
    public static readonly UniqueTestPhoneInterceptor Instance = new();
    private static int next;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Fill(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Fill(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Fill(DbContext? db)
    {
        if (db is null) return;
        foreach (var user in db.ChangeTracker.Entries<ApplicationUser>()
                     .Where(e => e.State == EntityState.Added && string.IsNullOrWhiteSpace(e.Entity.PhoneNumber)))
            user.Entity.PhoneNumber = $"08{Interlocked.Increment(ref next):D8}";   // no explicit test number starts with 08
    }
}
