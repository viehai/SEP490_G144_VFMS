using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Common;
using VFMS.Contracts.Lockers;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class MembershipPurchaseApiTests
{
    [Fact]
    public async Task LockerHistoryPagesOnlyOwnedClosedContractsAndProtectsDetails()
    {
        var (lockerId, policyId) = await PrepareLocker();
        int[] ownedIds;
        int otherId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var closed = Enumerable.Range(1, 13).Select(i => HistoryRental($"OLD-{i:00}", "member",
                i == 1 ? LockerContractStatus.Abandoned :
                i == 2 ? LockerContractStatus.Cancelled : LockerContractStatus.Terminated)).ToArray();
            var other = HistoryRental("OTHER", "gift", LockerContractStatus.Terminated);
            var deleted = HistoryRental("DELETED", "member", LockerContractStatus.Terminated);
            deleted.IsDeleted = true;
            var unpaid = HistoryRental("UNPAID", "member", LockerContractStatus.Cancelled);
            unpaid.HandedOverAt = null;
            db.LockerRentalContracts.AddRange(closed);
            db.LockerRentalContracts.AddRange(other, deleted, unpaid,
                HistoryRental("CURRENT", "member", LockerContractStatus.Active));
            (await db.Lockers.SingleAsync(l => l.Id == lockerId)).LockerStatus = LockerStatus.Inactive;
            await db.SaveChangesAsync();
            ownedIds = closed.Select(r => r.Id).OrderDescending().ToArray();
            otherId = other.Id;

            LockerRentalContract HistoryRental(string number, string owner, LockerContractStatus status) => new()
            {
                ContractNumber = number, CustomerUserId = owner, LockerId = lockerId,
                LockerPricingPolicyId = policyId, ContractStatus = status,
                StartDate = Today.AddMonths(-2), EndDate = Today.AddMonths(-1),
                HandedOverAt = _clock.Now.UtcDateTime.AddMonths(-2), CreatedAt = _clock.Now.UtcDateTime.AddMonths(-2)
            };
        }

        using var member = Client("member", "Member");
        var first = await member.GetFromJsonAsync<PagedResponse<LockerRentalView>>("/api/lockers/my-rentals/history");
        Assert.NotNull(first);
        Assert.Equal(13, first.TotalCount);
        Assert.Equal(10, first.PageSize);
        Assert.Equal(ownedIds.Take(10), first.Items.Select(r => r.Id));
        var second = await member.GetFromJsonAsync<PagedResponse<LockerRentalView>>("/api/lockers/my-rentals/history?page=2");
        Assert.Equal(ownedIds.Skip(10), second!.Items.Select(r => r.Id));
        Assert.Equal("Abandoned", second.Items.Last().Status);
        Assert.Contains(second.Items, r => r.Status == "Cancelled");
        var last = await member.GetFromJsonAsync<PagedResponse<LockerRentalView>>("/api/lockers/my-rentals/history?page=2147483647");
        Assert.Equal(2, last!.Page);
        var detail = await member.GetFromJsonAsync<LockerRentalView>($"/api/lockers/my-rentals/{ownedIds[0]}");
        Assert.Equal(ownedIds[0], detail!.Id);
        Assert.False(detail.CanRenew);
        Assert.Equal(100_000, detail.KeyDeposit);
        using var forbiddenDetail = await member.GetAsync($"/api/lockers/my-rentals/{otherId}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenDetail.StatusCode);
        using var invalidPage = await member.GetAsync("/api/lockers/my-rentals/history?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task LockerHistoryIsEmptyWithoutContractsAndRejectsNonMembers()
    {
        using var member = Client("member", "Member");
        var page = await member.GetFromJsonAsync<PagedResponse<LockerRentalView>>("/api/lockers/my-rentals/history");
        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
        using var staff = Client("staff", "Staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/lockers/my-rentals/history")).StatusCode);
        using var locked = Client("locked", "Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await locked.GetAsync("/api/lockers/my-rentals/history")).StatusCode);
    }
}
