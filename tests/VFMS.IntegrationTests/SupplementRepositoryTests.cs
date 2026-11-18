using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;
using VFMS.Infrastructure.Data;
using VFMS.Infrastructure.Repositories;

namespace VFMS.IntegrationTests;

/// <summary>Product search against a real (SQLite) database. Local only.</summary>
public sealed class SupplementRepositoryTests : IAsyncLifetime
{
    private static readonly SupplementProductStatus[] Store = [SupplementProductStatus.Active, SupplementProductStatus.OutOfStock];
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<VfmsDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<VfmsDbContext>().UseSqlite(_connection).Options;
        await using var db = new PriceSortableContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.SupplementProducts.AddRange(
            Product("Gold Standard 100% Whey", "Optimum Nutrition", "Whey Protein", 1_850_000m),
            Product("Impact Whey Protein", "Myprotein", "Whey Protein", 1_250_000m),
            Product("Creatine Monohydrate", "Optimum Nutrition", "Creatine", 650_000m),
            Product("R1 Protein", "Rule 1", "Whey Protein", 1_700_000m, SupplementProductStatus.OutOfStock),
            Product("Old test product", null, null, 10_000m, SupplementProductStatus.Inactive));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static SupplementProduct Product(string name, string? brand, string? category, decimal price,
        SupplementProductStatus status = SupplementProductStatus.Active) => new()
    {
        ProductName = name, Brand = brand, CatalogCategory = category, UnitPrice = price,
        Description = name, ImageUrl = "https://example.com/p.png", ProductStatus = status,
    };

    private static SupplementProductQuery Query(string? search = null, string? brand = null, decimal? min = null, decimal? max = null,
        SupplementProductSort sort = SupplementProductSort.Name, bool availableFirst = false) =>
        new(Store, null, search, brand, min, max, sort, availableFirst, 1, 20);

    private async Task<IReadOnlyList<SupplementProduct>> Search(SupplementProductQuery query)
    {
        await using var db = new PriceSortableContext(_options);
        return (await new SupplementRepository(db).SearchProductsAsync(query)).Items;
    }

    [Fact]
    public async Task SearchMatchesBrandAsWellAsName()
    {
        var items = await Search(Query("Optimum"));

        Assert.Equal(2, items.Count);
        Assert.All(items, p => Assert.Equal("Optimum Nutrition", p.Brand));
    }

    [Fact]
    public async Task SearchStillMatchesNameAndCategory()
    {
        Assert.Single(await Search(Query("Impact")));
        Assert.Single(await Search(Query("Creatine")));
    }

    [Fact]
    public async Task BrandFilterIsExact()
    {
        Assert.Equal(2, (await Search(Query(brand: "Optimum Nutrition"))).Count);
        Assert.Empty(await Search(Query(brand: "Optimum")));
    }

    [Fact]
    public async Task PriceRangeIsInclusive()
    {
        var items = await Search(Query(min: 650_000m, max: 1_250_000m));

        Assert.Equal(["Creatine Monohydrate", "Impact Whey Protein"], items.Select(p => p.ProductName));
    }

    [Fact]
    public async Task SortsByPriceBothWays()
    {
        var cheapest = await Search(Query(sort: SupplementProductSort.PriceAscending));
        var dearest = await Search(Query(sort: SupplementProductSort.PriceDescending));

        Assert.Equal(650_000m, cheapest[0].UnitPrice);
        Assert.Equal(1_850_000m, dearest[0].UnitPrice);
        Assert.Equal(cheapest.Select(p => p.Id).Reverse(), dearest.Select(p => p.Id));
    }

    [Fact]
    public async Task StoreViewPutsOutOfStockLast()
    {
        var items = await Search(Query(sort: SupplementProductSort.PriceDescending, availableFirst: true));

        // R1 Protein (1.7M) is dearer than Impact Whey but out of stock, so it comes last.
        Assert.Equal("R1 Protein", items[^1].ProductName);
        Assert.Equal("Gold Standard 100% Whey", items[0].ProductName);
    }

    [Fact]
    public async Task BrandsAreDistinctSortedAndSkipBlank()
    {
        await using var db = new PriceSortableContext(_options);
        var brands = await new SupplementRepository(db).GetBrandsAsync(Store);

        Assert.Equal(["Myprotein", "Optimum Nutrition", "Rule 1"], brands);
    }

    [Fact]
    public async Task InactiveProductsNeverMatchTheStoreStatuses()
    {
        Assert.DoesNotContain(await Search(Query("Old test")), p => p.ProductStatus == SupplementProductStatus.Inactive);
    }
}

/// <summary>
/// SQLite cannot ORDER BY decimal (SQL Server can), so this test context stores prices as REAL.
/// The shared SqliteTestContext is sealed and left alone.
/// </summary>
internal sealed class PriceSortableContext(DbContextOptions<VfmsDbContext> options) : VfmsDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<FaceData>().Property(face => face.FaceNetEmbedding).HasColumnType("BLOB");
        modelBuilder.Entity<Voucher>().Property(voucher => voucher.Code).UseCollation("NOCASE");
        modelBuilder.Entity<SupplementProduct>().Property(p => p.UnitPrice).HasConversion<double>();
    }
}
