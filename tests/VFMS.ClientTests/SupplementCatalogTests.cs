using VFMS.Client.Services;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Local only.</summary>
public sealed class SupplementCatalogTests
{
    [Fact]
    public void ListsHaveNoDuplicatesAndBrandsAreSorted()
    {
        Assert.Equal(SupplementCatalog.TypeNames.Count, SupplementCatalog.TypeNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(SupplementCatalog.Brands.Order(StringComparer.OrdinalIgnoreCase), SupplementCatalog.Brands);
        Assert.All(SupplementCatalog.TypeNames, t => Assert.True(t.Length <= 100));
    }

    [Fact]
    public void HintIsCaseInsensitiveAndEmptyForUnknown()
    {
        Assert.NotEmpty(SupplementCatalog.HintOf("whey protein"));
        Assert.Empty(SupplementCatalog.HintOf("Protein-mw3506dz"));
        Assert.Empty(SupplementCatalog.HintOf(null));
    }

    [Fact]
    public void OldValueStaysSelectable()
    {
        Assert.Equal(SupplementCatalog.Brands, SupplementCatalog.WithCurrent(SupplementCatalog.Brands, "BSN"));
        Assert.Equal("Scitec", SupplementCatalog.WithCurrent(SupplementCatalog.Brands, "Scitec")[^1]);
    }
}
