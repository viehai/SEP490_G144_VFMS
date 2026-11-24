using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Pages;

public partial class MembershipCheckout : IDisposable
{
    [Parameter] public string OrderNumber { get; set; } = "";
    [SupplyParameterFromQuery(Name = "renewal")] public bool IsRenewal { get; set; }
    private MembershipCheckoutState? _checkout;
    private PendingYogaClass? _pendingClass;
    private TimeSpan Remaining => _checkout?.Remaining ?? TimeSpan.Zero;

    protected override async Task OnParametersSetAsync()
    {
        Dispose();
        // Optional: the checkout page tests render without a JS runtime, and then there is just no class link.
        _pendingClass = Services.GetService(typeof(IJSRuntime)) is IJSRuntime js ? await PendingYogaClass.LoadAsync(js) : null;
        var checkout = _checkout = new(Plans, OrderNumber);
        var ct = checkout.CancellationToken;
        await checkout.RefreshAsync();
        if (!ct.IsCancellationRequested)
            _ = PollAsync(checkout, ct);
    }

    private Task RefreshAsync() => _checkout?.RefreshAsync() ?? Task.CompletedTask;
    private Task ApplyVoucherAsync(string code) => _checkout?.ApplyVoucherAsync(code) ?? Task.CompletedTask;
    private Task RemoveVoucherAsync() => _checkout?.RemoveVoucherAsync() ?? Task.CompletedTask;
    private Task CompleteNoChargeAsync() => _checkout?.CompleteNoChargeAsync() ?? Task.CompletedTask;
    private static string Money(decimal value) => Vnd.Format(value);

    private async Task PollAsync(MembershipCheckoutState checkout, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var seconds = 0;
        try
        {
            while (ReferenceEquals(_checkout, checkout) &&
                (checkout.Purchase is null || checkout.Purchase.OrderStatus == MembershipPurchaseStatuses.Pending) &&
                await timer.WaitForNextTickAsync(ct))
            {
                if (++seconds % 3 == 0)
                    await checkout.RefreshAsync();
                if (!ct.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _checkout?.Dispose();
        _checkout = null;
    }
}
