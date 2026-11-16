using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using VFMS.Client.Components.Promotions;
using VFMS.Client.Services;
using VFMS.Contracts.Promotions;
using VFMS.Contracts.Promotions.Validators;

namespace VFMS.Client.Pages.Manager;

public partial class Promotions : IDisposable
{
    private const string Label = "block text-sm font-semibold text-vfms-text";
    private const string Field = "mt-1 block min-h-[44px] min-w-0 w-full rounded-lg border border-vfms-border bg-vfms-surface px-3 py-2 text-base text-vfms-text sm:text-sm";
    private const string PrimaryButton = "min-h-[44px] rounded-lg bg-vfms-primary px-5 py-2 text-sm font-semibold text-white hover:bg-vfms-primary-hover disabled:opacity-50";
    private const string SecondaryButton = "min-h-[44px] rounded-lg border border-vfms-border bg-vfms-surface px-3 py-2 text-sm font-semibold text-vfms-text hover:bg-vfms-muted disabled:opacity-50";
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly string[] FormFields = ["Name", "Code", "DiscountType", "DiscountValue", "MinimumSpend", "MaxDiscountAmount", "MaxRedemptions", "StartAt", "EndAt", "IsEnabled"];
    [Inject] private PromotionApiClient Api { get; set; } = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CreatePromotionRequestValidator _createValidator = new();
    private readonly UpdatePromotionRequestValidator _updateValidator = new();
    private IReadOnlyList<PromotionResponse>? _promotions;
    private PromotionFormModel? _form;
    private EditContext? _editContext;
    private ValidationMessageStore? _messages;
    private int? _editingId;
    private int _editingUsedCount;
    private bool _loading;
    private bool _working;
    private bool _submitted;
    private bool _focusForm;
    private bool _focusError;
    private bool _focusSuccess;
    private string? _notice;
    private string? _error;
    private string? _saveError;
    private ElementReference _formTitle;
    private ElementReference _errorNotice;
    private ElementReference _successNotice;
    private bool Busy => _working || _loading;

    protected override Task OnInitializedAsync() => LoadAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusError) { _focusError = false; await _errorNotice.FocusAsync(); }
        else if (_focusForm) { _focusForm = false; await _formTitle.FocusAsync(); }
        else if (_focusSuccess) { _focusSuccess = false; await _successNotice.FocusAsync(); }
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        _error = null;
        try { _promotions = await Api.ListAsync(_lifetime.Token); }
        catch (AuthApiException error) { _error = error.Message; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _loading = false; }
    }

    private void StartCreate()
    {
        if (Busy) return;
        SetForm(new(), null, 0);
    }

    private async Task StartEditAsync(int id)
    {
        if (Busy) return;
        _working = true;
        _error = _notice = null;
        try
        {
            var detail = await Api.GetAsync(id, _lifetime.Token);
            SetForm(PromotionFormModel.FromResponse(detail), id, detail.UsedCount);
        }
        catch (AuthApiException error) { _error = error.Message; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _working = false; }
    }

    private void SetForm(PromotionFormModel form, int? id, int usedCount)
    {
        DetachValidation();
        _form = form;
        _editingId = id;
        _editingUsedCount = usedCount;
        _editContext = new(form);
        _messages = new(_editContext);
        _editContext.OnValidationRequested += ValidateSubmit;
        _editContext.OnFieldChanged += ValidateChange;
        _submitted = false;
        _saveError = _notice = null;
        _focusForm = true;
    }

    private async Task SaveAsync()
    {
        if (Busy || _form is not { } form) return;
        _working = true;
        _saveError = _error = _notice = null;
        try
        {
            _ = _editingId is { } id ? await Api.UpdateAsync(id, form.UpdateRequest(), _lifetime.Token)
                : await Api.CreateAsync(form.CreateRequest(), _lifetime.Token);
            CloseForm();
            _notice = "Đã lưu khuyến mãi thành công. (MSG-04)";
            _focusSuccess = true;
            await LoadAsync();
        }
        catch (AuthApiException error)
        {
            _saveError = error.Message;
            foreach (var field in error.Errors)
            {
                var name = FormFields.FirstOrDefault(value => value.Equals(field.Key.Split('.').Last(), StringComparison.OrdinalIgnoreCase)) ?? field.Key;
                _messages!.Add(new FieldIdentifier(form, name), field.Value);
            }
            if (error.ErrorCode == "PROMOTION_CODE_EXISTS")
                _messages!.Add(new FieldIdentifier(form, nameof(form.Code)), error.Message);
            _editContext!.NotifyValidationStateChanged();
            _focusError = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _working = false; }
    }

    private async Task SetEnabledAsync(PromotionResponse row)
    {
        if (Busy) return;
        _working = true;
        _error = _notice = null;
        try
        {
            await Api.SetEnabledAsync(row.Id, !row.IsEnabled, _lifetime.Token);
            // Refresh an open editor so a later save cannot accidentally undo this availability change.
            if (_editingId == row.Id) CloseForm();
            _notice = row.IsEnabled ? "Đã tắt mã giảm giá. Lịch sử sử dụng được giữ nguyên. (MSG-04)" : "Đã bật mã giảm giá. (MSG-04)";
            _focusSuccess = true;
            await LoadAsync();
        }
        catch (AuthApiException error) { _error = error.Message; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _working = false; }
    }

    private void CloseForm()
    {
        DetachValidation();
        _form = null;
        _editContext = null;
        _messages = null;
        _editingId = null;
        _saveError = null;
    }

    private void ValidateSubmit(object? sender, ValidationRequestedEventArgs args)
    {
        _submitted = true;
        _saveError = _notice = null;
        Validate();
    }

    private void ValidateChange(object? sender, FieldChangedEventArgs args)
    {
        _saveError = _notice = null;
        Validate();
    }

    private void DiscountTypeChanged()
    {
        if (_form?.DiscountType == PromotionDiscountType.Fixed) _form.MaxDiscountAmount = null;
        Validate();
    }

    private void Validate()
    {
        if (_form is null || _editContext is null || _messages is null) return;
        _messages.Clear();
        var failures = _editingId is null ? _createValidator.Validate(_form.CreateRequest()).Errors
            : _updateValidator.Validate(_form.UpdateRequest()).Errors;
        foreach (var failure in failures)
        {
            var field = new FieldIdentifier(_form, failure.PropertyName);
            if (_submitted || _editContext.IsModified(field)) _messages.Add(field, failure.ErrorMessage);
        }
        _editContext.NotifyValidationStateChanged();
    }

    private void FocusErrors() => _focusError = true;

    private void DetachValidation()
    {
        if (_editContext is null) return;
        _editContext.OnValidationRequested -= ValidateSubmit;
        _editContext.OnFieldChanged -= ValidateChange;
    }

    private static string Money(decimal value) => value.ToString("N0", Vietnamese) + " ₫";
    private static string DateLabel(DateTime date) => PromotionFormModel.ToGymTime(date).ToString("dd/MM/yyyy HH:mm", Vietnamese);
    private static string DiscountLabel(PromotionResponse response) => response.DiscountType == PromotionDiscountType.Percentage
        ? response.DiscountValue.ToString("0.##", Vietnamese) + "%" : Money(response.DiscountValue);
    private static string StatusLabel(PromotionStatus status) => status switch
    {
        PromotionStatus.Scheduled => "Đã lên lịch / Scheduled",
        PromotionStatus.Active => "Đang áp dụng / Active",
        PromotionStatus.Expired => "Hết hạn / Expired",
        PromotionStatus.Disabled => "Đã tắt / Disabled",
        _ => status.ToString()
    };
    private static string BadgeClass(PromotionStatus status) => "inline-flex rounded-full border px-3 py-1 text-xs font-semibold " + (status switch
    {
        PromotionStatus.Active => "border-emerald-200 bg-emerald-50 text-emerald-900",
        PromotionStatus.Scheduled => "border-amber-200 bg-amber-50 text-amber-900",
        PromotionStatus.Expired => "border-red-200 bg-red-50 text-red-900",
        _ => "border-vfms-border bg-vfms-muted text-vfms-text-secondary"
    });

    public void Dispose()
    {
        DetachValidation();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
