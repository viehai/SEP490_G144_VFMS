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
    public async Task GiftRecipientCanReviewOnlyTheUsedPackageAndOnlyApprovedVisibleFeedbackIsPublic()
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
        var pending = (await saved.Content.ReadFromJsonAsync<FeedbackResponse>())!;
        Assert.Equal(("Pending", gym.Name), (pending.Status, pending.PackageName));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/feedback", review)).StatusCode);
        Assert.Equal("Pending", (await member.GetFromJsonAsync<PackageReviewsResponse>(url))!.MyReviewStatus);
        var unpublished = (await guest.GetFromJsonAsync<PackageReviewsResponse>(url))!;
        Assert.Equal(0, unpublished.TotalReviews);
        Assert.Null(unpublished.MyReviewStatus);
        Assert.Null(unpublished.AverageRating);

        // Gym-wide feedback from the same author does not belong to the package.
        using var general = await member.PostAsJsonAsync("/api/feedback", review with { Rating = 1, PackageId = null });
        var generalId = (await general.Content.ReadFromJsonAsync<FeedbackResponse>())!.Id;
        (await manager.PostAsync($"/api/feedback/manager/{generalId}/approve", null)).EnsureSuccessStatusCode();
        (await manager.PostAsync($"/api/feedback/manager/{pending.Id}/approve", null)).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/feedback/manager/{pending.Id}/reply", new FeedbackReplyRequest("Cảm ơn bạn."))).EnsureSuccessStatusCode();
        var published = (await guest.GetFromJsonAsync<PackageReviewsResponse>(url))!;
        Assert.Equal(1, published.TotalReviews);
        Assert.Equal(4d, published.AverageRating);
        var item = Assert.Single(published.Reviews.Items);
        Assert.Equal((review.Comment, "Cảm ơn bạn."), (item.Comment, item.ManagerReply));
        Assert.Empty((await guest.GetFromJsonAsync<PackageReviewsResponse>($"{CatalogRoute}/{other.Id}/reviews"))!.Reviews.Items);
        Assert.Empty((await guest.GetFromJsonAsync<PackageReviewsResponse>(url + "?rating=5"))!.Reviews.Items);
        (await manager.PatchAsync($"/api/feedback/manager/{pending.Id}/visibility?visible=false", null)).EnsureSuccessStatusCode();
        Assert.Empty((await guest.GetFromJsonAsync<PackageReviewsResponse>(url))!.Reviews.Items);
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
        var pt = await CreatePackage("PT 12", ComponentType.PT);
        var otherPt = await CreatePackage("PT 24", ComponentType.PT);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VfmsDbContext>();
        await AddReviewUsers(db);
        var ptLine = ReviewLine(pt);
        var contract = new PTPurchase { MemberMembership = ReviewMembership(gym), MemberUserId = "test-user",
            PTUserId = "staff", Order = ptLine.Order, TotalSessions = 12 };
        var session = new PTSession { PTPurchase = contract, PTUserId = "staff", MemberUserId = "test-user", SessionStatus = PTSessionStatus.AutoCompleted };
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
