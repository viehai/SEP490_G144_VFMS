using System.Diagnostics;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Services;

/// <summary>State for one checkout page visit. Serializes refresh and voucher requests so older responses cannot replace a new total.</summary>
public sealed class MembershipCheckoutState(MembershipPlanApiClient api, string orderNumber) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private long _receivedAt;
    private bool _disposed;

    public MembershipPurchaseResponse? Purchase { get; private set; }
    public string? Error { get; private set; }
    public string? VoucherError { get; private set; }
    public string? VoucherNotice { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsApplying { get; private set; }
    public bool IsRemoving { get; private set; }
    public bool IsCompletingNoCharge { get; private set; }
    public bool IsBusy => IsRefreshing || IsApplying || IsRemoving || IsCompletingNoCharge;
    public bool CanManageVoucher => Purchase is { OrderStatus: MembershipPurchaseStatuses.Pending, Items.Count: > 0 } && Remaining > TimeSpan.Zero;
    public CancellationToken CancellationToken => _lifetime.Token;
    public TimeSpan Remaining
    {
        get
        {
            if (Purchase is not { } purchase) return TimeSpan.Zero;
            var remaining = purchase.ExpiresAt - purchase.ServerTime - Stopwatch.GetElapsedTime(_receivedAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || !await _operation.WaitAsync(0)) return;
        IsRefreshing = true;
        var ct = _lifetime.Token;
        try
        {
            var response = await api.GetPurchaseAsync(orderNumber, ct);
            if (!ct.IsCancellationRequested) Accept(response);
        }
        catch (AuthApiException error) when (!ct.IsCancellationRequested) { Error = error.Message; }
        catch (Exception error) when (ct.IsCancellationRequested && error is OperationCanceledException or AuthApiException) { }
        finally { IsRefreshing = false; _operation.Release(); }
    }

    public Task ApplyVoucherAsync(string code) => MutateVoucherAsync(code, remove: false);
    public Task RemoveVoucherAsync() => MutateVoucherAsync(null, remove: true);

    public async Task CompleteNoChargeAsync()
    {
        if (_disposed || !await _operation.WaitAsync(0)) return;
        var ct = _lifetime.Token;
        try
        {
            if (!CanManageVoucher || Purchase!.AmountDue != 0) return;
            IsCompletingNoCharge = true;
            Error = null;
            var response = await api.CompleteNoChargeMembershipPurchaseAsync(orderNumber, ct);
            if (!ct.IsCancellationRequested)
            {
                Accept(response);
                VoucherError = VoucherNotice = null;
            }
        }
        catch (AuthApiException error) when (!ct.IsCancellationRequested) { Error = error.Message; }
        catch (Exception error) when (ct.IsCancellationRequested && error is OperationCanceledException or AuthApiException) { }
        finally { IsCompletingNoCharge = false; _operation.Release(); }
    }

    private async Task MutateVoucherAsync(string? code, bool remove)
    {
        if (_disposed || !await _operation.WaitAsync(0)) return;
        var ct = _lifetime.Token;
        try
        {
            VoucherError = VoucherNotice = null;
            if (!CanManageVoucher)
            {
                VoucherError = "Chỉ có thể thay đổi mã giảm giá khi đơn hàng đang chờ thanh toán và còn hiệu lực.";
                return;
            }
            if (!remove && string.IsNullOrWhiteSpace(code))
            {
                VoucherError = "Vui lòng nhập mã giảm giá.";
                return;
            }
            IsApplying = !remove;
            IsRemoving = remove;
            var response = remove ? await api.RemoveMembershipVoucherAsync(orderNumber, ct)
                : await api.ApplyMembershipVoucherAsync(orderNumber, new(code!.Trim()), ct);
            if (ct.IsCancellationRequested) return;
            Accept(response);
            VoucherNotice = remove ? "Đã bỏ mã giảm giá. Tổng thanh toán đã được cập nhật."
                : "Áp dụng mã giảm giá thành công. Tổng thanh toán đã được cập nhật.";
        }
        catch (AuthApiException error) when (!ct.IsCancellationRequested)
        {
            VoucherError = error.Errors.Count > 0
                ? string.Join(" ", error.Errors.Values.SelectMany(messages => messages).Distinct()) : error.Message;
        }
        catch (Exception error) when (ct.IsCancellationRequested && error is OperationCanceledException or AuthApiException) { }
        finally { IsApplying = IsRemoving = false; _operation.Release(); }
    }

    private void Accept(MembershipPurchaseResponse response)
    {
        Purchase = response;
        _receivedAt = Stopwatch.GetTimestamp();
        Error = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
