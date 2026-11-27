using Microsoft.EntityFrameworkCore;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.IntegrationTests;

public class TrainerSharedIdentityTests
{
    [SqlTrainerFact]
    public async Task Hiring_guest_linked_to_existing_account_preserves_physical_survey()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var db = fixture.Db;
        var user = new ApplicationUser
        {
            UserId = "existing-applicant", GymId = await db.Gyms.Select(g => g.Id).SingleAsync(),
            FullName = "Existing customer", Email = TrainerTestDatabase.Request.Email,
            PhoneNumber = TrainerTestDatabase.Request.PhoneNumber,
            AccountStatus = AccountStatus.Active,
            PhysicalProfile = new PhysicalProfile
            {
                MemberUserId = "existing-applicant", Gender = "Other", WeightKg = 65,
                HealthConditions = "Existing survey", AiConsentGrantedAt = new DateTime(2026, 1, 1)
            }
        };
        db.Add(user);
        db.Add(new ApplicationUserRole
        {
            UserId = user.UserId, Role = new ApplicationRole { RoleName = "Customer" }
        });
        await db.SaveChangesAsync();
        TrainerTestDatabase.Success(await fixture.Service.Submit(TrainerTestDatabase.Request, null, default));
        var id = await db.TrainerApplications.Select(a => a.Id).SingleAsync();
        TrainerTestDatabase.Success(await fixture.Service.Review(id, new("Interview", "Interview complete"), "manager", default));
        db.ChangeTracker.Clear();
        TrainerTestDatabase.Success(await fixture.Service.Review(id, new("Hire", "Verified", "Junior", user.UserId), "manager", default));
        db.ChangeTracker.Clear();
        var physical = await db.PhysicalProfiles.SingleAsync(p => p.MemberUserId == user.UserId);
        Assert.Equal(65, physical.WeightKg);
        Assert.Equal("Existing survey", physical.HealthConditions);
        Assert.Equal(new DateTime(2026, 1, 1), physical.AiConsentGrantedAt);
        Assert.Equal(user.UserId, await db.TrainerProfiles.Select(p => p.PTUserId).SingleAsync());
    }

    [SqlTrainerFact]
    public async Task Public_trainer_reads_account_avatar_and_shared_gender_and_upload_updates_account()
    {
        await using var fixture = new TrainerTestDatabase();
        await fixture.Initialize();
        var hired = await fixture.Hire();
        TrainerTestDatabase.Success(await fixture.Service.Setup(new(await fixture.SetupToken(hired.Id), "TestPassword1!"), default));
        var db = fixture.Db;
        var profile = await db.TrainerProfiles.SingleAsync();
        var user = await db.ApplicationUsers.Include(u => u.PhysicalProfile).SingleAsync(u => u.UserId == profile.PTUserId);
        user.AvatarUrl = "https://example.test/account-photo.png";
        user.PhysicalProfile!.Gender = "Female";
        await db.SaveChangesAsync();
        var detail = TrainerTestDatabase.Success(await fixture.Service.Public(profile.Id, default));
        Assert.Equal("https://example.test/account-photo.png", detail.Profile.AvatarUrl);
        Assert.Equal("Female", detail.Profile.Gender);

        TrainerTestDatabase.Success(await fixture.Service.Update(new UpdateTrainerProfileRequest(null,
            new TrainerFileInput("portrait.png", [137, 80, 78, 71, 13, 10, 26, 10])), user.UserId, default));
        db.ChangeTracker.Clear();
        var stored = await db.ApplicationUsers.SingleAsync(u => u.UserId == user.UserId);
        Assert.Equal($"/api/trainers/{profile.Id}/avatar", stored.AvatarUrl);
        Assert.Equal(stored.AvatarUrl, TrainerTestDatabase.Success(await fixture.Service.Public(profile.Id, default)).Profile.AvatarUrl);
        Assert.True((await fixture.Service.Avatar(profile.Id, null, default)).IsSuccess);
    }
}
