using VFMS.Client.Components.Memberships;
using VFMS.Contracts.Yoga;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>GBR-128..GBR-130: how an instructor's class request fills the Manager's create-class form.</summary>
public sealed class YogaClassRequestPrefillTests
{
    private static YogaClassOpeningRequestResponse Request(int? studioId = 4) => new(
        12, "yi-1", "Lan Yoga", 3, "Hatha", "Sunrise Flow", "Gentle start", "Intermediate", 2, 4, 12, 75,
        new DateOnly(2026, 12, 7), new DateOnly(2027, 1, 31), [DayOfWeek.Friday, DayOfWeek.Monday], new TimeOnly(6, 30),
        studioId, studioId is null ? null : "Lotus", "Members asked for it", "Pending", null, null, null, null,
        new DateTime(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ApprovedRequestFillsTheCreateClassForm()
    {
        var form = YogaClassRequestPrefill.ToClassRequest(Request());

        Assert.Equal(3, form.YogaClassTypeId);
        Assert.Equal("Sunrise Flow", form.ClassName);
        Assert.Equal("yi-1", form.InstructorUserId);          // the requesting instructor teaches by default
        Assert.Equal("Intermediate", form.DifficultyLevel);
        Assert.Equal((2, 4, 12, 75), (form.SessionsPerWeek, form.MinParticipants, form.MaxParticipants, form.DurationMinutes));
        Assert.Equal((new DateOnly(2026, 12, 7), new DateOnly(2027, 1, 31)), (form.StartDate, form.EndDate));
        Assert.Equal("Gentle start", form.Description);
    }

    [Fact]
    public void DefaultDeadlineIs2359GymTimeTheDayBeforeStart()
    {
        var form = YogaClassRequestPrefill.ToClassRequest(Request());

        Assert.Equal(new DateTime(2026, 12, 6, 23, 59, 0), YogaClassRequestPrefill.DefaultDeadlineLocal(new DateOnly(2026, 12, 7)));
        Assert.Equal(DateTimeKind.Utc, form.RegistrationDeadline.Kind);
        Assert.Equal(new DateTime(2026, 12, 6, 16, 59, 0, DateTimeKind.Utc), form.RegistrationDeadline);   // UTC+7
    }

    [Fact]
    public void DaysLabelFollowsWeekOrder() =>
        Assert.Equal("T2, T6, CN", YogaClassRequestPrefill.DaysLabel([DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Monday]));

    [Theory]
    [InlineData("Pending", "Chờ duyệt")]
    [InlineData("Approved", "Đã duyệt")]
    [InlineData("Rejected", "Bị từ chối")]
    [InlineData("Withdrawn", "Đã rút")]
    public void StatusLabels(string status, string label) => Assert.Equal(label, YogaClassRequestPrefill.StatusLabel(status));

    [Fact]
    public void DaysMustMatchSessionsPerWeek()
    {
        Assert.Null(YogaClassRequestPrefill.DaysProblem([DayOfWeek.Monday, DayOfWeek.Thursday], 2));
        Assert.Contains("đúng 3 ngày", YogaClassRequestPrefill.DaysProblem([DayOfWeek.Monday], 3));
    }

    [Fact]
    public void StudioMustHoldTheMaxParticipants()
    {
        var studio = new YogaStudioResponse(4, "Lotus", 2, 10, true);
        Assert.Contains("10 thảm", YogaClassRequestPrefill.StudioProblem(studio, 12));
        Assert.Null(YogaClassRequestPrefill.StudioProblem(studio, 10));
        Assert.Null(YogaClassRequestPrefill.StudioProblem(null, 50));   // no studio picked: the Manager chooses
    }
}
