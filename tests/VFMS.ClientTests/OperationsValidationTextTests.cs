using FluentValidation;
using VFMS.Client.Services;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Equipment.Validators;
using VFMS.Contracts.Procurement;
using VFMS.Contracts.Procurement.Validators;
using VFMS.Contracts.Supplements;
using VFMS.Contracts.Supplements.Validators;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.SystemAdministration.Validators;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>The equipment, procurement, supplement and system configuration screens show every validator message in Vietnamese.</summary>
public sealed class OperationsValidationTextTests
{
    public static TheoryData<string> Messages()
    {
        var data = new TheoryData<string>();
        void Add<T>(IValidator<T> validator, T request)
        {
            foreach (var failure in validator.Validate(request).Errors) data.Add(failure.ErrorMessage);
        }

        var tooLong = new string('a', 2001);
        Add(new EquipmentRequestValidator(), new EquipmentRequest(" ", PurchasePrice: -1, PurchaseDate: DateOnly.MaxValue, DescriptionUrl: "ftp://x", Note: tooLong));
        Add(new EquipmentCategoryRequestValidator(), new EquipmentCategoryRequest(" ", CodePrefix: " "));
        Add(new EquipmentCategoryRequestValidator(), new EquipmentCategoryRequest("Cardio", CodePrefix: "1"));
        Add(new EquipmentStatusRequestValidator(), new EquipmentStatusRequest("Broken"));
        Add(new EquipmentStatusRequestValidator(), new EquipmentStatusRequest("Retired"));
        Add(new ReportEquipmentIssueRequestValidator(), new ReportEquipmentIssueRequest(0, "Noise", " ", "ftp://x"));
        Add(new PurchaseProposalRequestValidator(), new PurchaseProposalRequest(" ", []));
        Add(new PurchaseProposalRequestValidator(), new PurchaseProposalRequest("Mua máy", [new PurchaseProposalItemRequest(" ", 0, 0)]));
        Add(new SupplementProductRequestValidator(), new SupplementProductRequest(" ", " ", " ", 0, Status: "Gone", StockQuantity: -1));
        Add(new SupplementProductRequestValidator(), new SupplementProductRequest("Whey", "Mô tả", "https://x/a.gif", 1));
        Add(new SupplementStockRequestValidator(), new SupplementStockRequest("Add", 0));
        Add(new SupplementStockRequestValidator(), new SupplementStockRequest("Set", -1));
        Add(new SupplementStockRequestValidator(), new SupplementStockRequest("Move"));
        Add(new SupplementProductListRequestValidator(), new SupplementProductListRequest(MinPrice: -1, MaxPrice: -2, Sort: "random"));
        Add(new SupplementProductListRequestValidator(), new SupplementProductListRequest(MinPrice: 500, MaxPrice: 100));
        Add(new PlaceSupplementOrderRequestValidator(), new PlaceSupplementOrderRequest([], "Drone"));
        Add(new PlaceSupplementOrderRequestValidator(), new PlaceSupplementOrderRequest([new SupplementOrderLineRequest(1, 0)], "HomeDelivery"));
        Add(new SupplementOutForDeliveryRequestValidator(), new SupplementOutForDeliveryRequest(" ", " "));
        Add(new BannedWordRequestValidator(), new BannedWordRequest(" "));
        Add(new BannedWordRequestValidator(), new BannedWordRequest(new string('a', 101)));
        Add(new GymSettingsRequestValidator(), new GymSettingsRequest(" ", null, "12ab", "not-an-email", new TimeOnly(22, 0), new TimeOnly(6, 0), true, -1, -1));
        Add(new GymSettingsRequestValidator(), new GymSettingsRequest("Victor", null, null, null, new TimeOnly(6, 0), null, true));
        Add(new GenerateBusinessReportRequestValidator(), new GenerateBusinessReportRequest("Year", DateOnly.MinValue));
        return data;
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void EveryValidatorMessageIsTranslated(string message)
    {
        var text = OperationsText.Validation(message);

        Assert.NotEqual(message, text);
        Assert.DoesNotMatch(@"\b(must|field|required|invalid|between)\b", text);
    }

    [Theory]
    [InlineData("'Purchase Price' must be greater than or equal to '0'.", "Giá mua không được là số âm.")]
    [InlineData("'Min Price' must be greater than or equal to '0'.", "Giá từ không được là số âm.")]
    [InlineData("'Page' must be between 1 and 1000000. You entered 0.", "Trang phải từ 1 đến 1.000.000.")]
    [InlineData("Stock must be between 0 and 100000.", "Tồn kho phải từ 0 đến 100.000.")]
    [InlineData("Maximum price must not be less than the minimum price.", "Giá đến không được nhỏ hơn giá từ.")]
    public void DefaultMessagesReadNaturally(string message, string expected) =>
        Assert.Equal(expected, OperationsText.Validation(message));

    [Theory]
    [InlineData("BANNED_WORD_EXISTS", "Từ khóa này đã có trong danh sách.")]
    [InlineData("BUSINESS_REPORT_PERIOD_IN_FUTURE", "Kỳ báo cáo chưa kết thúc. Hãy chọn một kỳ đã qua.")]
    [InlineData("GYM_SETTINGS_INVALID_HOURS", "Giờ mở cửa phải sớm hơn giờ đóng cửa.")]
    public void SystemConfigurationErrorsAreVietnamese(string code, string expected) =>
        Assert.Equal(expected, OperationsText.ErrorMessage(code));

    [Fact]
    public void OtherMessagesAreLeftAsTheyAre() =>
        Assert.Equal("Something else.", OperationsText.Validation("Something else."));
}
