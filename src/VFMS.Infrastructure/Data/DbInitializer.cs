using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<VfmsDbContext>>();

        try
        {
            await context.Database.MigrateAsync();

            // 1. Seed default Gym
            Gym defaultGym;
            if (!await context.Gyms.AnyAsync())
            {
                defaultGym = new Gym
                {
                    GymName = "Victor Fitness & Gym",
                    Address = "123 Nguyen Trai, Hanoi",
                    PhoneNumber = "0243 1234 567",
                    Email = "contact@vfms.vn",
                    OpenTime = new TimeOnly(5, 0),
                    CloseTime = new TimeOnly(23, 0),
                    IsAcceptingMembers = true
                };
                context.Gyms.Add(defaultGym);
                await context.SaveChangesAsync();
                logger.LogInformation("Default Gym seeded.");
            }
            else
            {
                defaultGym = await context.Gyms.FirstAsync();
            }

            // 2. Seed Roles
            var roleNames = new[] { "Customer", "Member", "PT", "Staff", "Manager", "Admin" };
            foreach (var roleName in roleNames)
            {
                if (!await context.ApplicationRoles.AnyAsync(r => r.RoleName == roleName))
                {
                    context.ApplicationRoles.Add(new ApplicationRole { RoleName = roleName });
                }
            }
            await context.SaveChangesAsync();

            // 3. Seed Admin account
            const string adminEmail = "admin@vfms.vn";
            if (!await context.ApplicationUsers.AnyAsync(u => u.Email == adminEmail))
            {
                var adminRole = await context.ApplicationRoles.FirstAsync(r => r.RoleName == "Admin");
                var adminUser = new ApplicationUser
                {
                    UserId = Guid.NewGuid().ToString(),
                    GymId = defaultGym.Id,
                    FullName = "System Admin",
                    Email = adminEmail,
                    PhoneNumber = "0000000000",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    AccountStatus = AccountStatus.Active
                };
                context.ApplicationUsers.Add(adminUser);
                await context.SaveChangesAsync();

                context.ApplicationUserRoles.Add(new ApplicationUserRole
                {
                    UserId = adminUser.UserId,
                    RoleId = adminRole.Id
                });
                await context.SaveChangesAsync();
                logger.LogInformation("Default Admin account seeded.");
            }

            // 4. Exercise catalog and standard workout plans (GBR-95 fallback)
            await WorkoutSeed.SeedAsync(context, adminEmail, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
