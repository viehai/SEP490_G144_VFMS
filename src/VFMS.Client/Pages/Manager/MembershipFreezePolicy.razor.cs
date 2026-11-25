using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;

namespace VFMS.Client.Pages.Manager;

public partial class MembershipFreezePolicy : IDisposable
{
    private const string SecondaryButton = "min-h-[44px] rounded-lg border border-vfms-border bg-vfms-surface px-3 py-2 text-sm font-semibold text-vfms-text hover:bg-vfms-muted";

    [Inject] private MembershipPlanApiClient Api { get; set; } = default!;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly MembershipFreezePolicyRequestValidator _validator = new();
    private PolicyForm? _form;
    private EditContext? _editContext;
    private ValidationMessageStore? _messages;
    private string? _loadError;
    private string? _saveError;
    private bool _loading;
    private bool _saving;
    private bool _configured;
    private bool _submitted;
    private bool _success;
    private bool _focusError;
    private bool _focusSuccess;
    private ElementReference _errorNotice;
    private ElementReference _successNotice;

    protected override Task OnInitializedAsync() => LoadAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusError)
        {
            _focusError = false;
            await _errorNotice.FocusAsync();
        }
        else if (_focusSuccess)
        {
            _focusSuccess = false;
            await _successNotice.FocusAsync();
        }
    }

    private async Task LoadAsync()
    {
        if (_loading)
            return;

        _loading = true;
        _loadError = null;
        var ct = _lifetime.Token;
        try
        {
            var response = await Api.GetMembershipFreezePolicyAsync(ct);
            if (!ct.IsCancellationRequested)
                SetForm(response);
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

    private async Task SaveAsync()
    {
        if (_saving || _form is not { } form)
            return;

        ClearFeedback();
        _saving = true;
        var ct = _lifetime.Token;
        try
        {
            var response = await Api.UpdateMembershipFreezePolicyAsync(ToRequest(form), ct);
            if (!ct.IsCancellationRequested)
            {
                SetForm(response);
                _success = _focusSuccess = true;
            }
        }
        catch (AuthApiException error)
        {
            if (ct.IsCancellationRequested)
                return;

            _saveError = error.Message;
            foreach (var field in error.Errors)
                _messages!.Add(ResolveField(field.Key), field.Value);
            _editContext!.NotifyValidationStateChanged();
            FocusErrors();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _saving = false;
        }
    }

    private void SetForm(MembershipFreezePolicyResponse response)
    {
        DetachValidation();
        var policy = response.Policy;
        _configured = policy is not null;
        _submitted = false;
        _form = new() { DefaultMaxFreezeCount = policy?.DefaultMaxFreezeCount ?? 1 };
        if (policy is not null)
        {
            (_form.MinimumFreezeDays, _form.MinimumFreezeDaysUnit) = RestoreInput(policy.MinimumFreezeDays, policy.MinimumFreezeDaysUnit);
            (_form.MaximumFreezeDays, _form.MaximumFreezeDaysUnit) = RestoreInput(policy.MaximumFreezeDays, policy.MaximumFreezeDaysUnit);
            (_form.MinimumRequestDays, _form.MinimumRequestDaysUnit) = RestoreInput(policy.MinimumRequestDays, policy.MinimumRequestDaysUnit);
            (_form.EligibilityPaidDays, _form.EligibilityPaidDaysUnit) = RestoreInput(policy.EligibilityPaidDays, policy.EligibilityPaidDaysUnit);
        }

        _editContext = new(_form);
        _messages = new(_editContext);
        _editContext.OnValidationRequested += ValidateSubmit;
        _editContext.OnFieldChanged += ValidateChange;
    }

    private void ValidateSubmit(object? sender, ValidationRequestedEventArgs args)
    {
        ClearFeedback();
        _submitted = true;
        Validate();
    }

    private void ValidateChange(object? sender, FieldChangedEventArgs args)
    {
        ClearFeedback();
        Validate();
    }

    private void Validate()
    {
        _messages!.Clear();
        var countField = ResolveField(nameof(PolicyForm.DefaultMaxFreezeCount));
        if (_form!.DefaultMaxFreezeCount is null && (_submitted || _editContext!.IsModified(countField)))
            _messages.Add(countField, "Vui lòng nhập số lượt bảo lưu mặc định.");
        foreach (var failure in _validator.Validate(ToRequest(_form!)).Errors)
        {
            var field = ResolveField(failure.PropertyName);
            if (_submitted || _editContext!.IsModified(field) ||
                _editContext.IsModified(new FieldIdentifier(_form!, field.FieldName + "Unit")))
                _messages.Add(field, failure.ErrorMessage);
        }
        _editContext!.NotifyValidationStateChanged();
    }

    private FieldIdentifier ResolveField(string path) => new(_form!,
        typeof(PolicyForm).GetProperties().FirstOrDefault(property => property.Name.Equals(path, StringComparison.OrdinalIgnoreCase))?.Name ?? path);

    private static MembershipFreezePolicyRequest ToRequest(PolicyForm form) =>
        new(form.MinimumFreezeDays, form.MaximumFreezeDays, form.MinimumRequestDays, form.EligibilityPaidDays, null)
        {
            DefaultMaxFreezeCount = form.DefaultMaxFreezeCount,
            MinimumFreezeDaysUnit = form.MinimumFreezeDaysUnit,
            MaximumFreezeDaysUnit = form.MaximumFreezeDaysUnit,
            MinimumRequestDaysUnit = form.MinimumRequestDaysUnit,
            EligibilityPaidDaysUnit = form.EligibilityPaidDaysUnit
        };

    private static (int? Value, MembershipDurationUnit Unit) RestoreInput(int? days, MembershipDurationUnit unit) =>
        unit == MembershipDurationUnit.Month && days is { } count && count % MembershipFreezeDuration.DaysPerMonth == 0
            ? (count / MembershipFreezeDuration.DaysPerMonth, MembershipDurationUnit.Month)
            : (days, MembershipDurationUnit.Day);

    private void ClearFeedback()
    {
        _success = false;
        _saveError = null;
    }

    private void FocusErrors() => _focusError = true;

    private void DetachValidation()
    {
        if (_editContext is null)
            return;

        _editContext.OnValidationRequested -= ValidateSubmit;
        _editContext.OnFieldChanged -= ValidateChange;
    }

    public void Dispose()
    {
        DetachValidation();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class PolicyForm
    {
        public int? DefaultMaxFreezeCount { get; set; }
        public int? MinimumFreezeDays { get; set; }
        public MembershipDurationUnit MinimumFreezeDaysUnit { get; set; }
        public int? MaximumFreezeDays { get; set; }
        public MembershipDurationUnit MaximumFreezeDaysUnit { get; set; }
        public int? MinimumRequestDays { get; set; }
        public MembershipDurationUnit MinimumRequestDaysUnit { get; set; }
        public int? EligibilityPaidDays { get; set; }
        public MembershipDurationUnit EligibilityPaidDaysUnit { get; set; }
    }
}
