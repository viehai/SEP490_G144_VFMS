using VFMS.Application.Services;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

/// <summary>
/// Report 5.1 unit test cases for the submitYogaClassRequest and approveYogaClassRequest sheets, one test per UTCID.
/// Today is Monday 02/11/2026; studio 1 "Lotus" has 12 mats, studio 2 is inactive, class type 5 exists.
/// </summary>
public sealed partial class YogaClassOpeningTests
{
    // ── YogaClassOpeningService.SubmitAsync (GBR-128) ───────────

    [Fact]
    public async Task SubmitRequest_UTCID01_ValidRequestIsPendingAndManagersAreNotified()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(start: Today.AddDays(10), max: 10), null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(YogaClassRequestStatus.Pending, Assert.Single(_requests.Items).RequestStatus);
        Assert.Equal(["m-1", "m-2"], _requests.Notifications.Select(n => n.RecipientUserId));
    }

    [Fact]
    public async Task SubmitRequest_UTCID02_NoPreferredStudioIsAccepted()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: null), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(_requests.Items[0].PreferredStudioId);
    }

    [Fact]
    public async Task SubmitRequest_UTCID03_StartExactlySevenDaysAwayIsAccepted()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(start: Today.AddDays(7)), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitRequest_UTCID04_StartSixDaysAwayIsTooSoon()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(start: Today.AddDays(6)), null);

        Assert.Equal("YOGA_REQUEST_START_TOO_SOON", result.ErrorCode);
        Assert.Empty(_requests.Items);
    }

    [Fact]
    public async Task SubmitRequest_UTCID05_UnknownClassTypeIsRejected()
    {
        var result = await _service.SubmitAsync("yi-1", Submit() with { YogaClassTypeId = 99 }, null);

        Assert.Equal("YOGA_TYPE_NOT_FOUND", result.ErrorCode);
        Assert.Empty(_requests.Items);
    }

    [Fact]
    public async Task SubmitRequest_UTCID06_InactiveStudioIsRejected()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: 2, max: 10), null);

        Assert.Equal("YOGA_STUDIO_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task SubmitRequest_UTCID07_UnknownStudioIsRejected()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: 99, max: 10), null);

        Assert.Equal("YOGA_STUDIO_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task SubmitRequest_UTCID08_ClassSizeEqualToStudioCapacityIsAccepted()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: 1, max: 12), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitRequest_UTCID09_ClassSizeOneAboveStudioCapacityIsRejected()
    {
        var result = await _service.SubmitAsync("yi-1", Submit(studio: 1, max: 13), null);

        Assert.Equal("YOGA_STUDIO_TOO_SMALL", result.ErrorCode);
    }

    [Fact]
    public async Task SubmitRequest_UTCID10_ThirdPendingRequestIsAccepted()
    {
        await _service.SubmitAsync("yi-1", Submit(), null);
        await _service.SubmitAsync("yi-1", Submit(), null);

        var result = await _service.SubmitAsync("yi-1", Submit(), null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, _requests.Items.Count);
    }

    [Fact]
    public async Task SubmitRequest_UTCID11_FourthPendingRequestIsRejected()
    {
        for (var i = 0; i < YogaClassOpeningService.MaxPendingPerInstructor; i++)
            await _service.SubmitAsync("yi-1", Submit(), null);

        var result = await _service.SubmitAsync("yi-1", Submit(), null);

        Assert.Equal((409, "YOGA_REQUEST_PENDING_LIMIT"), (result.StatusCode, result.ErrorCode));
        Assert.Equal(3, _requests.Items.Count);
    }

    // ── YogaClassOpeningService.ApproveAsync (GBR-129, GBR-130) ─

    private async Task<int> PendingRequestAsync()
    {
        var id = (await _service.SubmitAsync("yi-1", Submit(), null)).Data!.Id;
        _requests.Notifications.Clear();
        return id;
    }

    [Fact]
    public async Task ApproveRequest_UTCID01_ApprovalCreatesADraftClassAndNotifiesTheInstructor()
    {
        var id = await PendingRequestAsync();

        var result = await _service.ApproveAsync(id, Form(), "m-1", null);

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(YogaClassStatus.Draft, Assert.Single(_yoga.Classes).ClassStatus);
        Assert.Equal((YogaClassRequestStatus.Approved, "m-1"), (_requests.Items[0].RequestStatus, _requests.Items[0].ReviewedByUserId));
        Assert.Equal("yi-1", Assert.Single(_requests.Notifications).RecipientUserId);
    }

    [Fact]
    public async Task ApproveRequest_UTCID02_ManagerCanAssignAnotherInstructor()
    {
        var id = await PendingRequestAsync();

        var result = await _service.ApproveAsync(id, Form(instructor: "yi-2"), "m-1", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("yi-2", _yoga.Classes[0].InstructorUserId);
    }

    [Fact]
    public async Task ApproveRequest_UTCID03_UnknownRequestIsNotFound()
    {
        var result = await _service.ApproveAsync(999, Form(), "m-1", null);

        Assert.Equal((404, "YOGA_REQUEST_NOT_FOUND"), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_yoga.Classes);
    }

    [Fact]
    public async Task ApproveRequest_UTCID04_AlreadyApprovedRequestCannotBeApprovedAgain()
    {
        var id = await PendingRequestAsync();
        _requests.Items[0].RequestStatus = YogaClassRequestStatus.Approved;

        var result = await _service.ApproveAsync(id, Form(), "m-1", null);

        Assert.Equal((409, "YOGA_REQUEST_ALREADY_DECIDED"), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_yoga.Classes);
    }

    [Fact]
    public async Task ApproveRequest_UTCID05_RejectedRequestCannotBeApproved()
    {
        var id = await PendingRequestAsync();
        _requests.Items[0].RequestStatus = YogaClassRequestStatus.Rejected;

        var result = await _service.ApproveAsync(id, Form(), "m-1", null);

        Assert.Equal((409, "YOGA_REQUEST_ALREADY_DECIDED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task ApproveRequest_UTCID06_WithdrawnRequestCannotBeApproved()
    {
        var id = await PendingRequestAsync();
        _requests.Items[0].RequestStatus = YogaClassRequestStatus.Withdrawn;

        var result = await _service.ApproveAsync(id, Form(), "m-1", null);

        Assert.Equal((409, "YOGA_REQUEST_ALREADY_DECIDED"), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task ApproveRequest_UTCID07_ClassStartInThePastKeepsTheRequestPending()
    {
        var id = await PendingRequestAsync();

        var result = await _service.ApproveAsync(id, Form(start: Today.AddDays(-1)), "m-1", null);

        Assert.Equal("YOGA_CLASS_START_PAST", result.ErrorCode);
        Assert.Equal(YogaClassRequestStatus.Pending, _requests.Items[0].RequestStatus);
        Assert.Empty(_yoga.Classes);
    }

    [Fact]
    public async Task ApproveRequest_UTCID08_InstructorWithoutTheYogaRoleKeepsTheRequestPending()
    {
        var id = await PendingRequestAsync();

        var result = await _service.ApproveAsync(id, Form(instructor: "receptionist"), "m-1", null);

        Assert.Equal("YOGA_INVALID_INSTRUCTOR", result.ErrorCode);
        Assert.Equal(YogaClassRequestStatus.Pending, _requests.Items[0].RequestStatus);
    }

    [Fact]
    public async Task ApproveRequest_UTCID09_ClassStartingTodayIsAccepted()
    {
        var id = await PendingRequestAsync();

        var result = await _service.ApproveAsync(id, Form(start: Today), "m-1", null);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Today, _yoga.Classes[0].StartDate);
    }
}
