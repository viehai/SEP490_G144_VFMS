using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Infrastructure.Identity;
using VFMS.Infrastructure.Repositories;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

// Fixes from the group 3-4 review (2026-11-20).
public sealed partial class MembershipPurchaseApiTests
{
    private async Task<PTSessionResponse> AgreeToCancel(HttpClient member, HttpClient trainer, PTSessionResponse session)
    {
        _clock.Now = LocalTime(session.Date, session.StartTime).AddHours(-48);
        using var requested = await member.PostAsync($"/api/pt/sessions/{session.Id}/cancellation-request", null);
        Assert.True(requested.StatusCode == HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        var pending = (await requested.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var accepted = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/reschedule-request/decision",
            new PTRescheduleDecision(true, session.Date, session.StartTime, pending.Proposal!.ExpiresAt));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        return (await accepted.Content.ReadFromJsonAsync<PTSessionResponse>())!;
    }

    [Fact]
    public async Task MakeUpCanBeBookedAfterTheGymMembershipEnds()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (_, contract) = await ActivatedPTContract(member);
        var first = contract.Sessions[0];
        await AgreeToCancel(member, trainer, first);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.MemberMemberships.SingleAsync()).EndDate = first.Date;
            await db.SaveChangesAsync();
        }

        // GBR-10: Gym access is checked at attendance, so a make-up after the membership ends is allowed.
        using var proposed = await member.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/make-up",
            new PTMakeUpRequest(contract.EndDate.AddDays(1), new TimeOnly(20, 0)));
        Assert.True(proposed.StatusCode == HttpStatusCode.Created, await proposed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PTPackagesAllowAtMostFiftySessions()
    {
        using var manager = Client("manager", "Manager");
        using var tooMany = await manager.PostAsJsonAsync("/api/pt/manager/packages", PackageInput(51, 7, 15));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        using var fifty = await manager.PostAsJsonAsync("/api/pt/manager/packages", PackageInput(50, 7, 15));
        Assert.True(fifty.StatusCode == HttpStatusCode.Created, await fifty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithdrawingAnApprovedFreezeRestoresOnlyItsOwnSessions()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        var first = contract.Sessions[0];
        await AgreeToCancel(member, trainer, first);

        // The agreed cancellation inside the future freeze window already has a booked make-up.
        using var proposed = await member.PostAsJsonAsync($"/api/pt/sessions/{first.Id}/make-up",
            new PTMakeUpRequest(contract.EndDate.AddDays(2), new TimeOnly(20, 0)));
        Assert.True(proposed.StatusCode == HttpStatusCode.Created, await proposed.Content.ReadAsStringAsync());
        var makeUp = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        using var acceptedMakeUp = await trainer.PostAsJsonAsync($"/api/pt/sessions/{makeUp.Id}/make-up/decision",
            new PTProposalDecision(true, makeUp.Proposal!.ExpiresAt));
        Assert.True(acceptedMakeUp.StatusCode == HttpStatusCode.OK, await acceptedMakeUp.Content.ReadAsStringAsync());

        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(first.Date, first.Date.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        using var approved = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision",
            new PTFreezeDecision(true, null));
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        using var withdrawn = await member.PostAsync($"/api/pt/freezes/{freeze.Id}/withdraw", null);
        Assert.True(withdrawn.StatusCode == HttpStatusCode.OK, await withdrawn.Content.ReadAsStringAsync());
        var after = await ContractOf(member, id);
        Assert.Equal("Scheduled", after.Sessions.Single(s => s.Id == makeUp.Id).Status);
        Assert.Equal("Cancelled", after.Sessions.Single(s => s.Id == first.Id).Status);
        Assert.All(contract.Sessions.Skip(1).Where(s => s.Date < first.Date.AddDays(7)),
            original => Assert.Equal("Scheduled", after.Sessions.Single(s => s.Id == original.Id).Status));
    }

    [Fact]
    public async Task ApprovalIsBlockedWhenAHeldPostFreezeSlotIsNoLongerUsable()
    {
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        using var requested = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        PTSession firstHold;
        await using (var scope = _app.Services.CreateAsyncScope())
            firstHold = await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().PTSessions.AsNoTracking()
                .Where(s => s.PTFreezeId == freeze.Id).OrderBy(s => s.SessionDate).FirstAsync();

        // The Manager decides only after the first held slot has started.
        _clock.Now = LocalTime(firstHold.SessionDate, firstHold.StartTime).AddMinutes(1);
        using var approve = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision",
            new PTFreezeDecision(true, null));
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("PT_FREEZE_SLOT_UNAVAILABLE", await approve.Content.ReadAsStringAsync());
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Equal(PTFreezeRequestStatus.Pending, (await db.PTFreezes.SingleAsync(f => f.Id == freeze.Id)).RequestStatus);
            Assert.Contains(await db.Notifications.ToListAsync(), n => n.RecipientUserId == "member" &&
                n.EntityType == nameof(PTFreeze) && n.EntityId == freeze.Id && n.Title == "Lịch sau bảo lưu không còn khả dụng");
        }
    }

    private static TrainerService Trainers(AsyncServiceScope scope)
    {
        var mapping = new TypeAdapterConfig();
        mapping.Scan(typeof(TrainerMapping).Assembly);
        return new(new TrainerRepository(scope.ServiceProvider.GetRequiredService<VfmsDbContext>(),
            new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build()), new PasswordHasher(), new Mapper(mapping));
    }

    // Feature 4 serves Trainer Role PT only: a Yoga trainer neither sells packages nor takes over a contract,
    // even if the account also holds the PT login role.
    [Fact]
    public async Task OnlyTrainerRolePTSellsPackagesAndTakesOverContracts()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            var ptRole = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "PT");
            db.ApplicationUsers.Add(User("yoga-pt", gym, ptRole));
            db.TrainerProfiles.Add(new TrainerProfile
            {
                PTUserId = "yoga-pt",
                Level = TrainerLevel.Senior,
                AcceptingNewMembers = true,
                TrainerRoleId = _yogaTrainerRole
            });
            (await db.PTPurchases.SingleAsync(p => p.Id == id)).TrainerChangeDeadline = _clock.Now.UtcDateTime.AddDays(7);
            await db.SaveChangesAsync();
        }
        Assert.Empty((await member.GetFromJsonAsync<PTReplacementTrainer[]>($"/api/pt/contracts/{id}/replacement-trainers"))!);

        var packageId = (await member.GetFromJsonAsync<PTPackageResponse[]>("/api/pt/packages"))!.Single().Id;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.TrainerProfiles.SingleAsync(p => p.PTUserId == "buyer")).TrainerRoleId = _yogaTrainerRole;
            await db.SaveChangesAsync();
        }
        using var slots = await member.PostAsJsonAsync("/api/pt/schedule/booking-slots",
            new PTBookingSlotsRequest("buyer", packageId, contract.StartDate.AddDays(28), []));
        Assert.Equal(HttpStatusCode.BadRequest, slots.StatusCode);
    }

    // Hiring itself runs on SQL Server only (TrainerRepository.RunExclusiveAsync); the hire and role removal are in TrainerWorkflowTests.
    [Fact]
    public async Task ApplicationDetailFlagsAMemberWithAnUnfinishedPTContractAndActiveMemberServices()
    {
        using var member = Client("member", "Member");
        var (id, _) = await ActivatedPTContract(member);
        int applicationId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(u => u.UserId == "member");
            user.PhoneNumber = "0911222333"; // seeded users share a phone number; keep the match unambiguous
            var application = new TrainerApplication
            {
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Gender = "Other",
                Specialty = "PT",
                ExperienceYears = 2,
                Bio = "Strength coach",
                TrackingCode = "TR-MEMBER",
                ApplicationStatus = TrainerApplicationStatus.Interviewing,
                SubmittedAt = DateTime.UtcNow
            };
            db.TrainerApplications.Add(application);
            await db.SaveChangesAsync();
            applicationId = application.Id;
        }

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var match = (await Trainers(scope).Application(applicationId, "manager", default)).Data!.AccountMatch!;
            Assert.Equal(("Matched", "member", true), (match.Status, match.UserId, match.HasUnfinishedPTContract));
            Assert.NotNull(match.MemberServicesUntil);
        }

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var contract = await db.PTPurchases.SingleAsync(p => p.Id == id);
            contract.Status = PTPurchaseStatus.Completed;
            contract.IsCompleted = true;
            await db.SaveChangesAsync();
        }
        await using (var scope = _app.Services.CreateAsyncScope())
            Assert.False((await Trainers(scope).Application(applicationId, "manager", default)).Data!.AccountMatch!.HasUnfinishedPTContract);
    }
}
