using VFMS.Client.Services;
using VFMS.Contracts.Equipment;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>The equipment picker on the issue report form matches as the user types, ignoring Vietnamese accents.</summary>
public sealed class EquipmentSearchTests
{
    private static readonly EquipmentResponse Treadmill = new(46, "Máy chạy bộ Precor TRM 885", "TM-0003", 20, "Máy chạy bộ", "Precor",
        "TRM 885", "Operational", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
    private static readonly EquipmentResponse Dumbbells = new(58, "Bộ tạ đơn Rogue cao su 2.5-40 kg", null, 25, "Tạ tự do", "Rogue Fitness",
        null, "Damaged", null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

    [Theory]
    [InlineData("")]
    [InlineData("máy chạy")]
    [InlineData("may chay")]        // without accents
    [InlineData("MAY CHAY")]
    [InlineData("tm-0003")]         // code
    [InlineData("precor")]          // brand
    [InlineData("trm 885")]         // model
    [InlineData("chay precor")]     // words in any order, from different fields
    public void FindsTheTreadmill(string query) => Assert.True(EquipmentSearch.Matches(Treadmill, query));

    [Theory]
    [InlineData("xe đạp")]
    [InlineData("chay rogue")]      // every word has to match
    [InlineData("TM-0004")]
    public void SkipsOtherQueries(string query) => Assert.False(EquipmentSearch.Matches(Treadmill, query));

    [Theory]
    [InlineData("ta don")]
    [InlineData("tạ tự do")]        // category
    [InlineData("rogue")]
    public void FindsByCategoryAndBrandWithoutCode(string query) => Assert.True(EquipmentSearch.Matches(Dumbbells, query));

    [Fact]
    public void FoldRemovesAccentsAndTheVietnameseD() => Assert.Equal("may dap dui", EquipmentSearch.Fold("  Máy Đạp Đùi "));

    [Fact]
    public void DisplayShowsTheCodeWhenThereIsOne()
    {
        Assert.Equal("Máy chạy bộ Precor TRM 885 · TM-0003", EquipmentSearch.Display(Treadmill));
        Assert.Equal("Bộ tạ đơn Rogue cao su 2.5-40 kg", EquipmentSearch.Display(Dumbbells));
    }
}
