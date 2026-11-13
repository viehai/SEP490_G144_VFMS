using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;

namespace VFMS.Client.Pages.Member;

public partial class MyFreezeRequests : IDisposable
{
    private const string SecondaryButton = "min-h-[44px] rounded-lg border border-vfms-border bg-white px-4 py-2 text-sm font-semibold text-vfms-text hover:bg-vfms-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-vfms-primary disabled:opacity-50";
    private const string TextLink = "inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold text-vfms-primary hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-vfms-primary";

    [Inject] private MembershipPlanApiClient Api { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<MembershipFreezeRequestResponse> _requests = [];
    private MembershipFreezeRequestResponse? _editing;
    private MembershipFreezeEligibilityResponse? _eligibility;
    private ElementReference _editorHeading;
    private ElementReference _noticeElement;
    private bool _loading;
    private bool _working;
    private bool _focusEditor;
    private bool _focusNotice;
    private string? _loadError;
    private string? _actionError;
    private string? _notice;
    private bool ActionsDisabled => _loading || _working || _editing is not null;

    protected override Task OnInitializedAsync() => LoadAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusEditor)
        {
            _focusEditor = false;
            await _editorHeading.FocusAsync();
        }
        else if (_focusNotice)
        {
            _focusNotice = false;
            await _noticeElement.FocusAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (ActionsDisabled)
            return;

        _loading = true;
        _loadError = null;
        var ct = _lifetime.Token;
        try
        {
            var requests = await Api.GetMyFreezeRequestsAsync(ct);
            if (!ct.IsCancellationRequested)
                _requests = requests;
        }
        catch (AuthApiException error)
        {
            if (!ct.IsCancellationRequested)
                _loadError = error.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _loading = false;
        }
    }

    private Task OpenEditAsync(MembershipFreezeRequestResponse request) => RunActionAsync(async ct =>
    {
        var eligibility = await Api.GetFreezeEligibilityAsync(request.MembershipId, request.FreezeRequestId, ct);
        if (!ct.IsCancellationRequested)
        {
            _editing = request;
            _eligibility = eligibility;
            _focusEditor = true;
        }
    });

    private Task CancelAsync(MembershipFreezeRequestResponse request) => RunActionAsync(async ct =>
    {
        if (!await JS.InvokeAsync<bool>("confirm", ct, $"Hủy yêu cầu bảo lưu #{request.FreezeRequestId} cho gói “{request.PlanName}”?"))
            return;

        var cancelled = await Api.CancelFreezeRequestAsync(request.FreezeRequestId, ct);
        if (!ct.IsCancellationRequested)
            UpdateRequest(cancelled, "Đã hủy yêu cầu bảo lưu.");
    });

    private Task RequestEarlyUnfreezeAsync(MembershipFreezeRequestResponse request) => RunActionAsync(async ct =>
    {
        if (!await JS.InvokeAsync<bool>("confirm", ct, $"Gửi yêu cầu kết thúc bảo lưu sớm cho gói “{request.PlanName}”? Yêu cầu sẽ chờ quản lý xét duyệt."))
            return;

        var updated = await Api.RequestEarlyUnfreezeAsync(request.FreezeRequestId, ct);
        if (!ct.IsCancellationRequested)
            UpdateRequest(updated, "Đã gửi yêu cầu kết thúc bảo lưu sớm. Yêu cầu đang chờ duyệt.");
    });

    private async Task RunActionAsync(Func<CancellationToken, Task> action)
    {
        if (ActionsDisabled)
            return;

        _working = true;
        _notice = _actionError = null;
        var ct = _lifetime.Token;
        try
        {
            await action(ct);
        }
        catch (AuthApiException error)
        {
            if (!ct.IsCancellationRequested)
            {
                _actionError = error.Message;
                _focusNotice = true;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _working = false;
        }
    }

    private void RequestSaved(MembershipFreezeRequestResponse request)
    {
        CloseEditor();
        UpdateRequest(request, "Đã cập nhật yêu cầu bảo lưu. Yêu cầu đang chờ duyệt.");
    }

    private void UpdateRequest(MembershipFreezeRequestResponse updated, string notice)
    {
        _requests = _requests.Select(request =>
            request.FreezeRequestId == updated.FreezeRequestId ? updated : request).ToArray();
        _notice = notice;
        _actionError = null;
        _focusNotice = true;
    }

    private void CloseEditor()
    {
        _editing = null;
        _eligibility = null;
    }

    private static DateOnly VietnamDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(timestamp.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
