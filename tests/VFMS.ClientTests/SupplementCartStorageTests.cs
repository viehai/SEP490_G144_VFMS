using VFMS.Client.Services;
using VFMS.Contracts.Supplements;
using Xunit;
using Line = VFMS.Client.Services.SupplementCartStorage.SavedLine;

namespace VFMS.ClientTests;

/// <summary>Local only.</summary>
public sealed class SupplementCartStorageTests
{
    private static SupplementProductResponse Product(int id, decimal price, bool canOrder = true) =>
        new(id, $"P{id}", "Whey Protein", null, null, price, canOrder ? "Active" : "OutOfStock", canOrder, DateTime.UtcNow, DateTime.UtcNow);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void UnreadableStorageGivesAnEmptyCart(string? json) => Assert.Empty(SupplementCartStorage.Parse(json));

    [Fact]
    public void ParseDropsBadLinesDuplicatesAndCapsQuantity()
    {
        var json = """
            [{"ProductId":1,"Quantity":2,"UnitPrice":10},{"ProductId":1,"Quantity":5,"UnitPrice":10},
             {"ProductId":0,"Quantity":1,"UnitPrice":1},{"ProductId":2,"Quantity":-3,"UnitPrice":1},
             {"ProductId":3,"Quantity":5000,"UnitPrice":1}]
            """;
        var lines = SupplementCartStorage.Parse(json);

        Assert.Equal([(1, 2), (3, 999)], lines.Select(l => (l.ProductId, l.Quantity)));
    }

    [Fact]
    public void ReconcileDropsGoneAndOutOfStockAndReportsPriceChanges()
    {
        Line[] saved = [new(1, 2, 100, "Whey"), new(2, 1, 50, "Gone"), new(3, 1, 70, "Empty"), new(4, 3, 40, "Bar")];
        var current = new Dictionary<int, SupplementProductResponse?>
        {
            [1] = Product(1, 120), [2] = null, [3] = Product(3, 70, canOrder: false), [4] = Product(4, 40),
        };

        var result = SupplementCartStorage.Reconcile(saved, current);

        Assert.Equal([(1, 2), (4, 3)], result.Lines.Select(l => (l.Product.Id, l.Quantity)));
        Assert.Equal(["Gone", "P3"], result.Removed);
        Assert.Equal(["P1"], result.PriceChanged);
        Assert.Equal(120, result.Lines[0].Product.UnitPrice);   // the new price is used
    }

    [Fact]
    public void EachUserHasTheirOwnKey() =>
        Assert.NotEqual(SupplementCartStorage.KeyFor("u1"), SupplementCartStorage.KeyFor("u2"));
}
