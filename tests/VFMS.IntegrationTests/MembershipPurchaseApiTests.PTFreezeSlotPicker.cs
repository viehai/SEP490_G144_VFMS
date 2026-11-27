using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.PersonalTraining;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task PTFreezeSlotPickerValidatesItsOptionalHoldContextAndKeepsLegacyRequestsCompatible()
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        using var created = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var freeze = (await created.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var held = (await member.GetFromJsonAsync<PTFreezeProposal>($"/api/pt/freezes/{freeze.Id}/slots"))!;
        var selected = held.Sessions[0];
        var path = $"/api/pt/contracts/{id}/freeze-slot-options";
        using var legacy = await member.PostAsJsonAsync(path, new PTFreezeSlotOptionsRequest(freeze.ResumeDate));
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.DoesNotContain((await legacy.Content.ReadFromJsonAsync<PTRescheduleSlot[]>())!,
            s => s.Date == selected.Date && s.Status == "Available");
        using var incomplete = await member.PostAsJsonAsync(path, new { From = freeze.ResumeDate, FreezeId = freeze.Id });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        using var foreignFreeze = await member.PostAsJsonAsync(path,
            new { From = freeze.ResumeDate, FreezeId = freeze.Id + 100, OriginalSessionId = selected.OriginalSessionId });
        Assert.Equal(HttpStatusCode.NotFound, foreignFreeze.StatusCode);
        using var foreignSession = await member.PostAsJsonAsync(path,
            new { From = freeze.ResumeDate, FreezeId = freeze.Id, OriginalSessionId = int.MaxValue });
        Assert.Equal(HttpStatusCode.NotFound, foreignSession.StatusCode);
        using var other = Client("gift", "Customer");
        using var forbidden = await other.PostAsJsonAsync(path,
            new { From = freeze.ResumeDate, FreezeId = freeze.Id, OriginalSessionId = selected.OriginalSessionId });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    [Fact]
    public async Task PTFreezeSlotPickerCanMoveItsOwnNineOClockHoldToTenOnTheSameDay()
    {
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        var (id, contract) = await ActivatedPTContract(member);
        using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(
            Enumerable.Range(1, 5).SelectMany(day => new[] { 9, 10, 12, 18 }
                .Select(hour => new PTSlotInput((DayOfWeek)day, new TimeOnly(hour, 0)))).ToArray()));
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync());
        var request = new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel");
        using var previewResponse = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes/preview", request);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PTFreezeProposal>())!;
        var planned = preview.Sessions[0];
        using var created = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes", request with
        {
            Moves = [new(planned.OriginalSessionId, planned.Date, new(9, 0))]
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var freeze = (await created.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var holdsPath = $"/api/pt/freezes/{freeze.Id}/slots";
        var held = (await member.GetFromJsonAsync<PTFreezeProposal>(holdsPath))!;
        var selected = held.Sessions.Single(s => s.OriginalSessionId == planned.OriginalSessionId);
        Assert.Equal(new TimeOnly(9, 0), selected.StartTime);

        using var optionsResponse = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freeze-slot-options", new
        {
            From = freeze.ResumeDate, FreezeId = freeze.Id, OriginalSessionId = selected.OriginalSessionId
        });
        Assert.True(optionsResponse.IsSuccessStatusCode, await optionsResponse.Content.ReadAsStringAsync());
        var options = (await optionsResponse.Content.ReadFromJsonAsync<PTRescheduleSlot[]>())!;
        Assert.Equal("Available", options.Single(s => s.Date == selected.Date && s.StartTime == new TimeOnly(10, 0)).Status);
        var other = held.Sessions.First(s => s.OriginalSessionId != selected.OriginalSessionId);
        Assert.DoesNotContain(options, s => s.Date == other.Date && s.Status == "Available");

        using var duplicateDay = await member.PutAsJsonAsync(holdsPath, new PTFreezeSlotChange(
            [new(selected.OriginalSessionId, other.Date, new(10, 0))]));
        Assert.Equal(HttpStatusCode.Conflict, duplicateDay.StatusCode);
        using var changed = await member.PutAsJsonAsync(holdsPath, new PTFreezeSlotChange(
            [new(selected.OriginalSessionId, selected.Date, new(10, 0))]));
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTFreezeProposal>(holdsPath))!;
        Assert.Equal(new TimeOnly(10, 0), after.Sessions.Single(s => s.OriginalSessionId == selected.OriginalSessionId).StartTime);
        Assert.Equal(held.Sessions.Where(s => s.OriginalSessionId != selected.OriginalSessionId),
            after.Sessions.Where(s => s.OriginalSessionId != selected.OriginalSessionId));
    }

    [Theory]
    [InlineData("/preview")]
    [InlineData("")]
    public async Task PTFreezeSlotPickerRejectsLegacyTrainerReplacementRequestsAndStillAllowsNormalFreeze(string suffix)
    {
        using var member = Client("member", "Member");
        var (id, contract) = await ActivatedPTContract(member);
        var templateIds = await AddFreezeReplacementTrainer();
        var request = new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel");
        using var rejected = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes{suffix}", request with
        {
            ReplacementTrainerId = "pt2", ReplacementTemplateIds = templateIds
        });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            Assert.Empty(await db.PTFreezes.ToListAsync());
            Assert.False(await db.PTSessions.AnyAsync(s => s.PTPurchaseId == id && s.PTUserId != "buyer"));
        }
        using var normal = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes", request);
        Assert.True(normal.StatusCode == HttpStatusCode.Created, await normal.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PTFreezeSlotPickerApprovesAPreviouslyStoredTrainerTransferWithItsHeldSlots()
    {
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        var (id, contract) = await ActivatedPTContract(member);
        await AddFreezeReplacementTrainer();
        using var created = await member.PostAsJsonAsync($"/api/pt/contracts/{id}/freezes",
            new PTFreezeRequest(contract.StartDate, contract.StartDate.AddDays(7), "Travel"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var freeze = (await created.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            (await db.PTFreezes.SingleAsync(f => f.Id == freeze.Id)).ReplacementTrainerId = "pt2";
            foreach (var hold in await db.PTSessions.Where(s => s.PTFreezeId == freeze.Id && s.SessionStatus == PTSessionStatus.FreezeHold).ToListAsync())
                hold.PTUserId = "pt2";
            await db.SaveChangesAsync();
        }
        using var approved = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision", new PTFreezeDecision(true, null));
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
        await using var check = _app.Services.CreateAsyncScope();
        var verifyDb = check.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.True(await verifyDb.PTSessions.AnyAsync(s => s.PTFreezeId == freeze.Id &&
            s.SessionStatus == PTSessionStatus.Scheduled && s.PTUserId == "pt2"));
    }

    private async Task<int[]> AddFreezeReplacementTrainer()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            var role = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "PT");
            db.ApplicationUsers.Add(User("pt2", gym, role));
            db.TrainerProfiles.Add(new TrainerProfile { PTUserId = "pt2", Level = TrainerLevel.Senior,
                AcceptingNewMembers = true, TrainerRoleId = _ptTrainerRole });
            await db.SaveChangesAsync();
        }
        using var trainer = Client("pt2", "PT");
        using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(
            Enumerable.Range(1, 5).SelectMany(day => new[] { 9, 18 }
                .Select(hour => new PTSlotInput((DayOfWeek)day, new(hour, 0)))).ToArray()));
        Assert.True(published.IsSuccessStatusCode, await published.Content.ReadAsStringAsync());
        return (await trainer.GetFromJsonAsync<PTWeeklySlot[]>("/api/pt/trainers/pt2/weekly-template"))!
            .Where(s => s.StartTime == new TimeOnly(9, 0) &&
                s.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
            .Select(s => s.TemplateId).ToArray();
    }
}
