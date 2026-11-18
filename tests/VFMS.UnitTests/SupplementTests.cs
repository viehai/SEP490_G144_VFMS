using Mapster;
using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Application.Mappings;
using VFMS.Application.Services;
using VFMS.Contracts.Supplements;
using VFMS.Contracts.Supplements.Validators;
using VFMS.Domain.Common;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.UnitTests;

public sealed class SupplementTests
{
    private const string Customer = "customer-1", OtherCustomer = "customer-2", Staff = "staff-1", Manager = "manager-1";

    private static SupplementProductRequest Product(string name = "Whey Gold", string status = "Active", decimal price = 1_200_000m) =>
        new(name, "Protein powder", "https://cdn.example.com/whey.png", price, "Protein", status);

    private static PlaceSupplementOrderRequest Pickup(params SupplementOrderLineRequest[] lines) => new(lines, "GymPickup");

    private static PlaceSupplementOrderRequest Delivery(params SupplementOrderLineRequest[] lines) =>
        new(lines, "HomeDelivery", "Nguyen Van A", "1 Le Loi, Hanoi", "Call first");

    private static SupplementOrderLineRequest L(int productId, int quantity) => new(productId, quantity);

    // ── Validators ───────────────────────────────────────────────

    [Fact]
    public void ProductValidatorChecksRequiredFieldsImageAndPrice()
    {
        var v = new SupplementProductRequestValidator();
        Assert.True(v.Validate(Product()).IsValid);
        Assert.False(v.Validate(Product() with { Name = " " }).IsValid);
        Assert.False(v.Validate(Product() with { Description = "" }).IsValid);
        Assert.False(v.Validate(Product() with { ImageUrl = "" }).IsValid);
        Assert.False(v.Validate(Product() with { ImageUrl = "https://cdn.example.com/whey.gif" }).IsValid);
        Assert.False(v.Validate(Product() with { ImageUrl = "ftp://cdn.example.com/whey.png" }).IsValid);
        Assert.True(v.Validate(Product() with { ImageUrl = "https://cdn.example.com/a/whey.JPEG?v=2" }).IsValid);
        Assert.False(v.Validate(Product(price: 0)).IsValid);
        Assert.False(v.Validate(Product(status: "Deleted")).IsValid);
    }

    [Fact]
    public void OrderValidatorRequiresCartQuantityAndDeliveryInfo()
    {
        var v = new PlaceSupplementOrderRequestValidator();
        Assert.True(v.Validate(Pickup(L(1, 1))).IsValid);
        Assert.False(v.Validate(Pickup()).IsValid);
        Assert.False(v.Validate(Pickup(L(1, 0))).IsValid);
        Assert.False(v.Validate(new PlaceSupplementOrderRequest([new(1, 1)], "Drone")).IsValid);
        Assert.False(v.Validate(new PlaceSupplementOrderRequest([new(1, 1)], "HomeDelivery", " ", "Addr")).IsValid);
        Assert.False(v.Validate(new PlaceSupplementOrderRequest([new(1, 1)], "HomeDelivery", "A", null)).IsValid);
        Assert.True(v.Validate(Delivery(L(1, 2))).IsValid);
    }

    [Fact]
    public void ListValidatorRejectsReversedDateRange()
    {
        var v = new SupplementOrderListRequestValidator();
        var result = v.Validate(new SupplementOrderListRequest(From: new DateOnly(2026, 5, 2), To: new DateOnly(2026, 5, 1)));
        Assert.Contains(result.Errors, e => e.PropertyName == "From");
        Assert.True(v.Validate(new SupplementOrderListRequest(From: new DateOnly(2026, 5, 1), To: new DateOnly(2026, 5, 1))).IsValid);
    }

    // ── Products (UC-77, UC-83) ──────────────────────────────────

    [Fact]
    public async Task BrandSizeAndFlavourAreSavedTrimmedAndAudited()
    {
        var repo = new Repo();
        var request = Product("Gold Standard 100% Whey") with
        {
            Brand = "  Optimum Nutrition ", Size = "5 lbs (2.27 kg)", Flavour = "Double Rich Chocolate", Category = "Whey Protein"
        };

        var created = await ProductService(repo).CreateAsync(request, Manager, null);

        Assert.Equal("Optimum Nutrition", created.Data!.Brand);
        Assert.Equal("5 lbs (2.27 kg)", created.Data.Size);
        Assert.Equal("Double Rich Chocolate", created.Data.Flavour);
        Assert.Contains("Brand=Optimum Nutrition", Assert.Single(repo.Audits).NewValue);
    }

    [Fact]
    public void BrandSizeAndFlavourHaveLengthLimits()
    {
        var v = new SupplementProductRequestValidator();
        Assert.True(v.Validate(Product() with { Brand = "Rule 1", Size = "300 g", Flavour = "Unflavoured" }).IsValid);
        Assert.False(v.Validate(Product() with { Brand = new string('b', 101) }).IsValid);
        Assert.False(v.Validate(Product() with { Size = new string('s', 51) }).IsValid);
        Assert.False(v.Validate(Product() with { Flavour = new string('f', 101) }).IsValid);
    }

    [Fact]
    public void ListFiltersCheckPriceRangeAndSort()
    {
        var v = new SupplementProductListRequestValidator();
        Assert.True(v.Validate(new SupplementProductListRequest(Brand: "Rule 1", MinPrice: 500_000, MaxPrice: 500_000, Sort: "price_desc")).IsValid);
        Assert.True(v.Validate(new SupplementProductListRequest(MinPrice: 1_000_000)).IsValid);
        Assert.False(v.Validate(new SupplementProductListRequest(MinPrice: 2_000_000, MaxPrice: 1_000_000)).IsValid);
        Assert.False(v.Validate(new SupplementProductListRequest(MinPrice: -1)).IsValid);
        Assert.False(v.Validate(new SupplementProductListRequest(Sort: "cheapest")).IsValid);
        Assert.False(v.Validate(new SupplementProductListRequest(Brand: new string('b', 101))).IsValid);
    }

    [Theory]
    [InlineData(null, SupplementProductSort.Name)]
    [InlineData("price_asc", SupplementProductSort.PriceAscending)]
    [InlineData(" PRICE_DESC ", SupplementProductSort.PriceDescending)]
    [InlineData("newest", SupplementProductSort.Newest)]
    public async Task ListPassesFiltersAndSortToTheRepository(string? sort, SupplementProductSort expected)
    {
        var repo = new Repo();
        var service = ProductService(repo);

        await service.GetStoreProductsAsync(new SupplementProductListRequest(Brand: "  Myprotein ", MinPrice: 1, MaxPrice: 2, Sort: sort));
        Assert.Equal(("Myprotein", 1m, 2m, expected, true),
            (repo.LastQuery!.Brand, repo.LastQuery.MinPrice, repo.LastQuery.MaxPrice, repo.LastQuery.Sort, repo.LastQuery.AvailableFirst));

        await service.GetAllProductsAsync(new SupplementProductListRequest());
        Assert.False(repo.LastQuery!.AvailableFirst);   // the manager sees statuses mixed, as stored
    }

    [Fact]
    public async Task StoreBrandsSkipInactiveProducts()
    {
        var repo = new Repo();
        var service = ProductService(repo);
        await service.CreateAsync(Product("A") with { Brand = "Rule 1" }, Manager, null);
        await service.CreateAsync(Product("B", "OutOfStock") with { Brand = "Dymatize" }, Manager, null);
        await service.CreateAsync(Product("C", "Inactive") with { Brand = "BSN" }, Manager, null);
        await service.CreateAsync(Product("D"), Manager, null);

        Assert.Equal(["Dymatize", "Rule 1"], (await service.GetBrandsAsync(storeView: true)).Data!);
        Assert.Equal(["BSN", "Dymatize", "Rule 1"], (await service.GetBrandsAsync(storeView: false)).Data!);
    }

    [Fact]
    public async Task ProductNameMustBeUnique()
    {
        var repo = new Repo();
        var service = ProductService(repo);
        Assert.Equal(201, (await service.CreateAsync(Product(), Manager, null)).StatusCode);
        var duplicate = await service.CreateAsync(Product("  whey   GOLD "), Manager, null);
        Assert.Equal("SUPPLEMENT_PRODUCT_NAME_EXISTS", duplicate.ErrorCode);
        Assert.Single(repo.Audits);
    }

    [Fact]
    public async Task StoreHidesInactiveButShowsOutOfStock()
    {
        var repo = new Repo();
        var service = ProductService(repo);
        var active = (await service.CreateAsync(Product("A"), Manager, null)).Data!;
        var oos = (await service.CreateAsync(Product("B", "OutOfStock"), Manager, null)).Data!;
        var hidden = (await service.CreateAsync(Product("C", "Inactive"), Manager, null)).Data!;

        var store = await service.GetStoreProductsAsync(new SupplementProductListRequest());
        Assert.Equal([active.Id, oos.Id], store.Data!.Items.Select(p => p.Id).Order());
        Assert.False(store.Data.Items.Single(p => p.Id == oos.Id).CanOrder);
        Assert.Equal(404, (await service.GetProductAsync(hidden.Id, storeView: true)).StatusCode);
        Assert.True((await service.GetProductAsync(hidden.Id, storeView: false)).IsSuccess);
        Assert.Empty((await service.GetStoreProductsAsync(new SupplementProductListRequest(Status: "Inactive"))).Data!.Items);
        Assert.Equal(3, (await service.GetAllProductsAsync(new SupplementProductListRequest())).Data!.TotalCount);
    }

    [Fact]
    public async Task ChangeStatusRejectsSameStatus()
    {
        var repo = new Repo();
        var service = ProductService(repo);
        var product = (await service.CreateAsync(Product(), Manager, null)).Data!;
        Assert.Equal("SUPPLEMENT_PRODUCT_STATUS_UNCHANGED", (await service.ChangeStatusAsync(product.Id, "Active", Manager, null)).ErrorCode);
        Assert.Equal("Inactive", (await service.ChangeStatusAsync(product.Id, "inactive", Manager, null)).Data!.Status);
    }

    // ── Placing orders (UC-78) ───────────────────────────────────

    [Fact]
    public async Task PickupOrderIsPendingWithPriceSnapshotAndNotifiesStaff()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("Whey", 1_000_000m);
        var b = repo.SeedProduct("BCAA", 250_000m);

        var result = await OrderService(repo).PlaceOrderAsync(Pickup(L(a, 2), L(b, 1), L(a, 1)), Customer, null);

        Assert.Equal(201, result.StatusCode);
        var order = result.Data!;
        Assert.Equal("Pending", order.Status);
        Assert.Equal("GymPickup", order.FulfillmentType);
        Assert.Matches(@"^SO-\d{8}-\d{6}$", order.OrderNumber);
        Assert.Equal(2, order.Items.Count);                          // duplicate lines merged
        Assert.Equal(3, order.Items.Single(i => i.ProductId == a).Quantity);
        Assert.Equal(3_250_000m, order.TotalAmount);
        Assert.Null(order.Delivery);
        Assert.True(order.CanCancel);
        Assert.Equal(Staff, Assert.Single(repo.Notifications).RecipientUserId);

        // Later price changes do not touch the order.
        repo.Products.Single(p => p.Id == a).UnitPrice = 9m;
        Assert.Equal(3_250_000m, (await OrderService(repo).GetAsync(order.Id, Customer)).Data!.TotalAmount);
    }

    [Fact]
    public async Task DeliveryOrderKeepsRecipientAndContactPhone()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("Whey", 100m);
        var order = (await OrderService(repo).PlaceOrderAsync(Delivery(L(a, 1)), Customer, null)).Data!;

        Assert.Equal("HomeDelivery", order.FulfillmentType);
        Assert.Equal("Nguyen Van A", order.Delivery!.RecipientName);
        Assert.Equal("1 Le Loi, Hanoi", order.Delivery.DeliveryAddress);
        Assert.Equal("0900000001", order.Delivery.ContactPhone);
        Assert.Null(order.Delivery.CourierName);
    }

    [Fact]
    public async Task OnlyActiveProductsCanBeOrdered()
    {
        var repo = new Repo();
        var active = repo.SeedProduct("A", 100m);
        var oos = repo.SeedProduct("B", 100m, SupplementProductStatus.OutOfStock);
        var hidden = repo.SeedProduct("C", 100m, SupplementProductStatus.Inactive);
        var service = OrderService(repo);

        var result = await service.PlaceOrderAsync(Pickup(L(active, 1), L(oos, 1), L(hidden, 1), L(999, 1)), Customer, null);

        Assert.Equal("SUPPLEMENT_PRODUCT_UNAVAILABLE", result.ErrorCode);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains($"{oos}, {hidden}, 999", result.ErrorMessage);
        Assert.Empty(repo.Orders);
    }

    // ── Customer views and cancel (UC-79..81) ────────────────────

    [Fact]
    public async Task CustomerSeesOnlyOwnOrders()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var order = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;

        Assert.Equal(404, (await service.GetAsync(order.Id, OtherCustomer)).StatusCode);
        Assert.Equal(404, (await service.CancelAsync(order.Id, OtherCustomer, null)).StatusCode);
        Assert.True((await service.GetAsync(order.Id, null)).IsSuccess);
        Assert.Empty((await service.GetHistoryAsync(new SupplementOrderListRequest(), OtherCustomer)).Data!.Items);
    }

    [Fact]
    public async Task CancelOnlyWhilePending()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var first = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;
        var second = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;

        var cancelled = await service.CancelAsync(first.Id, Customer, null);
        Assert.Equal("Cancelled", cancelled.Data!.Status);
        Assert.NotNull(cancelled.Data.CancelledAt);
        Assert.False(cancelled.Data.CanCancel);

        await service.ProcessAsync(second.Id, Staff, null);
        Assert.Equal("SUPPLEMENT_ORDER_INVALID_STATE", (await service.CancelAsync(second.Id, Customer, null)).ErrorCode);
    }

    [Fact]
    public async Task OngoingExcludesCompletedAndCancelled()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var pending = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;
        var cancelled = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;
        await service.CancelAsync(cancelled.Id, Customer, null);

        var ongoing = await service.GetOngoingAsync(Customer, 1, 20);
        Assert.Equal([pending.Id], ongoing.Data!.Items.Select(o => o.Id));
        Assert.Equal(2, (await service.GetHistoryAsync(new SupplementOrderListRequest(), Customer)).Data!.TotalCount);
    }

    // ── Staff / Manager flow (UC-82, GBR-57/59) ──────────────────

    [Fact]
    public async Task PickupFlowCompletesWithPaymentNotification()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var order = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;

        Assert.Equal("SUPPLEMENT_ORDER_INVALID_STATE", (await service.CompleteAsync(order.Id, Staff, null)).ErrorCode);
        Assert.Equal("Processing", (await service.ProcessAsync(order.Id, Staff, null)).Data!.Status);
        Assert.Equal("SUPPLEMENT_ORDER_WRONG_FULFILLMENT",
            (await service.OutForDeliveryAsync(order.Id, new SupplementOutForDeliveryRequest("GHN", "T1"), Staff, null)).ErrorCode);
        Assert.Equal("ReadyForPickup", (await service.ReadyForPickupAsync(order.Id, Staff, null)).Data!.Status);
        var done = await service.CompleteAsync(order.Id, Manager, null);

        Assert.Equal("Completed", done.Data!.Status);
        Assert.NotNull(done.Data.CompletedAt);
        Assert.Contains(repo.Notifications, n => n.RecipientUserId == Customer && n.NotificationType == "PaymentConfirmed");
        Assert.Equal(3, repo.Notifications.Count(n => n.RecipientUserId == Customer));
    }

    [Fact]
    public async Task DeliveryFlowRecordsCourierAndDeliveredTime()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var order = (await service.PlaceOrderAsync(Delivery(L(a, 1)), Customer, null)).Data!;
        await service.ProcessAsync(order.Id, Staff, null);

        Assert.Equal("SUPPLEMENT_ORDER_WRONG_FULFILLMENT", (await service.ReadyForPickupAsync(order.Id, Staff, null)).ErrorCode);
        Assert.Equal("SUPPLEMENT_ORDER_DELIVERY_INFO_REQUIRED",
            (await service.OutForDeliveryAsync(order.Id, new SupplementOutForDeliveryRequest(" ", "T1"), Staff, null)).ErrorCode);
        var shipped = await service.OutForDeliveryAsync(order.Id, new SupplementOutForDeliveryRequest("GHN", "GHN123"), Staff, null);
        Assert.Equal("OutForDelivery", shipped.Data!.Status);
        Assert.Equal("GHN123", shipped.Data.Delivery!.TrackingCode);
        Assert.Null(shipped.Data.Delivery.DeliveredAt);

        var done = await service.CompleteAsync(order.Id, Staff, null);
        Assert.Equal("Completed", done.Data!.Status);
        Assert.NotNull(done.Data.Delivery!.DeliveredAt);
    }

    [Fact]
    public async Task CancelledOrderAllowsNoStaffAction()
    {
        var repo = new Repo();
        var a = repo.SeedProduct("A", 100m);
        var service = OrderService(repo);
        var order = (await service.PlaceOrderAsync(Pickup(L(a, 1)), Customer, null)).Data!;
        await service.CancelAsync(order.Id, Customer, null);

        Assert.Equal("SUPPLEMENT_ORDER_CANCELLED", (await service.ProcessAsync(order.Id, Staff, null)).ErrorCode);
        Assert.Equal("SUPPLEMENT_ORDER_CANCELLED", (await service.CompleteAsync(order.Id, Staff, null)).ErrorCode);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static IMapper Mapper()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(SupplementMapping).Assembly);
        return new ServiceMapper(new NoServices(), config);
    }

    private static SupplementProductService ProductService(Repo repo) => new(repo, Mapper());
    private static SupplementOrderService OrderService(Repo repo) => new(repo, Mapper());

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class Repo : ISupplementRepository
    {
        private int _nextId = 1;
        public List<SupplementProduct> Products { get; } = [];
        public List<SupplementOrder> Orders { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        private static readonly Dictionary<string, ApplicationUser> Users = new()
        {
            [Customer] = new ApplicationUser { UserId = Customer, FullName = "Customer One", PhoneNumber = "0900000001" },
            [OtherCustomer] = new ApplicationUser { UserId = OtherCustomer, FullName = "Customer Two", PhoneNumber = "0900000002" }
        };

        public int SeedProduct(string name, decimal price, SupplementProductStatus status = SupplementProductStatus.Active)
        {
            var product = new SupplementProduct { Id = _nextId++, ProductName = name, UnitPrice = price, ProductStatus = status };
            Products.Add(product);
            return product.Id;
        }

        public void Add<TEntity>(TEntity entity) where TEntity : BaseEntity
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            switch (entity)
            {
                case SupplementProduct p: Products.Add(p); break;
                case SupplementOrder o:
                    o.CreatedAt = DateTime.UtcNow;
                    o.Customer = Users[o.CustomerUserId];
                    foreach (var item in o.Items)
                    {
                        if (item.Id == 0) item.Id = _nextId++;
                        item.SupplementProduct = Products.Single(p => p.Id == item.SupplementProductId);
                    }
                    Orders.Add(o);
                    break;
                case Notification n: Notifications.Add(n); break;
                case AuditLog a: Audits.Add(a); break;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) => work();

        public Task<IReadOnlyList<string>> GetActiveUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(roleName == "Staff" ? [Staff] : roleName == "Manager" ? [Manager] : []);

        public SupplementProductQuery? LastQuery { get; private set; }

        public Task<(IReadOnlyList<SupplementProduct> Items, int TotalCount)> SearchProductsAsync(
            SupplementProductQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var items = Products.Where(p => query.Statuses.Contains(p.ProductStatus)).ToList();
            return Task.FromResult<(IReadOnlyList<SupplementProduct>, int)>((items, items.Count));
        }

        public Task<SupplementProduct?> GetProductAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

        public Task<bool> ProductNameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.Any(p => string.Equals(p.ProductName, name, StringComparison.OrdinalIgnoreCase) && p.Id != excludeId));

        public Task<IReadOnlyList<string>> GetCategoriesAsync(
            IReadOnlyCollection<SupplementProductStatus> statuses, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Products.Where(p => statuses.Contains(p.ProductStatus) && p.CatalogCategory != null)
                .Select(p => p.CatalogCategory!).Distinct().ToList());

        public Task<IReadOnlyList<string>> GetBrandsAsync(
            IReadOnlyCollection<SupplementProductStatus> statuses, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Products.Where(p => statuses.Contains(p.ProductStatus) && p.Brand != null)
                .Select(p => p.Brand!).Distinct().Order().ToList());

        public Task<IReadOnlyList<SupplementProduct>> GetProductsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupplementProduct>>(Products.Where(p => ids.Contains(p.Id)).ToList());

        public Task<(IReadOnlyList<SupplementOrder> Items, int TotalCount)> SearchOrdersAsync(
            string? customerUserId, IReadOnlyCollection<SupplementOrderStatus>? statuses, SupplementDeliveryType? deliveryType,
            DateTime? fromUtc, DateTime? toUtcExclusive, string? search,
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var items = Orders
                .Where(o => customerUserId is null || o.CustomerUserId == customerUserId)
                .Where(o => statuses is null || statuses.Contains(o.OrderStatus))
                .Where(o => deliveryType is null || o.DeliveryType == deliveryType)
                .ToList();
            return Task.FromResult<(IReadOnlyList<SupplementOrder>, int)>((items, items.Count));
        }

        public Task<SupplementOrder?> GetOrderAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));

        public Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.Any(o => o.OrderNumber == orderNumber));
    }
}
