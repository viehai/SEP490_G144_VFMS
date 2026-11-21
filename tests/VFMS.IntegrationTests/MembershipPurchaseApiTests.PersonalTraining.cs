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
    public async Task DemoPTPaymentIsDevelopmentOnlyAndConfirmsTheOwnersHeldSessions()
    {
        var booking = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", booking);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var order = (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var path = $"/api/pt/purchases/{order.OrderNumber}/demo-payment";
        var contractPath = $"/api/pt/contracts/{order.ContractId}";
        var pending = Assert.Single((await member.GetFromJsonAsync<System.Text.Json.JsonElement[]>(contractPath))!);
        Assert.Equal(order.OrderNumber, pending.GetProperty("orderNumber").GetString());

        _app.Configuration["PersonalTraining:EnableDemoPayment"] = "true";
        using var disabled = await member.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);

        _app.Environment.EnvironmentName = "Development";
        Assert.True((await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{order.OrderNumber}"))!.CanSimulatePayment);
        using var otherMember = Client("buyer", "Member");
        using var rejected = await otherMember.PostAsync(path, null);
        Assert.True(rejected.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
        using var demo = await member.PostAsync(path, null);
        Assert.True(demo.IsSuccessStatusCode, await demo.Content.ReadAsStringAsync());
        Assert.True((await demo.Content.ReadFromJsonAsync<PTCheckoutResponse>())!.IsDemoPayment);
        using var retry = await member.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var contract = Assert.Single((await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!);
        Assert.Equal("Active", contract.Status);
        Assert.All(contract.Sessions, session => Assert.Equal("Scheduled", session.Status));
        using var trainer = Client("buyer", "PT");
        var schedulePath = $"/api/pt/teaching-schedule?from={booking.StartDate:yyyy-MM-dd}";
        using var memberSchedule = await member.GetAsync(schedulePath);
        Assert.Equal(HttpStatusCode.Forbidden, memberSchedule.StatusCode);
        var teaching = (await trainer.GetFromJsonAsync<System.Text.Json.JsonElement[]>(schedulePath))!;
        Assert.Equal(3, teaching.Length);
        Assert.All(teaching, slot => Assert.False(string.IsNullOrWhiteSpace(slot.GetProperty("memberName").GetString())));
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        Assert.Equal("PTDemo", (await db.PaymentTransactions.SingleAsync()).GatewayName);
    }

    [Fact]
    public async Task PTBookingUsesPublishedWeeklyTemplateAndAStartingMonday()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        var template = await member.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/pt/trainers/buyer/weekly-template");
        Assert.Equal(15, template!.Length);
        Assert.All(template, slot => Assert.False(slot.TryGetProperty("date", out _)));

        foreach (var start in new[] { request.StartDate.AddDays(-7), request.StartDate.AddDays(1) })
        {
            using var preview = await member.PostAsJsonAsync("/api/pt/schedule/preview", request with { StartDate = start });
            Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
            using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request with { StartDate = start });
            Assert.Equal(HttpStatusCode.BadRequest, hold.StatusCode);
        }
        using var valid = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task ManagerCanChangePTPackageSaleStatusAndDeleteOnlyUnusedPackages()
    {
        var booking = await PreparePTCheckout();
        using var manager = Client("manager", "Manager");
        using var member = Client("member", "Member");
        var route = $"/api/pt/manager/packages/{booking.PackageId}";

        using var denied = await member.DeleteAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var stopped = await manager.PatchAsJsonAsync($"{route}/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.DoesNotContain(await member.GetFromJsonAsync<PTPackageResponse[]>("/api/pt/packages") ?? [],
            p => p.Id == booking.PackageId);
        using var opened = await manager.PatchAsJsonAsync($"{route}/status", new { IsActive = true });
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", booking);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var checkout = (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        using var retained = await manager.DeleteAsync(route);
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        Assert.True((await retained.Content.ReadFromJsonAsync<PTPackageDeleteResponse>())!.KeptHistory);
        var retainedPackage = Assert.Single((await manager.GetFromJsonAsync<PTPackageResponse[]>("/api/pt/manager/packages"))!);
        Assert.False(retainedPackage.IsActive);
        Assert.Equal(0, retainedPackage.SubscriberCount);
        checkout = await IssueQr(member, checkout);
        var transfer = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
        _clock.Now = transfer.AddSeconds(1);
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 3091, transfer);
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await payment.Content.ReadAsStringAsync());
        Assert.Equal(1, Assert.Single((await manager.GetFromJsonAsync<PTPackageResponse[]>("/api/pt/manager/packages"))!).SubscriberCount);

        using var unused = await manager.PostAsJsonAsync("/api/pt/manager/packages", PackageInput(8, 3, 10));
        var unusedPackage = (await unused.Content.ReadFromJsonAsync<PTPackageResponse>())!;
        using var deleted = await manager.DeleteAsync($"/api/pt/manager/packages/{unusedPackage.Id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False((await deleted.Content.ReadFromJsonAsync<PTPackageDeleteResponse>())!.KeptHistory);
        Assert.Equal(booking.PackageId,
            Assert.Single(await manager.GetFromJsonAsync<PTPackageResponse[]>("/api/pt/manager/packages") ?? []).Id);
        using var gym = await manager.DeleteAsync($"/api/pt/manager/packages/{_basic}");
        Assert.Equal(HttpStatusCode.NotFound, gym.StatusCode);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task PTCheckoutUsesTransferTimeAndRechecksReleasedSlots(
        bool delayed, bool lateTransfer, bool taken, bool wrongAmount)
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
        Assert.Equal(deadline.AddMinutes(5), new DateTimeOffset(checkout.HoldExpiresAt));
        _clock.Now = delayed ? deadline.AddMinutes(taken ? 6 : 2) : transferAt.AddSeconds(1);
        if (delayed)
        {
            await RunJobAsync((service, ct) => service.ExpireOrdersAsync(ct));
            await using var workerScope = _app.Services.CreateAsyncScope();
            await workerScope.ServiceProvider.GetRequiredService<VFMS.Application.Services.PersonalTrainingService>()
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
        else if (delayed && !lateTransfer)
        {
            var waiting = await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{checkout.OrderNumber}");
            Assert.Equal("AwaitingConfirmation", waiting!.Status);
            Assert.Null(waiting.VietQr);
            using var competing = await member.PostAsJsonAsync("/api/pt/purchases", request);
            Assert.Equal(HttpStatusCode.Conflict, competing.StatusCode);
        }
        var amountReceived = wrongAmount ? checkout.AmountDue - 100 : checkout.AmountDue;
        using var payment = await NotifyTransferAsync(checkout.VietQr!.TransferContent, amountReceived, 91, transferAt);
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await payment.Content.ReadAsStringAsync());
        using var replay = await NotifyTransferAsync(checkout.VietQr.TransferContent, amountReceived, 91, transferAt);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var contract = await db.PTPurchases.Include(p => p.Sessions).SingleAsync(p => p.Id == checkout.ContractId);
        var unfulfilled = lateTransfer || taken || wrongAmount;
        Assert.Equal(unfulfilled ? PTPurchaseStatus.Expired : PTPurchaseStatus.Active, contract.Status);
        Assert.Equal(12, contract.Sessions.Count);
        Assert.All(contract.Sessions, s => Assert.Equal(unfulfilled ? PTSessionStatus.Cancelled : PTSessionStatus.Scheduled, s.SessionStatus));
        Assert.Equal(unfulfilled ? 0 : 1, await db.Invoices.CountAsync());
        var refunds = await db.PaymentTransactions.Where(p => p.RefundStatus != null).ToListAsync();
        if (unfulfilled)
        {
            var refund = Assert.Single(refunds);
            Assert.Equal(amountReceived, refund.RefundAmount);
            Assert.Equal("Pending", refund.RefundStatus);
        }
        else
            Assert.Empty(refunds);
        Assert.Equal(1, await db.MemberMemberships.CountAsync());
        if (!delayed && !wrongAmount)
        {
            using var extra = await NotifyTransferAsync(checkout.VietQr.TransferContent, checkout.AmountDue, 1991, transferAt);
            Assert.Equal(HttpStatusCode.OK, extra.StatusCode);
            using var repeatedExtra = await NotifyTransferAsync(checkout.VietQr.TransferContent, checkout.AmountDue, 1991, transferAt);
            Assert.Equal(HttpStatusCode.OK, repeatedExtra.StatusCode);
            var current = await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{checkout.OrderNumber}");
            Assert.Equal("Paid", current!.Status);
            Assert.Equal(checkout.AmountDue, Assert.Single(current.Refunds!).Amount);
            var requests = await member.GetFromJsonAsync<VFMS.Contracts.Common.CheckoutRefundView[]>("/api/checkout-refunds/mine");
            var requestForExtra = Assert.Single(requests!);
            Assert.Equal(checkout.OrderNumber, requestForExtra.OrderNumber);
            Assert.Equal(checkout.AmountDue, requestForExtra.Amount);
            var extraPayment = await db.PaymentTransactions.SingleAsync(p => p.GatewayTransactionReference == "1991");
            var original = await db.PaymentTransactions.SingleAsync(p => p.GatewayTransactionReference == "91");
            Assert.Equal(original.Id, extraPayment.SourcePaymentTransactionId);
            Assert.NotEqual(original.OrderId, extraPayment.OrderId);
            Assert.Equal(PaymentStatus.Paid, original.PaymentStatus);
            Assert.Equal("Pending", extraPayment.RefundStatus);
        }
        if (lateTransfer)
        {
            using var retry = await member.PostAsJsonAsync("/api/pt/purchases", request);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        }
    }

    [Fact]
    public async Task PTBookingSlotsShowHeldSlotsWithoutGivingSuggestionsOrMemberDetails()
    {
        var request = await PreparePTCheckout();
        using var trainer = Client("buyer", "PT");
        var path = "/api/pt/availability";
        var slots = (await trainer.GetFromJsonAsync<PTSlotInput[]>(path))!;
        using var published = await trainer.PutAsJsonAsync(path,
            new PTPublishRequest(slots.Concat(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }
                .Select(day => new PTSlotInput(day, new(18, 0)))).Distinct().ToArray()));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var member = Client("member", "Member");
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        using var response = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        var preview = (await response.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(12, preview.Sessions.Count);
        using var bookingSlots = await member.PostAsJsonAsync("/api/pt/schedule/booking-slots",
            new PTBookingSlotsRequest(request.TrainerId, request.PackageId, request.StartDate, request.TemplateIds));
        Assert.Equal(HttpStatusCode.OK, bookingSlots.StatusCode);
        var options = (await bookingSlots.Content.ReadFromJsonAsync<PTWeeklySlot[]>())!;
        Assert.All(options.Where(slot => request.TemplateIds.Contains(slot.TemplateId)),
            slot => Assert.Equal("Đang giữ chỗ", slot.BlockedReason));
        Assert.Contains(options, slot => slot.DayOfWeek == DayOfWeek.Saturday && slot.BlockedReason is null);
    }

    [Fact]
    public async Task PTCanPageThroughOnlyTheirEndedContracts()
    {
        var request = await PreparePTCheckout();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var gym = await db.MemberMemberships.SingleAsync();
        for (var index = 0; index < 21; index++)
        {
            db.PTPurchases.Add(new PTPurchase
            {
                MemberMembershipId = gym.Id,
                MemberUserId = "member",
                PTUserId = request.TrainerId,
                TotalSessions = 1,
                RemainingSessions = 1,
                StartDate = Today.AddDays(-2),
                EndDate = Today.AddDays(-1),
                PurchaseDate = Today.AddDays(-3),
                Status = PTPurchaseStatus.Expired
            });
        }
        await db.SaveChangesAsync();
        using var trainer = Client("buyer", "PT");
        const string path = "/api/pt/assigned-members/page?historical=true";
        var first = (await trainer.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTAssignedMember>>(path))!;
        var second = (await trainer.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTAssignedMember>>(path + "&page=2"))!;
        Assert.Equal(21, first.TotalCount);
        Assert.Equal(20, first.Items.Count);
        var ended = Assert.Single(second.Items);
        Assert.DoesNotContain(first.Items, item => item.ContractId == ended.ContractId);
        var visible = await trainer.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{ended.ContractId}");
        Assert.Equal("Expired", Assert.Single(visible!).Status);
    }

    [Fact]
    public async Task TimelyPTTransferIsRefundedIfTrainerRemovedTheSlotAfterHoldExpired()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var trainer = Client("buyer", "PT");
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var checkout = await IssueQr(member, (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transfer = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
        _clock.Now = new DateTimeOffset(checkout.HoldExpiresAt).AddMinutes(1);
        var path = "/api/pt/availability";
        var slots = (await trainer.GetFromJsonAsync<PTSlotInput[]>(path))!;
        using var updated = await trainer.PutAsJsonAsync(path,
            new PTPublishRequest(slots.Where(s => s.DayOfWeek != DayOfWeek.Monday || s.StartTime.Hour != 18).ToArray()));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var paid = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 96, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var state = (await member.GetFromJsonAsync<PTCheckoutResponse>($"/api/pt/purchases/{checkout.OrderNumber}"))!;
        Assert.Equal("RefundPending", state.Status);
        Assert.Equal(checkout.AmountDue, Assert.Single(state.Refunds!).Amount);
    }

    [Fact]
    public async Task EitherPartyCanProposeAMakeUpAndOnlyTheOtherPartyAcceptanceReservesTheCredit()
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
        // Member-proposed make-up: the PT decides (UC 4.7); declining keeps the make-up unit.
        using var requested = await member.PostAsJsonAsync(route, makeUp);
        Assert.True(requested.StatusCode == HttpStatusCode.Created, await requested.Content.ReadAsStringAsync());
        var memberRequest = (await requested.Content.ReadFromJsonAsync<PTSessionResponse>())!;
        Assert.Equal("MakeUpRequested", memberRequest.Status);
        using var memberCannotDecide = await member.PostAsJsonAsync($"/api/pt/sessions/{memberRequest.Id}/make-up/decision",
            new PTProposalDecision(true, memberRequest.Proposal!.ExpiresAt));
        Assert.Equal(HttpStatusCode.Forbidden, memberCannotDecide.StatusCode);
        using var declined = await trainer.PostAsJsonAsync($"/api/pt/sessions/{memberRequest.Id}/make-up/decision",
            new PTProposalDecision(false, memberRequest.Proposal.ExpiresAt));
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        Assert.Equal(1, (await member.GetFromJsonAsync<PTContractResponse[]>(path))!.Single().MakeUpUnits);

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
            MemberUserId = "gift",
            PTUserId = "buyer",
            SessionDate = session.Date,
            StartTime = busyTime,
            EndTime = busyTime.AddHours(1),
            Note = "Private coaching note"
        };
        db.PTPurchases.Add(new PTPurchase
        {
            MemberUserId = "gift",
            PTUserId = "buyer",
            MemberMembershipId = membership.Id,
            StartDate = contract.StartDate,
            EndDate = contract.EndDate,
            Sessions = [competing]
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
        // The PT may also ask to reschedule (UC 4.7), but not onto the other member's booked hour.
        using var trainerRequest = await trainer.PostAsJsonAsync($"{sessionPath}/proposal",
            new PTProposalRequest(session.Date, busyTime, "Reschedule"));
        Assert.Equal(HttpStatusCode.Conflict, trainerRequest.StatusCode);
        // The double session was removed from UC 4.7.
        using var doubleSession = await trainer.PostAsJsonAsync($"{sessionPath}/proposal",
            new PTProposalRequest(session.Date, session.StartTime, "DoubleSession"));
        Assert.Equal(HttpStatusCode.BadRequest, doubleSession.StatusCode);
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
            await worker.ServiceProvider.GetRequiredService<VFMS.Application.Services.PersonalTrainingService>()
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
        var pendingRequests = (await manager.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTFreezeResponse>>(
            "/api/pt/manager/freezes/page?processed=false&page=1"))!.Items;
        Assert.Equal(freeze.Id, Assert.Single(pendingRequests).Id);
        var processedRequests = (await manager.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PTFreezeResponse>>(
            "/api/pt/manager/freezes/page?processed=true&page=1"))!.Items;
        var processedRequest = Assert.Single(processedRequests);
        Assert.Equal(firstFreeze.Id, processedRequest.Id);
        Assert.Equal("Rejected", processedRequest.Status);
        Assert.Equal("Try again", processedRequest.ReviewNote);
        var protectedSessions = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single()
            .Sessions.Where(s => s.Status == "PendingFreeze").ToArray();
        Assert.Equal(3, protectedSessions.Length);
        var decision = $"/api/pt/manager/freezes/{freeze.Id}/decision";
        using var unauthorized = await trainer.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var approved = await manager.PostAsJsonAsync(decision, new PTFreezeDecision(true, null));
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var after = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(after.Sessions.Where(s => s.Status == "Scheduled").Max(s => s.Date), after.EndDate);
        Assert.Equal(13, after.ReservedCredits);
        Assert.Equal(13, after.RemainingFreezeDays);
        Assert.Equal(0, after.UsedCredits);
        Assert.Equal(3, after.Sessions.Count(s => s.Status == "Cancelled"));
        Assert.Equal(13, after.Sessions.Count(s => s.Status == "Scheduled"));
        using var afterApproval = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start.AddDays(8), start.AddDays(15), "Second"));
        Assert.Equal(HttpStatusCode.Conflict, afterApproval.StatusCode);

        using var nextCheckout = await member.PostAsJsonAsync("/api/pt/purchases",
            request with { StartDate = NextMonday(after.EndDate.AddDays(1)) });
        Assert.True(nextCheckout.StatusCode == HttpStatusCode.Created, await nextCheckout.Content.ReadAsStringAsync());
        var next = (await nextCheckout.Content.ReadFromJsonAsync<PTCheckoutResponse>())!;
        var newContract = (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{next.ContractId}"))!.Single();
        Assert.Equal(5, newContract.RemainingFreezeDays);
    }

    [Fact]
    public async Task EarlyResumeRechecksThePreviewAndPreservesOtherBookings()
    {
        var request = await PreparePTCheckout();
        using var member = Client("member", "Member");
        using var manager = Client("manager", "Manager");
        using var otherMember = Client("gift", "Member");
        using var order = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        var checkout = await IssueQr(member, (await order.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transferAt = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
        _clock.Now = transferAt.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 96, transferAt);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        var contractPath = $"/api/pt/contracts/{checkout.ContractId}";
        var original = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        var start = original.Sessions[0].Date;
        var newResumeDate = original.Sessions[1].Date;
        var previousResumeDate = start.AddDays(7);
        using var requested = await member.PostAsJsonAsync($"{contractPath}/freezes",
            new PTFreezeRequest(start, previousResumeDate, "Travel"));
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var freeze = (await requested.Content.ReadFromJsonAsync<PTFreezeResponse>())!;
        using var approved = await manager.PostAsJsonAsync($"/api/pt/manager/freezes/{freeze.Id}/decision",
            new PTFreezeDecision(true, null));
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
        var approvedContract = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        var path = $"/api/pt/freezes/{freeze.Id}/early-resume";
        using var upcoming = await member.PostAsJsonAsync($"{path}/preview", new PTEarlyResumeRequest(newResumeDate));
        Assert.Equal(HttpStatusCode.Conflict, upcoming.StatusCode);
        _clock.Now = new DateTimeOffset(start.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(7));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var memberRole = await db.ApplicationRoles.SingleAsync(r => r.RoleName == "Member");
        db.ApplicationUserRoles.Add(new ApplicationUserRole { UserId = "gift", Role = memberRole });
        await db.SaveChangesAsync();
        using var forbidden = await otherMember.PostAsJsonAsync($"{path}/preview", new PTEarlyResumeRequest(newResumeDate));
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var preview = await member.PostAsJsonAsync($"{path}/preview", new PTEarlyResumeRequest(newResumeDate));
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var proposal = (await preview.Content.ReadFromJsonAsync<PTEarlyResumePreview>())!;
        var move = Assert.Single(proposal.Moves, x => x.Date == newResumeDate);
        var days = newResumeDate.DayNumber - start.DayNumber;
        Assert.Equal(days, proposal.ActualFrozenDays);

        var membership = await db.MemberMemberships.SingleAsync();
        var occupied = new PTSession
        {
            MemberUserId = "gift",
            PTUserId = "buyer",
            SessionDate = newResumeDate,
            StartTime = original.Sessions[1].StartTime,
            EndTime = original.Sessions[1].EndTime
        };
        db.PTPurchases.Add(new PTPurchase
        {
            MemberUserId = "gift",
            PTUserId = "buyer",
            MemberMembershipId = membership.Id,
            StartDate = start,
            EndDate = original.EndDate,
            Sessions = [occupied]
        });
        await db.SaveChangesAsync();

        using var stale = await member.PostAsJsonAsync($"{path}/confirm",
            new PTEarlyResumeConfirmation(newResumeDate, previousResumeDate, proposal.Moves.ToArray()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var afterConflict = (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single();
        Assert.Equal(approvedContract.EndDate, afterConflict.EndDate);
        Assert.Equal(original.ReservedCredits, afterConflict.ReservedCredits);

        var alternate = move with { StartTime = new TimeOnly(12, 0) };
        using var revised = await member.PostAsJsonAsync($"{path}/preview",
            new PTEarlyResumeRequest(newResumeDate, [alternate]));
        Assert.True(revised.IsSuccessStatusCode, await revised.Content.ReadAsStringAsync());
        var revisedPlan = (await revised.Content.ReadFromJsonAsync<PTEarlyResumePreview>())!;
        var confirmation = new PTEarlyResumeConfirmation(newResumeDate, previousResumeDate, revisedPlan.Moves.ToArray());
        using var confirm = await member.PostAsJsonAsync($"{path}/confirm", confirmation);
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        using var repeated = await member.PostAsJsonAsync($"{path}/confirm", confirmation);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var updated = (await confirm.Content.ReadFromJsonAsync<PTContractResponse>())!;
        Assert.Equal("Frozen", updated.Status);
        Assert.Equal(updated.Sessions.Where(s => s.Status == "Scheduled").Max(s => s.Date), updated.EndDate);
        Assert.Equal(original.RemainingFreezeDays - days, updated.RemainingFreezeDays);
        Assert.Equal(original.ReservedCredits, updated.ReservedCredits);
        Assert.Equal(original.UsedCredits, updated.UsedCredits);
        Assert.Contains(updated.Sessions, s => s.Id == alternate.SessionId && s.Date == newResumeDate && s.StartTime == alternate.StartTime);
        var otherBooking = await db.PTSessions.AsNoTracking().SingleAsync(s => s.Id == occupied.Id);
        Assert.Equal(PTSessionStatus.Scheduled, otherBooking.SessionStatus);
        Assert.Equal(occupied.StartTime, otherBooking.StartTime);
        var recipients = await db.Notifications.Where(n => n.EntityId == freeze.Id && n.Title == "Tập lại PT sớm")
            .OrderBy(n => n.RecipientUserId).Select(n => n.RecipientUserId).ToListAsync();
        Assert.Equal(new[] { "buyer", "member" }, recipients);
        _clock.Now = new DateTimeOffset(newResumeDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
        Assert.Equal("Active", (await member.GetFromJsonAsync<PTContractResponse[]>(contractPath))!.Single().Status);
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
    public async Task ActiveYogaInstructorCannotSellNewPTSessions()
    {
        var request = await PreparePTCheckout();
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var yogaClass = new YogaClass
        {
            ClassName = "Yoga",
            YogaClassType = new YogaClassType { TypeName = "Yoga" },
            InstructorUserId = request.TrainerId,
            ClassStatus = YogaClassStatus.Published,
            MinParticipants = 1,
            MaxParticipants = 10,
            DurationMinutes = 60
        };
        db.YogaClasses.Add(yogaClass);
        await db.SaveChangesAsync();

        using var member = Client("member", "Member");
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.Equal(HttpStatusCode.Conflict, hold.StatusCode);
        Assert.Empty(await db.PTPurchases.ToListAsync());

        yogaClass.ClassStatus = YogaClassStatus.Completed;
        await db.SaveChangesAsync();
        using var available = await member.PostAsJsonAsync("/api/pt/purchases", request);
        Assert.True(available.StatusCode == HttpStatusCode.Created, await available.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckoutRequiresGymOnTheHoldDateOnly(bool activeToday)
    {
        var request = await PreparePTCheckout(13, 5, 20);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        var gymMembership = await db.MemberMemberships.SingleAsync();
        gymMembership.EndDate = activeToday ? Today : Today.AddDays(-1);
        await db.SaveChangesAsync();
        using var member = Client("member", "Member");
        using var preview = await member.PostAsJsonAsync("/api/pt/schedule/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var schedule = (await preview.Content.ReadFromJsonAsync<PTBookingPreview>())!;
        Assert.Equal(13, schedule.Sessions.Count);
        Assert.Equal(schedule.Sessions[^1].Date, schedule.ExpiresOn);
        using var hold = await member.PostAsJsonAsync("/api/pt/purchases", request);
        if (!activeToday)
        {
            Assert.Equal(HttpStatusCode.Conflict, hold.StatusCode);
            Assert.Empty(await db.PTPurchases.ToListAsync());
            return;
        }
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var checkout = await IssueQr(member, (await hold.Content.ReadFromJsonAsync<PTCheckoutResponse>())!);
        var transfer = new DateTimeOffset(checkout.ExpiresAt).AddMinutes(-14);
        gymMembership.MembershipStatus = MembershipStatus.Expired;
        await db.SaveChangesAsync();
        _clock.Now = transfer.AddSeconds(1);
        using var paid = await NotifyTransferAsync(checkout.VietQr!.TransferContent, checkout.AmountDue, 99, transfer);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var contract = (await member.GetFromJsonAsync<PTContractResponse[]>($"/api/pt/contracts/{checkout.ContractId}"))!.Single();
        Assert.Equal("Active", contract.Status);
        Assert.Equal(13, contract.Sessions.Count(s => s.Status == "Scheduled"));
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

    private static object PackageInput(int credits, int minimum, int maximum) => new
    {
        name = $"PT {credits}",
        credits,
        price = 600_000,
        trainerLevel = "Senior",
        freezeMinimumDays = minimum,
        freezeMaxTotalDays = maximum
    };

    [Fact]
    public async Task PTPublishesOneSlotAndCanClearAnUnbookedTemplate()
    {
        await PreparePTCheckout();
        using var trainer = Client("buyer", "PT");
        var path = "/api/pt/availability";
        Assert.Equal(new PTOperatingHours(new(7, 0), new(22, 0)),
            await trainer.GetFromJsonAsync<PTOperatingHours>(path + "/operating-hours"));
        using var valid = await trainer.PutAsJsonAsync(path, new PTPublishRequest([new(DayOfWeek.Monday, new(21, 0))]));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var outside = await trainer.PutAsJsonAsync(path, new PTPublishRequest([new(DayOfWeek.Monday, new(21, 30))]));
        Assert.False(outside.IsSuccessStatusCode);
        using var halfHour = await trainer.PutAsJsonAsync(path, new PTPublishRequest([new(DayOfWeek.Monday, new(19, 30))]));
        Assert.Equal(HttpStatusCode.BadRequest, halfHour.StatusCode);
        using var adjacent = await trainer.PutAsJsonAsync(path, new PTPublishRequest(
            [new(DayOfWeek.Monday, new(19, 0)), new(DayOfWeek.Monday, new(20, 0))]));
        Assert.Equal(HttpStatusCode.OK, adjacent.StatusCode);
        using var overlapping = await trainer.PutAsJsonAsync(path, new PTPublishRequest(
            [new(DayOfWeek.Monday, new(19, 0)), new(DayOfWeek.Monday, new(19, 30))]));
        Assert.False(overlapping.IsSuccessStatusCode);
        using var cleared = await trainer.PutAsJsonAsync(path, new PTPublishRequest([]));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await trainer.GetFromJsonAsync<PTSlotInput[]>(path))!);
    }

    private async Task<PTBookingRequest> PreparePTCheckout(int credits = 12, int minimum = 7, int maximum = 15)
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
            db.TrainerProfiles.Add(new TrainerProfile
            {
                PTUserId = "buyer",
                Level = TrainerLevel.Senior,
                AcceptingNewMembers = true,
                TrainerRoleId = _ptTrainerRole
            });
            db.Vouchers.Add(new Voucher
            {
                Code = "PT-OFF",
                Name = "Test PT voucher",
                DiscountValue = 50_000,
                DiscountType = DiscountType.FIXED,
                CreatedByUserId = "manager",
                StartAt = _clock.Now.UtcDateTime.AddDays(-1),
                EndAt = _clock.Now.UtcDateTime.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        using var manager = Client("manager", "Manager");
        using var create = await manager.PostAsJsonAsync("/api/pt/manager/packages",
            PackageInput(credits, minimum, maximum));
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var package = (await create.Content.ReadFromJsonAsync<PTPackageResponse>())!;
        using var trainer = Client("buyer", "PT");
        var slots = Enumerable.Range(1, 5).SelectMany(day => new[] { 9, 12, 18 }
            .Select(hour => new PTSlotInput((DayOfWeek)day, new TimeOnly(hour, 0)))).ToArray();
        using var published = await trainer.PutAsJsonAsync("/api/pt/availability", new PTPublishRequest(slots));
        Assert.True(published.StatusCode == HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        // GBR-12: the first Monday that can still be held today (at least 3 days ahead).
        var start = NextMonday(Today.AddDays(3));
        var calendar = (await trainer.GetFromJsonAsync<PTCalendarSlot[]>($"/api/pt/trainers/buyer/calendar?from={start:yyyy-MM-dd}"))!;
        var pattern = calendar.Where(s => s.StartTime.Hour == 18 &&
            s.Date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Friday)
            .Select(s => s.TemplateId).Distinct().ToArray();
        Assert.Equal(3, pattern.Length);
        return new("buyer", package.Id, start, pattern);
    }

    private static DateOnly NextMonday(DateOnly date) =>
        date.AddDays(((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7);

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
