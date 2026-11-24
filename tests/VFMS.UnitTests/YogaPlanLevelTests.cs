using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;
using Xunit;

namespace VFMS.UnitTests;

/// <summary>Yoga plans carry the highest class level they book; the level is required on every yoga plan.</summary>
public sealed class YogaPlanLevelTests
{
    [Theory]
    [InlineData("YOGA", "Intermediate", true)]
    [InlineData("YOGA", "intermediate", true)]
    [InlineData("YOGA", null, false)]        // yoga plans need a level
    [InlineData("YOGA", "Expert", false)]
    [InlineData("GYM", null, true)]          // gym plans have none
    public void YogaPlansNeedAKnownLevel(string serviceType, string? level, bool valid)
    {
        var request = new MembershipPlanRequest
        {
            Name = "Yoga", DurationInDays = 30, Price = 600_000m, Status = "ACTIVE",
            ServiceType = serviceType, YogaDifficultyLevel = level
        };

        Assert.Equal(valid, new MembershipPlanRequestValidator().Validate(request).IsValid);
    }
}
