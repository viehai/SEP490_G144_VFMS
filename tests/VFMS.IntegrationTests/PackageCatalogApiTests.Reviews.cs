using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFMS.Contracts.Feedback;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public sealed partial class PackageCatalogApiTests
{
    [Fact]
    public async Task PublicFeedbackSearchFiltersBeforeFiveItemPagination()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await AddReviewUsers(db);
        var gymId = await db.Gyms.Select(gym => gym.Id).SingleAsync();
        var reviewers = Enumerable.Range(1, 6)
            .Select(index => new ApplicationUser
            {
                UserId = $"reviewer-{index}",
                FullName = $"Reviewer {index}",
                GymId = gymId,
                Email = $"reviewer-{index}@example.test",
                PhoneNumber = $"090200000{index}"
            })
            .ToArray();
        db.ApplicationUsers.AddRange(reviewers);
        await db.SaveChangesAsync();
        db.TrainerReviews.AddRange(reviewers.Select((reviewer, index) => new TrainerReview
        {
            MemberUserId = reviewer.UserId,
            TrainerUserId = "staff",
            Rating = 5,
            Comment = $"Từ khóa tìm kiếm phản hồi {index + 1}"
        }));
        await db.SaveChangesAsync();

        using var guest = CreateClient();
        var search = Uri.EscapeDataString("Từ khóa");
        var firstPage = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            $"/api/feedback/public?search={search}&page=1&pageSize=5");
        var secondPage = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            $"/api/feedback/public?search={search}&page=2&pageSize=5");
        var noMatches = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?search=not-found&page=1&pageSize=5");

        Assert.Equal(6, firstPage!.TotalCount);
        Assert.Equal(5, firstPage.Items.Count);
        Assert.All(firstPage.Items, item => Assert.Contains("Từ khóa", item.Comment));
        Assert.Equal(6, secondPage!.TotalCount);
        Assert.Single(secondPage.Items);
        Assert.DoesNotContain(secondPage.Items.Single().Id, firstPage.Items.Select(item => item.Id));
        Assert.Equal(0, noMatches!.TotalCount);
        Assert.Empty(noMatches.Items);
    }

    [Fact]
    public async Task GiftRecipientCanReviewOnlyTheUsedPackageAndReviewsArePublicWithoutManagerApproval()
    {
        var gym = await CreatePackage("Gym sáng", ComponentType.GYM);
        var other = await CreatePackage("Gym tối", ComponentType.GYM);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await AddReviewUsers(db);
        var membership = ReviewMembership(gym);
        db.Add(membership);
        await db.SaveChangesAsync();

        using var guest = CreateClient();
        using var member = CreateClient("Member");
        using var anotherMember = CreateClient("Member", "giver");
        using var giver = CreateClient("Customer", "giver");
        using var manager = CreateClient("Manager", "staff");
        var url = $"{CatalogRoute}/{gym.Id}/reviews";
        var review = new FeedbackSubmitRequest(null, 4, "Phù hợp để tập trước giờ làm.", gym.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/feedback", review)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/feedback", review)).StatusCode);
        db.Add(new CheckIn { MemberUserId = "test-user", MemberMembership = membership, CheckinTime = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.True((await member.GetFromJsonAsync<PackageReviewsResponse>(url))!.CanReview);
        Assert.Equal(HttpStatusCode.Forbidden, (await giver.PostAsJsonAsync("/api/feedback", review)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/feedback", review with { PackageId = other.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync("/api/feedback", review with { Rating = 6 })).StatusCode);

        using var saved = await member.PostAsJsonAsync("/api/feedback", review);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var savedReview = (await saved.Content.ReadFromJsonAsync<FeedbackResponse>())!;
        Assert.Equal(("Approved", gym.Name), (savedReview.Status, savedReview.PackageName));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/feedback", review)).StatusCode);
        Assert.Equal("Approved", (await member.GetFromJsonAsync<PackageReviewsResponse>(url))!.MyReviewStatus);
        var publicPackageReviews = (await guest.GetFromJsonAsync<PackageReviewsResponse>(url))!;
        Assert.Equal(1, publicPackageReviews.TotalReviews);
        Assert.Equal(4d, publicPackageReviews.AverageRating);

        // Gym-wide feedback is also immediately visible in the public feedback feed.
        using var general = await member.PostAsJsonAsync("/api/feedback", review with { Rating = 1, PackageId = null });
        var generalId = (await general.Content.ReadFromJsonAsync<FeedbackResponse>())!.Id;
        var publicFeedback = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=20");
        Assert.Equal(2, publicFeedback!.TotalCount);
        Assert.All(publicFeedback.Items, item => Assert.False(item.IsMine));
        var firstPublicPage = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=1");
        var secondPublicPage = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=2&pageSize=1");
        Assert.Equal(2, firstPublicPage!.TotalCount);
        Assert.Single(firstPublicPage.Items);
        Assert.Single(secondPublicPage!.Items);
        Assert.NotEqual(firstPublicPage.Items.Single().Id, secondPublicPage.Items.Single().Id);
        var memberFeedback = await member.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=20");
        Assert.Contains(memberFeedback!.Items, item => item.Id == generalId && item.IsMine);

        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.PostAsync($"/api/feedback/manager/{generalId}/approve", null)).StatusCode);
        var managementList = await manager.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<FeedbackResponse>>(
            "/api/feedback/manager?page=1&pageSize=10&rating=1");
        Assert.Equal(1, managementList!.TotalCount);
        using var managerReply = await manager.PostAsJsonAsync(
            $"/api/feedback/manager/{generalId}/reply", new FeedbackReplyRequest("Cảm ơn bạn."));
        Assert.Equal(HttpStatusCode.OK, managerReply.StatusCode);
        var publicAfterReply = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=20");
        Assert.Contains(publicAfterReply!.Items, item => item.Id == generalId && item.ManagerReply == "Cảm ơn bạn.");

        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherMember.PutAsJsonAsync($"/api/feedback/{generalId}", new FeedbackUpdateRequest(3, "Không phải của tôi."))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await member.PutAsJsonAsync($"/api/feedback/{generalId}", new FeedbackUpdateRequest(6, "Sai số sao."))).StatusCode);
        using var updated = await member.PutAsJsonAsync(
            $"/api/feedback/{generalId}", new FeedbackUpdateRequest(3, "Đã cập nhật."));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterUpdate = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=20");
        Assert.Contains(afterUpdate!.Items, item => item.Id == generalId &&
            item.Rating == 3 && item.Comment == "Đã cập nhật.");

        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherMember.DeleteAsync($"/api/feedback/{generalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"/api/feedback/{generalId}")).StatusCode);
        var afterDelete = await guest.GetFromJsonAsync<VFMS.Contracts.Common.PagedResponse<PublicFeedbackResponse>>(
            "/api/feedback/public?page=1&pageSize=20");
        Assert.DoesNotContain(afterDelete!.Items, item => item.Id == generalId);

        Assert.Empty((await guest.GetFromJsonAsync<PackageReviewsResponse>($"{CatalogRoute}/{other.Id}/reviews"))!.Reviews.Items);
        Assert.Empty((await guest.GetFromJsonAsync<PackageReviewsResponse>(url + "?rating=5"))!.Reviews.Items);
    }

    [Fact]
    public async Task YogaNeedsPresentAttendanceAndLegacyBookingsMustIdentifyOnePackage()
    {
        var yoga = await CreatePackage("Yoga sáng", ComponentType.YOGA);
        var other = await CreatePackage("Yoga tối", ComponentType.YOGA);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await AddReviewUsers(db);
        var membership = ReviewMembership(yoga);
        var bookingTime = new DateTime(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc); // Nov 1 in Vietnam
        var yogaClass = new YogaClass { ClassName = "Yoga sáng", YogaClassType = new() { TypeName = "Hatha" }, InstructorUserId = "staff" };
        var schedule = new YogaClassSchedule { YogaClass = yogaClass, YogaStudio = new() { GymId = 1, StudioName = "Studio" }, ScheduledDate = new(2026, 11, 3) };
        var booking = new YogaBooking { YogaClass = yogaClass, MemberUserId = "test-user", BookedAt = bookingTime };
        var attendance = new YogaAttendance { YogaClassSchedule = schedule, YogaBooking = booking, MemberUserId = "test-user",
            InstructorUserId = "staff", AttendanceStatus = YogaAttendanceStatus.NoShow };
        db.AddRange(membership, attendance);
        await db.SaveChangesAsync();
        using var member = CreateClient("Member");
        async Task<bool> CanReview(int id) =>
            (await member.GetFromJsonAsync<PackageReviewsResponse>($"{CatalogRoute}/{id}/reviews"))!.CanReview;

        Assert.False(await CanReview(yoga.Id));
        membership.CreatedAt = bookingTime.AddDays(-1); // The DbContext stamps new entities at insert time.
        attendance.AttendanceStatus = YogaAttendanceStatus.Present;
        await db.SaveChangesAsync();
        Assert.True(await CanReview(yoga.Id));
        var overlapping = ReviewMembership(other);
        db.Add(overlapping);
        await db.SaveChangesAsync();
        overlapping.CreatedAt = membership.CreatedAt;
        await db.SaveChangesAsync();
        Assert.False(await CanReview(yoga.Id));
        Assert.False(await CanReview(other.Id));
        booking.OrderItemId = membership.OrderItemId;
        await db.SaveChangesAsync();
        Assert.True(await CanReview(yoga.Id));
        Assert.False(await CanReview(other.Id));
        Assert.Equal(HttpStatusCode.Created, (await member.PostAsJsonAsync("/api/feedback", new FeedbackSubmitRequest(null, 5, "Lớp tốt.", yoga.Id))).StatusCode);
    }

    [Fact]
    public async Task PtUsesTheCompletedSessionAndPtOrderWhileTrainerRatingRemainsSeparate()
    {
        var gym = await CreatePackage("Gym", ComponentType.GYM);
        var pt = await CreatePackage("PT 12", ComponentType.PT, sessionCount: 12);
        var otherPt = await CreatePackage("PT 24", ComponentType.PT);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await AddReviewUsers(db);
        var ptLine = ReviewLine(pt);
        ptLine.UnitPriceSnapshot = pt.Price;
        ptLine.ComponentTermsSnapshot = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { Type = ComponentType.PT, SessionCount = 12 }
        });
        var contract = new PTPurchase { MemberMembership = ReviewMembership(gym), MemberUserId = "test-user",
            PTUserId = "staff", Order = ptLine.Order };
        var session = new PTSession { PTPurchase = contract, PTUserId = "staff", SessionStatus = PTSessionStatus.AutoCompleted };
        db.AddRange(ptLine, session, new TrainerReview { MemberUserId = "test-user", TrainerUserId = "staff", PTPurchase = contract,
            Rating = 2, ReviewStatus = TrainerReviewStatus.Approved, IsVisible = true });
        await db.SaveChangesAsync();
        using var member = CreateClient("Member");
        async Task<PackageReviewsResponse> Reviews(int id) =>
            (await member.GetFromJsonAsync<PackageReviewsResponse>($"{CatalogRoute}/{id}/reviews"))!;
        Assert.False((await Reviews(pt.Id)).CanReview);
        session.SessionStatus = PTSessionStatus.Completed;
        await db.SaveChangesAsync();
        var result = await Reviews(pt.Id);
        Assert.True(result.CanReview);
        Assert.Equal(0, result.TotalReviews);
        Assert.False((await Reviews(otherPt.Id)).CanReview);
        Assert.False((await Reviews(gym.Id)).CanReview);
        Assert.Equal(HttpStatusCode.Created, (await member.PostAsJsonAsync("/api/feedback", new FeedbackSubmitRequest(null, 5, null, pt.Id))).StatusCode);
    }

    private static async Task AddReviewUsers(VfmsDbContext db)
    {
        var gymId = await db.Gyms.Select(g => g.Id).SingleAsync();
        foreach (var (id, phone) in new[] { ("test-user", "0901000001"), ("giver", "0901000002"), ("staff", "0901000003") })
            db.Add(new ApplicationUser { UserId = id, FullName = id, GymId = gymId, Email = id + "@example.test", PhoneNumber = phone });
        await db.SaveChangesAsync();
    }

    private static OrderItem ReviewLine(Package package) => new()
    {
        PackageId = package.Id, BeneficiaryUserId = "test-user", PackageNameSnapshot = package.Name,
        Order = new Order { OrderNumber = Guid.NewGuid().ToString("N"), PurchaserUserId = "giver", OrderStatus = OrderStatus.Paid }
    };

    private static MemberMembership ReviewMembership(Package package) => new()
    {
        OrderItem = ReviewLine(package), PackageComponentId = package.Components.Single().Id, MemberUserId = "test-user",
        MembershipStatus = MembershipStatus.Expired, StartDate = new(2026, 11, 1), EndDate = new(2026, 11, 15)
    };
}
