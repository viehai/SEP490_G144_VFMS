using VFMS.Client.Components.Memberships;
using Xunit;

namespace VFMS.ClientTests;

public sealed class PlanDescriptionTests
{
    [Fact]
    public void FirstLineIsTheTaglineAndEachFurtherLineABenefit()
    {
        var text = PlanDescription.Parse("Xây dựng thói quen\r\n- Tập Gym 24/7\r\n\r\n  ✓ Tủ Locker điện tử  \n");

        Assert.Equal("Xây dựng thói quen", text.Tagline);
        Assert.Equal(["Tập Gym 24/7", "Tủ Locker điện tử"], text.Features);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("Test plan", "Test plan")]
    public void AOneLineOrEmptyDescriptionHasNoBenefits(string? description, string? tagline)
    {
        var text = PlanDescription.Parse(description);

        Assert.Equal(tagline, text.Tagline);
        Assert.Empty(text.Features);
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(90, 3)]
    [InlineData(180, 6)]
    [InlineData(365, 12)]
    [InlineData(7, 1)]
    [InlineData(null, 1)]
    public void MonthsRoundsTheTermToWholeMonths(int? days, int months) =>
        Assert.Equal(months, PlanDescription.Months(days));
}
