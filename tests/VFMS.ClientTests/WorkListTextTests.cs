using VFMS.Client.Services;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Equipment.Validators;
using VFMS.Contracts.Operations;
using VFMS.Contracts.Supplements;
using VFMS.Contracts.Supplements.Validators;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Work lists open on the items still to handle, and issue replies read in Vietnamese.</summary>
public sealed class WorkListTextTests
{
    private static readonly StatusCountsResponse Counts = new(new Dictionary<string, int>
    {
        ["Reported"] = 4, ["InProgress"] = 2, ["Escalated"] = 1, ["Resolved"] = 2, ["MarkedSafe"] = 1,
        ["Pending"] = 1, ["ReadyForPickup"] = 1, ["OutForDelivery"] = 1, ["Completed"] = 16, ["Cancelled"] = 3,
    });

    [Fact]
    public void WorkCountLabelAddsUpTheOpenStatuses()
    {
        Assert.Equal("Cần xử lý (7)", OperationsText.WorkCountLabel(StatusFilters.OpenIssueStatuses, Counts));
        Assert.Equal("Cần xử lý (3)", OperationsText.WorkCountLabel(StatusFilters.ActiveOrderStatuses, Counts));
        Assert.Equal("Cần xử lý", OperationsText.WorkCountLabel(StatusFilters.OpenIssueStatuses, null));
    }

    [Fact]
    public void ValidatorsAcceptTheWorkFilters()
    {
        Assert.True(new EquipmentIssueListRequestValidator().Validate(new EquipmentIssueListRequest(Status: "Open")).IsValid);
        Assert.True(new SupplementOrderListRequestValidator().Validate(new SupplementOrderListRequest(Status: "Active")).IsValid);
        Assert.False(new EquipmentIssueListRequestValidator().Validate(new EquipmentIssueListRequest(Status: "Active")).IsValid);
    }

    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Reported", "Safety", 0.5, "Mới báo", false)]
    [InlineData("Reported", "Safety", 5, "Mở 5 giờ", false)]
    [InlineData("Reported", "Safety", 30, "Mở 1 ngày", true)]          // Safety is overdue after a day
    [InlineData("InProgress", "Malfunction", 30, "Mở 1 ngày", false)]
    [InlineData("Escalated", "Damage", 9 * 24, "Mở 9 ngày", true)]     // anything else after three days
    [InlineData("Resolved", "Safety", 9 * 24, null, false)]            // closed issues show no age
    public void OpenIssuesShowTheirAge(string status, string type, double hoursAgo, string? age, bool overdue)
    {
        var reported = Now.AddHours(-hoursAgo);
        Assert.Equal(age, OperationsText.IssueAge(status, reported, Now));
        Assert.Equal(overdue, OperationsText.IssueOverdue(status, type, reported, Now));
    }

    [Theory]
    [InlineData("Escalated: Đã khóa máy.", "Chuyển quản lý: Đã khóa máy.")]
    [InlineData("Not safe after repair: Còn rung.", "Chưa an toàn sau sửa chữa: Còn rung.")]
    [InlineData("Disposed: Hỏng khung.", "Thanh lý: Hỏng khung.")]
    [InlineData("Chuyển quản lý: Đã khóa máy.", "Chuyển quản lý: Đã khóa máy.")]
    [InlineData("Đã thay chốt khóa yên mới.", "Đã thay chốt khóa yên mới.")]
    public void OldEnglishNotePrefixesReadInVietnamese(string stored, string shown) =>
        Assert.Equal(shown, OperationsText.Note(stored));
}
