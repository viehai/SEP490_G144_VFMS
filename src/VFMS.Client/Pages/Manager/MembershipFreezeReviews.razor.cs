using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VFMS.Client.Components.Auth;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;

namespace VFMS.Client.Pages.Manager;

public partial class MembershipFreezeReviews : IDisposable
{
    [Inject] private MembershipPlanApiClient Api { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly CancellationTokenSource _lifetime = new();
    private const int PageSize = 20;
    private readonly RejectMembershipFreezeRequestValidator _rejectValidator = new();
    private PagedResponse<MembershipFreezeReviewSummaryResponse>? _page;
    private MembershipFreezeReviewDetailResponse? _detail;
    private (int Id, string Type)? _selection;
    private RejectForm _rejectForm = new();
    private ContractValidation<RejectMembershipFreezeRequest>? _rejectValidation;
    private ElementReference _detailHeading;
    private ElementReference _noticeElement;
    private ElementReference _decisionErrorElement;
    private string _typeInput = "";
    private string? _activeType;
    private int _requestedPage = 1;
    private string? _queueError;
    private string? _detailError;
    private string? _decisionError;
    private string? _notice;
    private bool _loading;
    private bool _loadingDetail;
    private bool _working;
    private bool _focusDetail;
    private bool _focusNotice;
    private bool _focusDecisionError;
    private bool Busy => _loading || _loadingDetail || _working;

    protected override Task OnInitializedAsync() => LoadQueueCoreAsync(1);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusDecisionError)
        {
            _focusDecisionError = false;
            await _decisionErrorElement.FocusAsync();
        }
        else if (_focusNotice)
        {
            _focusNotice = false;
            await _noticeElement.FocusAsync();
        }
        else if (_focusDetail)
        {
            _focusDetail = false;
            await _detailHeading.FocusAsync();
        }
    }

    private async Task LoadQueueCoreAsync(int page)
    {
        _loading = true;
        _queueError = null;
        _requestedPage = page;
        var ct = _lifetime.Token;
        try
        {
            var result = await Api.ListFreezeReviewsAsync(new() { Type = _activeType, Page = page, PageSize = PageSize }, ct);
            var lastPage = Math.Max(1, (int)Math.Ceiling((double)result.TotalCount / Math.Max(1, result.PageSize)));
            if (result.Page > lastPage)
            {
                _requestedPage = lastPage;
                result = await Api.ListFreezeReviewsAsync(new() { Type = _activeType, Page = lastPage, PageSize = PageSize }, ct);
            }
            if (!ct.IsCancellationRequested)
                _page = result;
        }
        catch (AuthApiException error)
        {
            if (!ct.IsCancellationRequested)
                _queueError = error.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _loading = false;
        }
    }

    private Task ApplyFilterAsync()
    {
        if (Busy)
            return Task.CompletedTask;
        CloseDetail();
        _notice = null;
        _activeType = string.IsNullOrEmpty(_typeInput) ? null : _typeInput;
        return LoadQueueCoreAsync(1);
    }

    private Task RetryQueueAsync() => Busy ? Task.CompletedTask : LoadQueueCoreAsync(_requestedPage);

    private Task LoadPageAsync(int page) => Busy ? Task.CompletedTask : LoadQueueCoreAsync(page);

    private Task SelectAsync(MembershipFreezeReviewSummaryResponse request)
    {
        if (Busy)
            return Task.CompletedTask;

        _notice = _decisionError = null;
        return LoadDetailCoreAsync(request.FreezeRequestId, request.RequestType, focus: true);
    }

    private async Task LoadDetailCoreAsync(int id, string type, bool focus)
    {
        _selection = (id, type);
        _loadingDetail = true;
        _detailError = null;
        _detail = null;
        var ct = _lifetime.Token;
        try
        {
            var detail = await Api.GetFreezeReviewAsync(id, type, ct);
            if (!ct.IsCancellationRequested)
            {
                _detail = detail;
                _rejectForm = new();
                _rejectValidation = null;
                _focusDetail = focus;
            }
        }
        catch (AuthApiException error)
        {
            if (!ct.IsCancellationRequested)
                _detailError = error.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _loadingDetail = false;
        }
    }

    private Task RefreshDetailAsync() => Busy || _selection is not { } selection
        ? Task.CompletedTask : LoadDetailCoreAsync(selection.Id, selection.Type, focus: true);

    private Task ApproveAsync() => DecideAsync(approve: true);

    private Task RejectAsync() => DecideAsync(approve: false);

    private async Task DecideAsync(bool approve)
    {
        if (Busy || _detail is not { } detail || (approve ? !detail.CanApprove : !detail.CanReject))
            return;

        _working = true;
        _notice = _decisionError = null;
        var ct = _lifetime.Token;
        try
        {
            if (approve && !await JS.InvokeAsync<bool>("confirm", ct,
                    $"Duyệt {TypeLabel(detail.Request.RequestType).ToLowerInvariant()} #{detail.Request.FreezeRequestId} của {detail.Request.MemberName}?"))
                return;

            var result = approve
                ? await Api.ApproveFreezeReviewAsync(detail.Request.FreezeRequestId, detail.Request.RequestType, ct)
                : await Api.RejectFreezeReviewAsync(detail.Request.FreezeRequestId, detail.Request.RequestType, CreateRejectRequest(), ct);
            if (ct.IsCancellationRequested)
                return;

            _detail = result;
            _rejectForm = new();
            _rejectValidation = null;
            _notice = approve ? "Đã duyệt yêu cầu thành công." : "Đã từ chối yêu cầu và ghi nhận lý do.";
            _focusNotice = true;
            await LoadQueueCoreAsync(_requestedPage);
        }
        catch (AuthApiException error)
        {
            if (ct.IsCancellationRequested)
                return;

            _decisionError = error.Message;
            _rejectValidation?.ShowErrors(error.Errors);
            _focusDecisionError = true;
            if (error.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound ||
                (approve && error.StatusCode == HttpStatusCode.BadRequest && error.Errors.Count == 0))
            {
                await LoadDetailCoreAsync(detail.Request.FreezeRequestId, detail.Request.RequestType, focus: false);
                await LoadQueueCoreAsync(_requestedPage);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _working = false;
        }
    }

    private RejectMembershipFreezeRequest CreateRejectRequest() => new(_rejectForm.Reason.Trim());

    private void InvalidReject()
    {
        _decisionError = "Vui lòng nhập lý do từ chối hợp lệ.";
        _focusDecisionError = true;
    }

    private void CloseDetail()
    {
        _detail = null;
        _selection = null;
        _detailError = _decisionError = null;
        _rejectValidation = null;
    }

    private static string TypeLabel(string type) => type == MembershipFreezeReviewTypes.EarlyUnfreeze
        ? "Kết thúc bảo lưu sớm" : "Bảo lưu";

    private static string PeriodLabel(string type) => type == MembershipFreezeReviewTypes.EarlyUnfreeze
        ? "Khoảng bảo lưu đã duyệt" : "Thời gian bảo lưu đề nghị";

    private static string UnitLabel(string unit) => unit == MembershipFreezeTierUnits.Months ? "tháng" : "ngày";

    private static DateOnly VietnamDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(timestamp.ToOffset(TimeSpan.FromHours(7)).DateTime);

    private static string IsoTimestamp(DateTimeOffset timestamp) => timestamp.ToString("O", CultureInfo.InvariantCulture);

    private static string DisplayTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " (UTC+7)";

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class RejectForm
    {
        public string Reason { get; set; } = "";
    }
}
