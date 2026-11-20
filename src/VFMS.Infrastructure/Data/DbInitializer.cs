using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, bool isDevelopment = false)
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
            var roleNames = new[] { "Customer", "Member", "PT", "YogaInstructor", "Staff", "Manager", "Admin" };
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

            if (isDevelopment)
            {
                var testAccounts = new[]
                {
                    ("Manager", "manager@test.vn", "Manager Test", "0900000001"),
                    ("Staff", "staff@test.vn", "Staff Test", "0900000002"),
                    ("YogaInstructor", "yoga@test.vn", "Yoga Instructor Test", "0900000003"),
                    ("Member", "member@test.vn", "Member Test", "0900000004")
                };

                foreach (var (roleName, email, fullName, phoneNumber) in testAccounts)
                {
                    var role = await context.ApplicationRoles.FirstAsync(r => r.RoleName == roleName);
                    var user = await context.ApplicationUsers
                        .Include(u => u.UserRoles)
                        .FirstOrDefaultAsync(u => u.Email == email);

                    if (user is null)
                    {
                        user = new ApplicationUser
                        {
                            UserId = email == "member@test.vn"
                                ? "b7a2e8b5-23e2-4415-8679-357bb5c33ad3"
                                : Guid.NewGuid().ToString(),
                            GymId = defaultGym.Id,
                            FullName = fullName,
                            Email = email,
                            PhoneNumber = phoneNumber,
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
                            AccountStatus = AccountStatus.Active
                        };
                        context.ApplicationUsers.Add(user);
                    }
                    else
                    {
                        user.AccountStatus = AccountStatus.Active;
                        user.IsBanned = false;
                        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234");
                        user.PasswordSetupTokenHash = null;
                        user.PasswordSetupExpiresAt = null;
                    }

                    await context.SaveChangesAsync();

                    if (!user.UserRoles.Any(ur => ur.RoleId == role.Id))
                    {
                        context.ApplicationUserRoles.Add(new ApplicationUserRole
                        {
                            UserId = user.UserId,
                            RoleId = role.Id
                        });
                        await context.SaveChangesAsync();
                    }
                }

                logger.LogInformation("Development test accounts seeded.");
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
