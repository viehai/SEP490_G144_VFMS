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
    [InlineData(-1, "Scheduled")]
    [InlineData(0, "Active")]
    [InlineData(60, "Active")]
    [InlineData(120, "Expired")]
    [InlineData(121, "Expired")]
    public async Task EffectiveStatusUsesServerClockAndExclusiveEndBoundary(int minutesSinceStart, string status)
    {
        using var manager = Client();
        var start = _clock.Now.UtcDateTime;
        var request = Request();
        request["startAt"] = start;
        request["endAt"] = start.AddHours(2);
        var id = (await Create(manager, request)).GetProperty("id").GetInt32();
        _clock.Now = new DateTimeOffset(start.AddMinutes(minutesSinceStart));
        Assert.Equal(status, (await Detail(manager, id)).GetProperty("status").GetString());
        var listed = Assert.Single((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
        Assert.Equal(status, listed.GetProperty("status").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task CountIncludesOnlyConfirmedHistoryEvenWhenSoftDeletedAndQuotaExhaustionExpires()
    {
        using var manager = Client();
        var request = Request();
        request["maxRedemptions"] = 2;
        var id = (await Create(manager, request)).GetProperty("id").GetInt32();
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        await SeedRedemption(id, VoucherRedemptionStatus.Reserved);
        await SeedRedemption(id, VoucherRedemptionStatus.Released);
        var available = await Detail(manager, id);
        Assert.Equal(1, available.GetProperty("usedCount").GetInt32());
        Assert.Equal("Active", available.GetProperty("status").GetString());
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed, isDeleted: true);
        var exhausted = await Detail(manager, id);
        Assert.Equal(2, exhausted.GetProperty("usedCount").GetInt32());
        Assert.Equal("Expired", exhausted.GetProperty("status").GetString());
        var listed = Assert.Single((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
        Assert.Equal(2, listed.GetProperty("usedCount").GetInt32());
        Assert.Equal("Expired", listed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NullableGlobalQuotaRemainsUnlimitedAndDoesNotConfigurePerUserQuota()
    {
        using var manager = Client();
        var request = Request();
        request["maxRedemptions"] = null;
        request["minimumSpend"] = null;
        var id = (await Create(manager, request)).GetProperty("id").GetInt32();
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        var detail = await Detail(manager, id);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("maxRedemptions").ValueKind);
        Assert.Equal("Active", detail.GetProperty("status").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<VfmsDbContext>().Vouchers.SingleAsync()).MaxRedemptionsPerUser);
    }

    [Theory]
    [InlineData(-2, -1)]
    [InlineData(-1, 1)]
    [InlineData(1, 2)]
    public async Task ExplicitDisabledStateOverridesEveryDateLifecycle(int startDays, int endDays)
    {
        using var manager = Client();
        var request = Request();
        request["startAt"] = _clock.Now.UtcDateTime.AddDays(startDays);
        request["endAt"] = _clock.Now.UtcDateTime.AddDays(endDays);
        request["isEnabled"] = false;
        var created = await Create(manager, request);
        Assert.Equal("Disabled", created.GetProperty("status").GetString());
        Assert.False(created.GetProperty("isEnabled").GetBoolean());
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal("Disabled", (await Detail(manager, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnsafeQuotaReductionRejectsUpdateAndPreservesAllConfigurationAndAudit()
    {
        using var manager = Client();
        var id = (await Create(manager)).GetProperty("id").GetInt32();
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed, isDeleted: true);
        var before = await Detail(manager, id);
        var request = Request(name: "Unsafe rename");
        request["maxRedemptions"] = 1;
        using var rejected = await manager.PutAsJsonAsync($"{Route}/{id}", request);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(before.GetRawText(), (await Detail(manager, id)).GetRawText());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal(2, await db.VoucherRedemptions.IgnoreQueryFilters().CountAsync());

        request["name"] = "Promotion";
        request["maxRedemptions"] = 2;
        var exactQuota = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.OK);
        Assert.Equal(2, exactQuota.GetProperty("maxRedemptions").GetInt32());
        Assert.Equal("Expired", exactQuota.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ClientCannotForgeUsageDerivedStatusActorOrSystemTimestamps()
    {
        using var manager = Client();
        var request = Request();
        request["startAt"] = _clock.Now.UtcDateTime.AddDays(1);
        request["endAt"] = _clock.Now.UtcDateTime.AddDays(2);
        request["usedCount"] = 123;
        request["status"] = "Active";
        request["voucherStatus"] = "Active";
        request["createdByUserId"] = CustomerId;
        request["actorUserId"] = CustomerId;
        request["createdAt"] = "2000-01-01T00:00:00Z";
        request["maxRedemptionsPerUser"] = 1;
        var created = await Create(manager, request);
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(0, created.GetProperty("usedCount").GetInt32());
        Assert.Equal("Scheduled", created.GetProperty("status").GetString());
        await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        request["usedCount"] = 0;
        var updated = await Body(await manager.PutAsJsonAsync($"{Route}/{id}", request), HttpStatusCode.OK);
        Assert.Equal(1, updated.GetProperty("usedCount").GetInt32());
        Assert.Equal("Scheduled", updated.GetProperty("status").GetString());
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var stored = await db.Vouchers.SingleAsync();
        Assert.Equal(ManagerId, stored.CreatedByUserId);
        Assert.NotEqual(new DateTime(2000, 1, 1), stored.CreatedAt);
        Assert.Null(stored.MaxRedemptionsPerUser);
        Assert.All(await db.AuditLogs.ToListAsync(), audit => Assert.Equal(ManagerId, audit.ActorUserId));
    }

    [Fact]
    public async Task DisablingAndEnablingPreservesRedemptionsFinancialHistoryAndExpiredEligibility()
    {
        using var manager = Client();
        var request = Request();
        request["maxRedemptions"] = 1;
        var id = (await Create(manager, request)).GetProperty("id").GetInt32();
        var orderId = await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var voucher = await db.Vouchers.SingleAsync();
            voucher.MaxRedemptionsPerUser = 3;
            await db.SaveChangesAsync();
        }
        var disabled = await Body(await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = false }), HttpStatusCode.OK);
        Assert.Equal("Disabled", disabled.GetProperty("status").GetString());
        Assert.False(disabled.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(1, disabled.GetProperty("usedCount").GetInt32());
        var enabled = await Body(await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = true }), HttpStatusCode.OK);
        Assert.Equal("Expired", enabled.GetProperty("status").GetString());
        Assert.True(enabled.GetProperty("isEnabled").GetBoolean());

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal(3, (await verify.Vouchers.SingleAsync()).MaxRedemptionsPerUser);
        var redemption = Assert.Single(await verify.VoucherRedemptions.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(VoucherRedemptionStatus.Confirmed, redemption.RedemptionStatus);
        Assert.Equal(10000m, redemption.DiscountAmount);
        var order = await verify.Orders.Include(row => row.PaymentTransaction).ThenInclude(row => row!.Invoice)
            .SingleAsync(row => row.Id == orderId);
        Assert.Equal(OrderStatus.Paid, order.OrderStatus);
        Assert.Equal(490000m, order.PaymentTransaction!.AmountReceived);
        Assert.Equal(490000m, order.PaymentTransaction.AmountDue);
        Assert.Equal(PaymentStatus.Paid, order.PaymentTransaction.PaymentStatus);
        Assert.Equal(InvoiceStatus.Issued, order.PaymentTransaction.Invoice!.InvoiceStatus);
        var logs = await verify.AuditLogs.OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, log => AssertAudit(log, id));
        Assert.Equal(AuditActionType.Update, logs[1].ActionType);
        Assert.Equal(AuditActionType.Update, logs[2].ActionType);
        Assert.True(JsonSerializer.Deserialize<JsonElement>(logs[1].OldValue!).GetProperty("IsEnabled").GetBoolean());
        Assert.False(JsonSerializer.Deserialize<JsonElement>(logs[1].NewValue!).GetProperty("IsEnabled").GetBoolean());
        Assert.False(JsonSerializer.Deserialize<JsonElement>(logs[2].OldValue!).GetProperty("IsEnabled").GetBoolean());
        Assert.True(JsonSerializer.Deserialize<JsonElement>(logs[2].NewValue!).GetProperty("IsEnabled").GetBoolean());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("toggle")]
    public async Task FailedAuditRollsBackEveryPrivilegedMutation(string operation)
    {
        using var manager = Client();
        var before = operation == "create" ? (JsonElement?)null : await Create(manager);
        var id = before?.GetProperty("id").GetInt32() ?? 1;
        if (operation != "create") await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        var expected = operation == "create" ? (JsonElement?)null : await Detail(manager, id);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_promotion_audit BEFORE INSERT ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'test promotion audit failure');
            END;
            """);
        using var response = operation switch
        {
            "create" => await manager.PostAsJsonAsync(Route, Request()),
            "update" => await manager.PutAsJsonAsync($"{Route}/{id}", Request(name: "Changed")),
            _ => await manager.PatchAsJsonAsync($"{Route}/{id}/enabled", new { isEnabled = false })
        };
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(operation == "create" ? 0 : 1, await db.Vouchers.IgnoreQueryFilters().CountAsync());
        Assert.Equal(operation == "create" ? 0 : 1, await db.AuditLogs.CountAsync());
        Assert.Equal(operation == "create" ? 0 : 1, await db.VoucherRedemptions.IgnoreQueryFilters().CountAsync());
        if (expected.HasValue) Assert.Equal(expected.Value.GetRawText(), (await Detail(manager, id)).GetRawText());
    }

    [Fact]
    public async Task ListProjectsCountsInBoundedQueriesWithoutLoadingRedemptionHistory()
    {
        using var manager = Client();
        for (var index = 0; index < 5; index++)
        {
            var id = (await Create(manager, Request(code: $"SAVE{index}"))).GetProperty("id").GetInt32();
            await SeedRedemption(id, VoucherRedemptionStatus.Confirmed);
        }
        _selectCounter.Count = 0;
        var rows = (await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray().ToArray();
        Assert.Equal(5, rows.Length);
        Assert.All(rows, row => Assert.Equal(1, row.GetProperty("usedCount").GetInt32()));
        Assert.InRange(_selectCounter.Count, 1, 2);
    }

    [Fact]
    public async Task DatabaseUniqueCodeConstraintRejectsCaseEquivalentDirectWrite()
    {
        using var manager = Client();
        await Create(manager);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        db.Vouchers.Add(new Voucher
        {
            Name = "Direct duplicate", Code = "save20", CreatedByUserId = ManagerId,
            DiscountType = DiscountType.FIXED, DiscountValue = 10000,
            StartAt = _clock.Now.UtcDateTime, EndAt = _clock.Now.UtcDateTime.AddDays(1)
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SoftDeletedHistoricalVoucherStillReservesItsCode()
    {
        using var manager = Client();
        await Create(manager);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var voucher = await db.Vouchers.SingleAsync();
        voucher.IsDeleted = true;
        await db.SaveChangesAsync();
        using var duplicate = await manager.PostAsJsonAsync(Route, Request(code: "save20"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Empty((await Body(await manager.GetAsync(Route), HttpStatusCode.OK)).EnumerateArray());
        using var detail = await manager.GetAsync($"{Route}/{voucher.Id}");
        using var update = await manager.PutAsJsonAsync($"{Route}/{voucher.Id}", Request(name: "Hidden edit"));
        using var toggle = await manager.PatchAsJsonAsync($"{Route}/{voucher.Id}/enabled", new { isEnabled = false });
        Assert.All(new[] { detail, update, toggle }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Single(await db.Vouchers.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    private async Task<int> SeedRedemption(int id, VoucherRedemptionStatus status, bool isDeleted = false)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var order = new Order
        {
            PurchaserUserId = CustomerId, OrderNumber = $"PROMO-{Guid.NewGuid():N}",
            Channel = OrderChannel.Online, OrderStatus = OrderStatus.Paid, CompletedAt = _clock.Now.UtcDateTime,
            PaymentTransaction = new PaymentTransaction
            {
                UserId = CustomerId, GatewayName = "Test", PaymentStatus = PaymentStatus.Paid,
                PaymentMethod = PaymentMethod.VietQR, AmountDue = 490000, AmountReceived = 490000,
                PaidAt = _clock.Now.UtcDateTime,
                Invoice = new Invoice { InvoiceNumber = $"INV-{Guid.NewGuid():N}", IssuedAt = _clock.Now.UtcDateTime }
            }
        };
        db.VoucherRedemptions.Add(new VoucherRedemption
        {
            VoucherId = id, Order = order, DiscountAmount = 10000, RedemptionStatus = status,
            RedeemedAt = _clock.Now.UtcDateTime, IsDeleted = isDeleted
        });
        await db.SaveChangesAsync();
        return order.Id;
    }
}
