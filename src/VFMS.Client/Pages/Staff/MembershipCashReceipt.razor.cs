using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VFMS.Client.Components.Memberships;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Pages.Staff;

public partial class MembershipCashReceipt : IDisposable
{
    [Inject] private MembershipPlanApiClient Plans { get; set; } = default!;
    [Inject] private PersonalTrainingApiClient Training { get; set; } = default!;

    private static readonly string?[] OrderStatuses = [null, "Pending", "Paid", "Expired", "Cancelled"];
    private static readonly string[] PaymentMethods = ["Cash", "VietQR", "BankTransfer", "NoCharge"];
    private static readonly string[] PaymentStatuses = ["Pending", "Paid", "Failed", "Cancelled", "Refunded"];

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _listRequest;
    private StaffPackageOrderQuery _draft = new();
    private StaffPackageOrderQuery _active = new();
    private PagedResponse<StaffPackageOrderResponse>? _orders;
    private StaffPackageOrderResponse? _selected;
    private string _amount = "";
    private bool _loading;
    private bool _confirming;
    private string? _loadError;
    private string? _filterError;
    private string? _actionError;
    private string? _notice;
    private ElementReference _detailHeading;
    private ElementReference _listHeading;
    private ElementReference _noticeElement;
    private FocusTarget _focus;
    private bool Busy => _loading || _confirming;

    private enum FocusTarget { None, Details, List, Notice }

    [SupplyParameterFromQuery(Name = "order")] public string? OrderNumber { get; set; }
    private string? _previousNumber;
    private bool _initialized;

    protected override async Task OnParametersSetAsync()
    {
        if (_initialized && _previousNumber == OrderNumber) return;
        _initialized = true;
        _previousNumber = OrderNumber;
        _selected = null;
        var number = OrderNumber;
        _draft = new() { Search = number };
        _orders = null;
        await LoadAsync(_draft with { Page = 1 });
        if (number is not null && number == OrderNumber && _orders?.Items.FirstOrDefault(o => o.OrderNumber == number) is { } match)
            OpenDetails(match);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var target = _focus switch
        {
            FocusTarget.Details => _detailHeading,
            FocusTarget.List => _listHeading,
            FocusTarget.Notice => _noticeElement,
            _ => default
        };
        _focus = FocusTarget.None;
        if (target.Id is not null) await target.FocusAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        if (Busy) return;
        _filterError = _draft.From > _draft.To ? "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc." : null;
        if (_filterError is not null) return;

        _selected = null;
        await LoadAsync(_draft with { Page = 1 });
    }

    private Task SelectStatusAsync(string? status)
    {
        _draft.Status = status;
        return ApplyFiltersAsync();
    }

    private Task ResetFiltersAsync()
    {
        _draft = new();
        return ApplyFiltersAsync();
    }

    private Task RefreshAsync() => LoadAsync(_active);
    private Task LoadPageAsync(int page) => LoadAsync(_active with { Page = page });

    private async Task LoadAsync(StaffPackageOrderQuery query)
    {
        if (_lifetime.IsCancellationRequested) return;
        _listRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _listRequest = request;
        _loading = true;
        _loadError = null;
        _active = query;
        var ct = request.Token;
        try
        {
            var page = await Plans.ListStaffOrdersAsync(query, ct);
            ct.ThrowIfCancellationRequested();
            var lastPage = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)query.PageSize));
            if (query.Page > lastPage)
            {
                _active = query with { Page = lastPage };
                page = await Plans.ListStaffOrdersAsync(_active, ct);
                ct.ThrowIfCancellationRequested();
            }
            _orders = page;
            if (_selected is { } selected)
                _selected = page.Items.FirstOrDefault(row => row.OrderId == selected.OrderId);
        }
        catch (AuthApiException error) when (!ct.IsCancellationRequested)
        {
            _loadError = error.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (_listRequest == request) { _listRequest = null; _loading = false; }
        }
    }

    private void OpenDetails(StaffPackageOrderResponse row)
    {
        _selected = row;
        _amount = row is { CanConfirmCash: true, AmountDue: { } due } ? Vnd.FormatAmount(due) : "";
        _actionError = null;
        _notice = null;
        _focus = FocusTarget.Details;
    }

    private void CloseDetails()
    {
        _selected = null;
        _actionError = null;
        _focus = FocusTarget.List;
    }

    private async Task ConfirmCashAsync()
    {
        if (Busy || _selected is not { CanConfirmCash: true, AmountDue: { } due } order ||
            CashTendered is not { } tendered || tendered < due) return;

        _confirming = true;
        _actionError = _notice = null;
        var ct = _lifetime.Token;
        try
        {
            var invoice = order.IsPersonalTraining
                ? (await Training.ConfirmCashAsync(order.OrderNumber, new(due), ct)).InvoiceNumber
                : (await Plans.ConfirmCashAsync(order.OrderNumber, new(due), ct)).InvoiceNumber;
            if (ct.IsCancellationRequested) return;

            _selected = null;
            _notice = $"Đã thu {OperationsText.Money(due)} cho đơn {order.OrderNumber}.";
            if (invoice is not null) _notice += $" Hóa đơn {invoice}.";
            _focus = FocusTarget.Notice;
            await RefreshAsync();
        }
        catch (AuthApiException error) when (!ct.IsCancellationRequested)
        {
            _actionError = error.Message;
            if (error.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                await RefreshAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _confirming = false; }
    }

    private decimal? CashTendered => Vnd.ParseInput(_amount, wholeUnits: true) is >= 0 and var cash ? cash : null;

    private void SetCashTendered(string? input) => _amount = Vnd.FormatInput(_amount, input, wholeUnits: true);

    private static bool HasFilters(StaffPackageOrderQuery query) =>
        !string.IsNullOrWhiteSpace(query.Search) || !string.IsNullOrEmpty(query.Status) ||
        !string.IsNullOrEmpty(query.ServiceType) || !string.IsNullOrEmpty(query.Method) ||
        !string.IsNullOrEmpty(query.PaymentStatus) || query.From.HasValue || query.To.HasValue;

    private static string MethodLabel(string? method) => method is null ? "Chưa chọn phương thức" : TransactionDisplay.Method(method);
    private static string PaymentLabel(string? status) => status is null ? "Chưa tạo giao dịch" : TransactionDisplay.Status(status);
    private static string AmountLabel(decimal? amount) => amount is { } value ? OperationsText.Money(value) : "—";
    private static string ServiceLabel(string service) => service switch { "GYM" => "Gym", "YOGA" => "Yoga", _ => service };

    private static string? RefundLabel(string? status) => status switch
    {
        "Pending" => "Chờ hoàn tiền",
        "Refunded" => "Đã hoàn tiền",
        "Failed" => "Hoàn tiền chưa thành công",
        _ => null
    };

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
