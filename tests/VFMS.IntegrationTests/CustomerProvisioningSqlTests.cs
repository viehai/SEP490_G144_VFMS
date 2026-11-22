using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class CustomerProvisioningSqlTests
{
    [SqlTrainerFact]
    public async Task StartupDoesNotRecreateArchivedBootstrapAdminOrAssignTemplatesToReusedEmail()
    {
        await using var database = new TrainerTestDatabase();
        await database.Initialize();
        var gymId = await database.Db.Gyms.Select(gym => gym.Id).FirstAsync();
        const string bootstrapEmail = "admin@vfms.vn";
        database.Db.ApplicationUsers.Add(new ApplicationUser
        {
            UserId = "archived-bootstrap",
            GymId = gymId,
            Email = bootstrapEmail,
            PhoneNumber = "0900000101",
            FullName = "Archived bootstrap fixture",
            PasswordHash = "fixture-only",
            AccountStatus = AccountStatus.Active,
            IsDeleted = true
        });
        await database.Db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<VfmsDbContext>(options =>
            options.UseSqlServer(database.Db.Database.GetConnectionString()));
        await using var provider = services.BuildServiceProvider();

        await DbInitializer.SeedAsync(provider);
        Assert.False(await database.Db.ApplicationUsers.AnyAsync(user => user.Email == bootstrapEmail));
        Assert.Equal(1, await database.Db.ApplicationUsers.IgnoreQueryFilters().CountAsync(user => user.Email == bootstrapEmail));

        var customerRoleId = await database.Db.ApplicationRoles.Where(role => role.RoleName == "Customer")
            .Select(role => role.Id).SingleAsync();
        database.Db.ApplicationUsers.Add(new ApplicationUser
        {
            UserId = "customer-reused-email",
            GymId = gymId,
            Email = bootstrapEmail,
            PhoneNumber = "0900000102",
            FullName = "Customer fixture",
            PasswordHash = "fixture-only",
            AccountStatus = AccountStatus.Active,
            UserRoles = [new ApplicationUserRole { RoleId = customerRoleId }]
        });
        await database.Db.SaveChangesAsync();

        await DbInitializer.SeedAsync(provider);
        Assert.False(await database.Db.WorkoutPlans.AnyAsync(plan => plan.MemberUserId == "customer-reused-email"));
    }

    [SqlTrainerFact]
    public async Task ProvisioningMapsSqlServerDeadlockAfterServerRollsBackVictim()
    {
        await using var database = new TrainerTestDatabase();
        await database.Initialize();
        var first = new Gym { GymName = "First disposable lock row" };
        var second = new Gym { GymName = "Second disposable lock row" };
        database.Db.AddRange(first, second);
        await database.Db.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer(database.Db.Database.GetConnectionString()).Options;
        await using var victim = new VfmsDbContext(options);
        await using var survivor = new VfmsDbContext(options);
        // Track this row before either contender holds a lock, so the deadlock occurs inside SaveChanges.
        var victimTarget = await victim.Gyms.SingleAsync(gym => gym.Id == second.Id);
        var victimLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var survivorLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var victimTask = new UserAdministrationRepository(victim).ExecuteProvisioningAsync(async () =>
        {
            await victim.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY LOW;", timeout.Token);
            await RenameAsync(victim, first.Id, "Victim transaction", timeout.Token);
            victimLocked.SetResult();
            await survivorLocked.Task.WaitAsync(timeout.Token);
            victimTarget.GymName = "Should roll back";
            await victim.SaveChangesAsync(timeout.Token);
            return Result<bool>.Success(true);
        }, timeout.Token);

        var survivorTask = CommitSurvivorAsync();
        await Task.WhenAll(victimTask, survivorTask);
        var mapped = await victimTask;
        Assert.Equal(409, mapped.StatusCode);
        Assert.Equal("USER_PROVISIONING_CONFLICT", mapped.ErrorCode);

        await using var check = new VfmsDbContext(options);
        Assert.Equal("Survivor transaction", (await check.Gyms.SingleAsync(gym => gym.Id == first.Id)).GymName);
        Assert.Equal("Survivor held this row", (await check.Gyms.SingleAsync(gym => gym.Id == second.Id)).GymName);

        async Task CommitSurvivorAsync()
        {
            await using var transaction = await survivor.Database.BeginTransactionAsync(timeout.Token);
            await RenameAsync(survivor, second.Id, "Survivor held this row", timeout.Token);
            survivorLocked.SetResult();
            await victimLocked.Task.WaitAsync(timeout.Token);
            await RenameAsync(survivor, first.Id, "Survivor transaction", timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
    }

    private static Task RenameAsync(VfmsDbContext db, int gymId, string name, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Gyms WITH (ROWLOCK) SET GymName = {name} WHERE Id = {gymId};", cancellationToken);
}
