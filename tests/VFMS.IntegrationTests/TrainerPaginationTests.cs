using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class TrainerPaginationTests
{
    [Fact]
    public async Task ApplicationsHaveTenItemsPerPageAndReturnToLastPageAfterReview()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteTestContext(new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var gym = new Gym { GymName = "Pagination test gym" };
        var role = await db.ApplicationRoles.FirstOrDefaultAsync(r => r.RoleName == "Manager")
            ?? new ApplicationRole { RoleName = "Manager" };
        var manager = new ApplicationUser
        {
            UserId = "paging-manager", Gym = gym, Email = "paging@example.test", PhoneNumber = "0900000099",
            FullName = "Test manager", AccountStatus = AccountStatus.Active
        };
        db.Add(new ApplicationUserRole { User = manager, Role = role });
        db.TrainerApplications.AddRange(Enumerable.Range(1, 21).Select(i => new TrainerApplication
        {
            TrackingCode = $"PAGING-{i:00}", FullName = $"Applicant {i:00}", SubmittedAt = DateTime.UtcNow.AddMinutes(-i)
        }));
        await db.SaveChangesAsync();
        var mapping = new TypeAdapterConfig();
        mapping.Scan(typeof(TrainerMapping).Assembly);
        var service = new TrainerService(new TrainerRepository(db, new EphemeralDataProtectionProvider()),
            new PasswordHasher(), new Mapper(mapping));
        var filter = new TrainerApplicationFilter(Status: "Pending", PageSize: 100);

        var first = (await service.Applications(filter, manager.UserId, default)).Data!;
        var second = (await service.Applications(filter with { Page = 2 }, manager.UserId, default)).Data!;
        var third = (await service.Applications(filter with { Page = 3 }, manager.UserId, default)).Data!;
        Assert.Equal(10, first.PageSize);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(10, second.Items.Count);
        var last = Assert.Single(third.Items);
        Assert.Equal(21, first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).Distinct().Count());

        (await db.TrainerApplications.SingleAsync(a => a.Id == last.Id)).ApplicationStatus = TrainerApplicationStatus.Rejected;
        await db.SaveChangesAsync();
        var refreshed = (await service.Applications(filter with { Page = 3 }, manager.UserId, default)).Data!;
        Assert.Equal(2, refreshed.Page);
        Assert.Equal(20, refreshed.TotalCount);
        Assert.Equal(10, refreshed.Items.Count);
    }
}
