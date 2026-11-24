using Microsoft.EntityFrameworkCore;
using VFMS.Application.Services;
using VFMS.Contracts.Competitions;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

public sealed class CompetitionRewardSqlTests
{
    [SqlTrainerFact]
    public async Task WholeRankingIsAtomicAndConcurrentConfirmationGrantsEachPrizeOnlyOnce()
    {
        await using var database = new TrainerTestDatabase();
        await database.Initialize();
        var db = database.Db;
        var gymId = await db.Gyms.Select(g => g.Id).FirstAsync();
        var first = new ApplicationUser { UserId = "first", FullName = "Hạng nhất", GymId = gymId, Email = "first@example.test", PhoneNumber = "0900000011" };
        var second = new ApplicationUser { UserId = "second", FullName = "Hạng nhì", GymId = gymId, Email = "second@example.test", PhoneNumber = "0900000012" };
        var firstVoucher = NewVoucher("FIRST");
        var secondVoucher = NewVoucher("SECOND");
        var replacement = NewVoucher("ALTERNATE");
        var previous = new Competition { CompetitionName = "Previous results", Status = CompetitionStatus.Completed };
        var previousPrize = new CompetitionPrize { Competition = previous, Rank = 1, Voucher = secondVoucher, PrizeName = "Previous prize" };
        var competition = new Competition { CompetitionName = "Finals", Status = CompetitionStatus.Completed };
        var top1 = new CompetitionPrize { Competition = competition, Rank = 1, PrizeName = "Top 1", Voucher = firstVoucher };
        var top2 = new CompetitionPrize { Competition = competition, Rank = 2, PrizeName = "Top 2", Voucher = secondVoucher };
        db.AddRange(first, second, replacement, top1, top2,
            new VoucherGrant { CompetitionPrize = previousPrize, Voucher = secondVoucher, User = second },
            new CompetitionJoin { Competition = competition, Rank = 1, Member = first },
            new CompetitionJoin { Competition = competition, Rank = 2, Member = second });
        await db.SaveChangesAsync();
        await using (var attempt = database.CreateContext())
        {
            var blocked = await new CompetitionService(new CompetitionRepository(attempt)).ConfirmAllResultsAsync(competition.Id, "manager", null);
            Assert.False(blocked.IsSuccess);
            Assert.Contains("hạng 2", blocked.ErrorMessage);
        }
        Assert.Equal(0, await db.CompetitionJoins.CountAsync(j => j.ConfirmedAt.HasValue));
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Single(await db.VoucherGrants.ToListAsync());
        await using (var update = database.CreateContext())
        {
            var fixedRewards = await new CompetitionService(new CompetitionRepository(update)).ReplaceUnusableRewardsAsync(competition.Id,
                [new("Top 1", 1, VoucherId: firstVoucher.Id), new("Top 2", 2, VoucherId: replacement.Id)], "manager", null, default);
            Assert.True(fixedRewards.IsSuccess, fixedRewards.ErrorMessage);
        }
        using var start = new Barrier(2);
        var confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var context = database.CreateContext();
            var service = new CompetitionService(new CompetitionRepository(context));
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(15)));
            return await service.ConfirmAllResultsAsync(competition.Id, "manager", null);
        })));
        Assert.All(confirmations, r => Assert.True(r.IsSuccess, r.ErrorMessage));
        Assert.Equal(new[] { 0, 2 }, confirmations.Select(r => r.Data).Order());
        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.Equal(2, await db.CompetitionJoins.CountAsync(j => j.ConfirmedAt.HasValue));
        Assert.True(await db.VoucherGrants.AnyAsync(g => g.CompetitionPrizeId == top1.Id && g.UserId == first.UserId && g.VoucherId == firstVoucher.Id));
        Assert.True(await db.VoucherGrants.AnyAsync(g => g.CompetitionPrizeId == top2.Id && g.UserId == second.UserId && g.VoucherId == replacement.Id));
        await using var duplicate = database.CreateContext();
        duplicate.VoucherGrants.Add(new VoucherGrant { CompetitionPrizeId = top1.Id, VoucherId = firstVoucher.Id, UserId = first.UserId });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    private static Voucher NewVoucher(string code) => new()
    {
        Code = code, Name = code, Audience = VoucherAudience.AssignedUsers, VoucherStatus = VoucherStatus.Active,
        CreatedByUserId = "manager", DiscountType = DiscountType.FIXED, DiscountValue = 10000,
        MaxRedemptionsPerUser = 1, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddMonths(1)
    };
}
