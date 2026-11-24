using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class PromotionApiTests
{
    [Theory]
    [InlineData("NewCustomers", 0, "Months", 1)]
    [InlineData("NewCustomers", null, "Months", 1)]
    [InlineData("NewCustomers", 3, null, 1)]
    [InlineData("AllCustomers", 1, "Days", 1)]
    [InlineData("AssignedUsers", null, "Days", 1)]
    [InlineData("AllCustomers", null, null, 0)]
    [InlineData("AllCustomers", null, null, -1)]
    public async Task InvalidAudienceSettingsAreRejected(string audience, int? duration, string? unit, int limit)
    {
        using var manager = Client();
        var request = Request();
        request["audience"] = audience;
        request["newCustomerDuration"] = duration;
        request["newCustomerUnit"] = unit;
        request["maxRedemptionsPerUser"] = limit;
        using var response = await manager.PostAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
    }

    [Fact]
    public async Task EditProtectsConfirmedAndReservedUsesPerPurchaserAndAllowsClearingLimits()
    {
        using var manager = Client();
        var request = Request();
        request["audience"] = "NewCustomers";
        request["newCustomerDuration"] = 1;
        request["newCustomerUnit"] = "Months";
        request["maxRedemptionsPerUser"] = 3;
        var created = await Create(manager, request);
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal("Months", created.GetProperty("newCustomerUnit").GetString());
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        await SeedRedemption(id, VoucherRedemptionStatus.Reserved);
        await SeedRedemption(id, VoucherRedemptionStatus.Released);

        request["maxRedemptionsPerUser"] = 1;
        var rejected = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.BadRequest);
        Assert.Equal("PROMOTION_ACCOUNT_LIMIT_TOO_LOW", rejected.GetProperty("code").GetString());
        request["maxRedemptionsPerUser"] = 2;
        request["newCustomerDuration"] = 7;
        request["newCustomerUnit"] = "Days";
        var changed = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.OK);
        Assert.Equal(1, changed.GetProperty("usedCount").GetInt32());
        Assert.Equal("Days", changed.GetProperty("newCustomerUnit").GetString());

        request["audience"] = "AllCustomers";
        request["maxRedemptionsPerUser"] = request["newCustomerUnit"] = request["newCustomerDuration"] = null;
        changed = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.OK);
        Assert.Equal("AllCustomers", changed.GetProperty("audience").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("maxRedemptionsPerUser").ValueKind);
        Assert.Equal(1, changed.GetProperty("usedCount").GetInt32());
    }

    [Fact]
    public async Task PublicationProtectsPromisedTermsAndQuotaAndListReturnsOnlyGrantCount()
    {
        using var manager = Client();
        var request = Request();
        request["audience"] = "AssignedUsers";
        var id = (await Create(manager, request)).GetProperty("id").GetInt32();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var contest = new Competition { CompetitionName = "Giải hội viên", Status = CompetitionStatus.Published };
        contest.Prizes.Add(new CompetitionPrize { Rank = 1, PrizeName = "Hạng nhất", VoucherId = id });
        contest.Prizes.Add(new CompetitionPrize { Rank = 2, PrizeName = "Hạng nhì", VoucherId = id });
        db.Competitions.Add(contest);
        await db.SaveChangesAsync();
        var grant = new VoucherGrant { VoucherId = id, UserId = CustomerId, CompetitionPrizeId = contest.Prizes.First().Id };
        db.VoucherGrants.Add(grant);
        await db.SaveChangesAsync();

        request["name"] = "Đổi phần thưởng";
        var locked = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.Conflict);
        Assert.Equal("PROMOTION_REWARD_COMMITTED", locked.GetProperty("code").GetString());
        using var disable = await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = false });
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode);
        request["name"] = "Promotion";
        request["maxRedemptions"] = 1;
        var low = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.BadRequest);
        Assert.Equal("PROMOTION_USAGE_LIMIT_TOO_LOW", low.GetProperty("code").GetString());
        request["maxRedemptions"] = 2;
        var updated = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.OK);
        Assert.Equal(1, updated.GetProperty("grantedCount").GetInt32());
        Assert.Equal(0, updated.GetProperty("usedCount").GetInt32());
        var listed = (await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray().Single();
        Assert.Equal(1, listed.GetProperty("grantedCount").GetInt32());
        Assert.DoesNotContain(CustomerId, listed.GetRawText());
    }
}
