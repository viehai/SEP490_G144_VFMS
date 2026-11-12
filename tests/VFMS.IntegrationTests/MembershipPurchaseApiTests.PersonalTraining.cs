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
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task PTCheckoutUsesTransferTimeAndRechecksReleasedSlots(bool delayed, bool lateTransfer, bool taken)
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var previewResponse = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(12, preview.Sessions.Count);

        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.Equal("Pending", checkout.Status);
        Assert.Null(checkout.VietQr);
        using var invalidVoucher = await member.PostAsJsonAsync($"/api/pt/purchases/{checkout.OrderNumber}/payment",
            new PTPaymentRequest("INVALID"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidVoucher.StatusCode);
        using var second = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        checkout = await IssueQr(member, checkout, "PT-OFF");
        Assert.Equal(550_000m, checkout.AmountDue);
        Assert.Equal(checkout.AmountDue, (await IssueQr(member, checkout)).AmountDue);

        var deadline = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime());
        var transferAt = lateTransfer ? deadline.AddSeconds(1) : deadline.AddMinutes(-14);
        _clock.Now = delayed ? deadline.AddSeconds(2) : transferAt.AddSeconds(1);
        if (delayed)
        {
            await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
            await using var workerScope = _app.Services.CreateAsyncScope();
            await workerScope.ServiceProvider.GetRequiredService<VFMS.Infrastructure.PersonalTraining.PersonalTrainingService>()
                .ExpirePending(CancellationToken.None);
        }
        if (taken)
        {
            using var competingHold = await member.PostAsJsonAsync("/api/pt/purchases", request);
            Assert.Equal(HttpStatusCode.Created, competingHold.StatusCode);
            var competing = (await competingHold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
            await using var competingScope = _app.Services.CreateAsyncScope();
            var competingDb = competingScope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var currentHold = await competingDb.PTPurchases.SingleAsync(p => p.Id == competing.ContractId);
            // Order.CreatedAt is set by the database context using wall time, while this test advances the service clock.
            currentHold.HoldExpiresAt = _clock.Now.UtcDateTime.AddMinutes(15);
            await competingDb.SaveChangesAsync();
        }
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 91, transferAt);
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await payment.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(checkout.VietQr.TransferContent, checkout.AmountDue, 91, transferAt);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var contract = await db.PTPurchases.Include(p => p.Sessions).SingleAsync(p => p.Id == checkout.ContractId);
        var unfulfilled = lateTransfer || taken;
        Assert.Equal(unfulfilled ? PTPurchaseStatus.Expired : PTPurchaseStatus.Active, contract.Status);
        Assert.Equal(12, contract.Sessions.Count);
        Assert.All(contract.Sessions, s => Assert.Equal(unfulfilled ? PTSessionStatus.Cancelled : PTSessionStatus.Scheduled, s.SessionStatus));
        Assert.Equal(unfulfilled ? 0 : 1, await db.Invoices.CountAsync());
        Assert.Equal(1, await db.MemberMemberships.CountAsync());
        if (lateTransfer)
        {
            using var retry = await member.PostAsJsonAsync("/api/pt/purchases", request);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        }
    }

    [Fact]
    public async Task PTCanProposeAMakeUpButOnlyMemberAcceptanceReservesTheReturnedCredit()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var held = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, held.StatusCode);
        var order = await IssueQr(member, (await held.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transfer = new DateTimeOffset(order.ExpiresAt).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(order.VietQr!.TransferContent, order.AmountDue, 96, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var path = $"/api/pt/contracts/{order.ContractId}";
        var contract = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        var old = contract.Sessions[0];
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var historical = await db.PTSessions.SingleAsync(s => s.Id == old.Id);
            historical.SessionStatus = PTSessionStatus.Cancelled;
            historical.CancelledAt = _clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }

        var route = $"/api/pt/sessions/{old.Id}/make-up";
        var makeUp = new PTMakeUpRequest(old.Date, new TimeOnly(15, 0));
        using var forbidden = await member.PostAsJsonAsync(route, makeUp);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var proposed = await trainer.PostAsJsonAsync(route, makeUp);
        Assert.True(proposed.StatusCode == HttpStatusCode.Created, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal("MakeUpPending", pending.Status);
        Assert.Equal(1, (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single().AvailableCredits);
        using var accepted = await member.PostAsJsonAsync($"/api/pt/sessions/{pending.Id}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal!.ExpiresAt));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        Assert.Equal(0, after.AvailableCredits);
        Assert.Contains(after.Sessions, s => s.Id == old.Id && s.Status == "Cancelled");
        Assert.Contains(after.Sessions, s => s.Id == pending.Id && s.Status == "Scheduled");
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("conflict")]
    [InlineData("expire")]
    public async Task OnlyAssignedPTCanMoveARequestedSessionAndCreditsStayReserved(string outcome)
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var otherMember = Client("gift", "Member");
        using var trainer = Client("buyer", "PT");
        using var otherTrainer = Client("other-pt", "PT");
        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        checkout = await IssueQr(member, checkout);
        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 92, transferAt);
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
        var path = $"/api/pt/contracts/{checkout.ContractId}";
        var contract = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        var session = contract.Sessions[0];
        var busyTime = new TimeOnly(9, 0);
        var newTime = new TimeOnly(12, 0);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var membership = await db.MemberMemberships.SingleAsync();
        var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
        var roles = await db.ApplicationRoles.ToDictionaryAsync(role => role.RoleName);
        db.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = "gift", Role = roles["Member"] });
        db.ApplicationUsers.Add(User("other-pt", gym, roles["PT"]));
        var competing = new PTSession
        {
            MemberUserId = "gift", PTUserId = "buyer", SessionDate = session.Date,
            StartTime = busyTime, EndTime = busyTime.AddHours(1), Note = "Private coaching note"
        };
        db.PTPurchases.Add(new PTPurchase
        {
            MemberUserId = "gift", PTUserId = "buyer", MemberMembershipId = membership.Id,
            StartDate = contract.StartDate, EndDate = contract.EndDate, Sessions = [competing]
        });
        await db.SaveChangesAsync();
        var sessionPath = $"/api/pt/sessions/{session.Id}";
        var optionsPath = $"{sessionPath}/reschedule-options?from={session.Date:yyyy-MM-dd}";
        using var forbidden = await otherMember.GetAsync(optionsPath);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var options = await member.GetAsync(optionsPath);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var calendarJson = System.Text.Json.JsonDocument.Parse(await options.Content.ReadAsStringAsync());
        Assert.All(calendarJson.RootElement.EnumerateArray(), slot =>
            Assert.Equal(new[] { "date", "endTime", "startTime", "status" },
                slot.EnumerateObject().Select(property => property.Name).OrderBy(name => name)));
        var calendar = (await options.Content.ReadFromJsonAsync<PTRescheduleSlot[]>())!;
        Assert.Contains(calendar, slot => slot.Date == session.Date && slot.StartTime == busyTime && slot.Status == "Busy");
        Assert.Contains(calendar, slot => slot.Date == session.Date && slot.StartTime == newTime && slot.Status == "Available");
        using var removed = await member.PostAsync($"{sessionPath}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        using var trainerRequest = await trainer.PostAsJsonAsync($"{sessionPath}/proposal",
            new PTProposalRequest(session.Date, newTime, "Reschedule"));
        Assert.Equal(HttpStatusCode.BadRequest, trainerRequest.StatusCode);
        var requestPath = $"{sessionPath}/reschedule-request";
        using var wrongMember = await otherMember.PostAsJsonAsync(requestPath, new PTRescheduleRequest(session.Date, newTime));
        Assert.Equal(HttpStatusCode.NotFound, wrongMember.StatusCode);
        using var busy = await member.PostAsJsonAsync(requestPath, new PTRescheduleRequest(session.Date, busyTime));
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        using var proposed = await member.PostAsJsonAsync(requestPath, new PTRescheduleRequest(session.Date, newTime));
        Assert.True(proposed.StatusCode == HttpStatusCode.OK, await proposed.Content.ReadAsStringAsync());
        var pending = (await proposed.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal(session.StartTime, pending.StartTime);
        Assert.Equal("RescheduleRequested", pending.Status);
        Assert.Equal(session.Date, pending.Date);
        Assert.Equal(newTime, pending.Proposal!.StartTime);
        var during = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        Assert.Equal(12, during.ReservedCredits);
        var decision = new PTRescheduleDecision(outcome != "decline", session.Date, newTime, pending.Proposal.ExpiresAt);
        using var wrongTrainer = await otherTrainer.PostAsJsonAsync($"{requestPath}/decision", decision);
        Assert.Equal(HttpStatusCode.NotFound, wrongTrainer.StatusCode);
        using var memberDecision = await member.PostAsJsonAsync($"{requestPath}/decision", decision);
        Assert.Equal(HttpStatusCode.Forbidden, memberDecision.StatusCode);
        using var wrongRoute = await member.PostAsJsonAsync($"{sessionPath}/proposal/decision",
            new PTProposalDecision(true, pending.Proposal.ExpiresAt));
        Assert.Equal(HttpStatusCode.Conflict, wrongRoute.StatusCode);

        if (outcome == "conflict")
        {
            competing.StartTime = newTime;
            competing.EndTime = newTime.AddHours(1);
            await db.SaveChangesAsync();
        }
        if (outcome == "expire")
        {
            _clock.Now = new DateTimeOffset(pending.Proposal.ExpiresAt);
            await using var worker = _app.Services.CreateAsyncScope();
            await worker.ServiceProvider.GetRequiredService<VFMS.Infrastructure.PersonalTraining.PersonalTrainingService>()
                .ExpirePending(CancellationToken.None);
        }
        using var decided = await trainer.PostAsJsonAsync($"{requestPath}/decision", decision);
        Assert.True(decided.StatusCode == (outcome is "accept" or "decline" ? HttpStatusCode.OK : HttpStatusCode.Conflict),
            await decided.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single();
        var settled = Assert.Single(after.Sessions, s => s.Id == session.Id);
        Assert.Equal(outcome == "accept" ? newTime : session.StartTime, settled.StartTime);
        Assert.Equal("Scheduled", settled.Status);
        Assert.Null(settled.Proposal);
        Assert.Equal(contract.Sessions.Count, after.Sessions.Count);
        Assert.Equal(12, after.ReservedCredits);
        Assert.Equal(0, after.UsedCredits);
        Assert.Contains(await db.Notifications.AsNoTracking().ToListAsync(),
            n => n.EntityId == session.Id && n.RecipientUserId == "member" && n.Title == "Yêu cầu đổi lịch PT");
    }

    [Fact]
    public async Task ThirteenCreditContractKeepsItsFreezePolicyAfterManagerEditsThePackage()
    {
        var request = await PreparePTCheckout(13, 5, 20);
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        using var trainer = Client("buyer", "PT");
        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checkout = (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        checkout = await IssueQr(member, checkout);
        var transferAt = new DateTimeOffset(checkout.ExpiresAt.ToUniversalTime()).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 93, transferAt);
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        var contractPath = $"/api/pt/contracts/{checkout.ContractId}";
        var before = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(13, before.Sessions.Count);
        Assert.Equal(20, before.RemainingFreezeDays);
        using var edited = await manager.PutAsJsonAsync($"/api/pt/manager/packages/{request.PackageId}",
            PackageInput(13, 5, 5));
        Assert.True(edited.StatusCode == HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var start = before.Sessions[0].Date;
        using var missingReason = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start, start.AddDays(7), " "));
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        using var first = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start, start.AddDays(7), "Travel"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstFreeze = (await first.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        using var duplicate = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start.AddDays(8), start.AddDays(15), "Second"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var rejected = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{firstFreeze.Id}/decision",
            new PTFreezeDecision(false, "Try again"));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var requested = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start, start.AddDays(7), "Travel"));
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        var protectedSessions = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single()
            .Sessions.Where(s => s.Status == "PendingFreeze").ToArray();
        Assert.Equal(3, protectedSessions.Length);
        var decision = $"/api/pt/manager/freezes/{freeze.Id}/decision";
        using var unauthorized = await trainer.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var approved = await manager.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(before.EndDate.AddDays(7), after.EndDate);
        Assert.Equal(13, after.ReservedCredits);
        Assert.Equal(13, after.RemainingFreezeDays);
        Assert.Equal(0, after.UsedCredits);
        Assert.Equal(3, after.Sessions.Count(s => s.Status == "Cancelled"));
        Assert.Equal(13, after.Sessions.Count(s => s.Status == "Scheduled"));
        using var afterApproval = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start.AddDays(8), start.AddDays(15), "Second"));
        Assert.Equal(HttpStatusCode.Conflict, afterApproval.StatusCode);

        using var nextCheckout = await member.PostAsJsonAsync("/api/pt/purchases",
            request with { StartDate = after.EndDate.AddDays(1) });
        Assert.True(nextCheckout.StatusCode == HttpStatusCode.Created, await nextCheckout.Content.ReadAsStringAsync());
        var next = (await nextCheckout.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var newContract = (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{next.ContractId}"))!.Single();
        Assert.Equal(5, newContract.RemainingFreezeDays);
    }

    [Theory]
    [InlineData(0, 5, 20)]
    [InlineData(13, 0, 20)]
    [InlineData(13, 6, 5)]
    public async Task ManagerCannotCreatePackageWithInvalidCreditsOrFreezePolicy(int credits, int minimum, int maximum)
    {
        using var manager = Client("manager", "Manager");
        using var response = await manager.PostAsJsonAsync("/api/pt/manager/packages", PackageInput(credits, minimum, maximum));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PreviewStillReturnsScheduleWhenMembershipDoesNotCoverIt()
    {
        var request = await PreparePTCheckout(13, 5, 20, validity: 90);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var gymMembership = await db.MemberMemberships.SingleAsync();
        gymMembership.EndDate = Today.AddDays(1);
        await db.SaveChangesAsync();
        using var member = Client("member", "Member");
        using var preview = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var schedule = (await preview.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(13, schedule.Sessions.Count);
        Assert.Equal(request.StartDate.AddDays(89), schedule.ExpiresOn);
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, hold.StatusCode);
        Assert.Empty(await db.PTPurchases.ToListAsync());
    }

    [Fact]
    public async Task AttendanceCannotCompleteASessionWhenGymMembershipIsSuspended()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var response = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkout = await IssueQr(member, (await response.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transfer = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 95, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var session = await db.PTSessions.AsNoTracking().OrderBy(s => s.SessionDate).FirstAsync();
        var membership = await db.MemberMemberships.SingleAsync();
        membership.MembershipStatus = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        _clock.Now = new DateTimeOffset(session.SessionDate.ToDateTime(session.EndTime), TimeSpan.FromHours(7)).AddMinutes(1);
        using var attendance = await trainer.PostAsJsonAsync($"/api/pt/sessions/{session.Id}/attendance", true);
        Assert.Equal(HttpStatusCode.Conflict, attendance.StatusCode);
        Assert.Equal(PTSessionStatus.Scheduled, await db.PTSessions.Where(s => s.Id == session.Id).Select(s => s.SessionStatus).SingleAsync());
    }

    private static object PackageInput(int credits, int minimum, int maximum, int validity = 60) => new
    {
        name = $"PT {credits}",
        credits,
        validityDays = validity,
        price = 600_000,
        trainerLevel = "Senior",
        freezeMinimumDays = minimum,
        freezeMaxTotalDays = maximum
    };

    private async Task<PTBookingRequest> PreparePTCheckout(int credits = 12, int minimum = 7, int maximum = 15, int validity = 60)
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
            var gym = await db.Gyms.SingleAsync(g => g.GymName == "VFMS");
            gym.OpenTime = new(7, 0);
            gym.CloseTime = new(22, 0);
            var component = await db.PackageComponents.SingleAsync(c => c.PackageId == _basic);
            var order = new Order
            {
                PurchaserUserId = "member",
                OrderNumber = "PT-TEST-GYM",
                OrderStatus = OrderStatus.Paid,
                Items = [new OrderItem
                {
                    PackageId = _basic, BeneficiaryUserId = "member",
                    MemberMemberships = [new MemberMembership
                    {
                        MemberUserId = "member", PackageComponentId = component.Id,
                        MembershipStatus = MembershipStatus.Active, StartDate = Today, EndDate = Today.AddDays(200)
                    }]
                }]
            };
            db.Orders.Add(order);
            db.TrainerProfiles.Add(new TrainerProfile { PTUserId = "buyer", Level = TrainerLevel.Senior });
            db.Vouchers.Add(new Voucher
            {
                Code = "PT-OFF", Name = "Test PT voucher", DiscountValue = 50_000,
                DiscountType = DiscountType.FIXED, CreatedByUserId = "manager",
                StartAt = _clock.Now.UtcDateTime.AddDays(-1), EndAt = _clock.Now.UtcDateTime.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        using var manager = Client("manager", "Manager");
        using var create = await manager.PostAsJsonAsync("/api/pt/manager/packages",
            PackageInput(credits, minimum, maximum, validity));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var package = (await create.Content.ReadFromJsonAsync<PTPackageResponse>())!;
        using var trainer = Client("buyer", "PT");
        var slots = Enumerable.Range(1, 5).SelectMany(day => new[] { 9, 12, 18 }
            .Select(hour => new PTSlotInput((DayOfWeek)day, new TimeOnly(hour, 0)))).ToArray();
        using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(slots));
        Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var start = Today.AddDays(2);
        var calendar = (await trainer.GetFromJsonAsync<PTCalendarSlot[]>($"/api/pt/trainers/buyer/calendar?from={start:yyyy-MM-dd}"))!;
        var pattern = calendar.Where(s => s.StartTime.Hour == 18 &&
            s.Date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
            .Select(s => s.TemplateId).Distinct().ToArray();
        Assert.Equal(3, pattern.Length);
        return new("buyer", package.Id, start, pattern);
    }

    private static async Task<PTCheckoutResponse> IssueQr(HttpClient member, PTCheckoutResponse hold, string? voucher = null)
    {
        using var issued = await member.PostAsJsonAsync($"/api/pt/purchases/{hold.OrderNumber}/payment", new PTPaymentRequest(voucher));
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
        var checkout = (await issued.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        Assert.NotNull(checkout.VietQr);
        Assert.Equal(hold.ExpiresAt, checkout.ExpiresAt);
        return checkout;
    }
}
