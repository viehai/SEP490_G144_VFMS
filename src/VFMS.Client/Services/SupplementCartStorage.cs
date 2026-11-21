using System.Text.Json;
using Microsoft.JSInterop;
using VFMS.Contracts.Supplements;

namespace VFMS.Client.Services;

/// <summary>
/// Keeps a shopper's supplement cart in the browser's localStorage, one entry per signed-in user, so a reload or a
/// visit to another page does not empty it (UC-78). Storage can be missing or blocked (private mode, cleared site
/// data), so every call swallows storage errors: the cart then simply lasts for the visit, as before.
/// </summary>
public sealed class SupplementCartStorage(IJSRuntime js)
{
    /// <summary>A saved cart line: the product, quantity, the price the shopper saw and the name (for messages).</summary>
    public sealed record SavedLine(int ProductId, int Quantity, decimal UnitPrice, string? Name = null);

    /// <summary>Outcome of checking a saved cart against the current catalogue.</summary>
    public sealed record Reconciled(
        IReadOnlyList<(SupplementProductResponse Product, int Quantity)> Lines,
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> PriceChanged,
        IReadOnlyList<string>? Reduced = null);

    private const string KeyPrefix = "vfms.supplement-cart.";
    private const int MaxQuantity = 999;

    public static string KeyFor(string userId) => KeyPrefix + userId;

    /// <summary>
    /// Most of a product one cart line may hold: the stock left when the store shows it (it does only when the
    /// stock is low), otherwise the usual 999.
    /// </summary>
    public static int MaxFor(SupplementProductResponse product) =>
        product.StockQuantity is { } left ? Math.Clamp(left, 0, MaxQuantity) : MaxQuantity;

    public async Task<IReadOnlyList<SavedLine>> LoadAsync(string userId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", KeyFor(userId));
            return Parse(json);
        }
        catch (Exception error) when (error is JSException or InvalidOperationException or TaskCanceledException)
        {
            return [];
        }
    }

    public async Task SaveAsync(string userId, IEnumerable<SavedLine> lines)
    {
        var list = lines.ToList();
        try
        {
            if (list.Count == 0)
                await js.InvokeVoidAsync("localStorage.removeItem", KeyFor(userId));
            else
                await js.InvokeVoidAsync("localStorage.setItem", KeyFor(userId), JsonSerializer.Serialize(list));
        }
        catch (Exception error) when (error is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Storage unavailable: the cart still works for this visit.
        }
    }

    /// <summary>Reads saved JSON; anything unreadable, duplicated or out of range is dropped rather than trusted.</summary>
    public static IReadOnlyList<SavedLine> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<SavedLine>>(json) ?? [])
                .Where(line => line is { ProductId: > 0, Quantity: > 0 })
                .GroupBy(line => line.ProductId)
                .Select(group => group.First() with { Quantity = Math.Min(MaxQuantity, group.First().Quantity) })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Matches saved lines with the products as they are now. A product that is gone (null) or can no longer be ordered
    /// is removed; a product whose price changed is kept at the new price and reported, so the shopper is not surprised
    /// at checkout.
    /// </summary>
    public static Reconciled Reconcile(IReadOnlyList<SavedLine> saved, IReadOnlyDictionary<int, SupplementProductResponse?> current)
    {
        var lines = new List<(SupplementProductResponse, int)>();
        var removed = new List<string>();
        var changed = new List<string>();
        var reduced = new List<string>();
        foreach (var line in saved)
        {
            current.TryGetValue(line.ProductId, out var product);
            if (product is null)
            {
                removed.Add(line.Name ?? $"Sản phẩm #{line.ProductId}");
                continue;
            }
            if (!product.CanOrder)
            {
                removed.Add(product.Name);
                continue;
            }
            if (product.UnitPrice != line.UnitPrice) changed.Add(product.Name);
            var quantity = Math.Min(line.Quantity, MaxFor(product));
            if (quantity < line.Quantity) reduced.Add($"{product.Name} (còn {quantity})");
            lines.Add((product, quantity));
        }
        return new(lines, removed, changed, reduced);
    }
}
