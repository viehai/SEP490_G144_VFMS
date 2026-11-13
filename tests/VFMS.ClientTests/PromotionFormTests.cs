using VFMS.Client.Components.Promotions;
using VFMS.Contracts.Promotions;
using VFMS.Contracts.Promotions.Validators;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PromotionFormTests
{
    [Fact]
    public void Edit_RoundTripsUtcTimestampsThroughExplicitGymWallClock()
    {
        var response = PromotionApiClientTests.Response();
        var form = PromotionFormModel.FromResponse(response);
        Assert.Equal(new DateTime(2026, 11, 13, 8, 30, 0), form.StartAt);
        Assert.Equal(DateTimeKind.Unspecified, form.StartAt!.Value.Kind);
        var request = form.UpdateRequest();
        Assert.Equal(response.StartAt, request.StartAt);
        Assert.Equal(DateTimeKind.Utc, request.StartAt!.Value.Kind);
        Assert.Equal(response.EndAt, request.EndAt);
        Assert.True(new UpdatePromotionRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void DateConversion_CrossesCalendarBoundaryWithoutUsingBrowserTimezone()
    {
        var form = new PromotionFormModel { StartAt = new DateTime(2026, 11, 14, 1, 0, 0), EndAt = new DateTime(2026, 11, 15, 1, 0, 0) };
        Assert.Equal(new DateTime(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc), form.CreateRequest().StartAt);
    }

    [Fact]
    public void NewForm_PreservesMissingValuesForSharedValidation()
    {
        var request = new PromotionFormModel().CreateRequest();
        var result = new CreatePromotionRequestValidator().Validate(request);
        Assert.Null(request.DiscountValue);
        Assert.Null(request.StartAt);
        Assert.Null(request.EndAt);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(request.DiscountValue));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(request.StartAt));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(request.EndAt));
    }

    [Fact]
    public void FixedConfiguration_DropsPercentageCapAndAllowsUnlimitedQuota()
    {
        var form = PromotionFormModel.FromResponse(PromotionApiClientTests.Response());
        form.DiscountType = PromotionDiscountType.Fixed;
        form.DiscountValue = 50_000m;
        form.MaxRedemptions = null;
        var request = form.CreateRequest();
        Assert.Null(request.MaxDiscountAmount);
        Assert.Null(request.MaxRedemptions);
        Assert.True(new CreatePromotionRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void PercentageConfiguration_RequiresCapInSharedValidation()
    {
        var form = PromotionFormModel.FromResponse(PromotionApiClientTests.Response());
        form.MaxDiscountAmount = null;
        var result = new CreatePromotionRequestValidator().Validate(form.CreateRequest());
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(form.MaxDiscountAmount));
    }

    [Fact]
    public void LocalRangeValidation_IsAppliedAfterUtcConversion()
    {
        var form = PromotionFormModel.FromResponse(PromotionApiClientTests.Response());
        form.EndAt = form.StartAt;
        Assert.Contains(new UpdatePromotionRequestValidator().Validate(form.UpdateRequest()).Errors,
            failure => failure.PropertyName == nameof(form.EndAt));
    }
}
