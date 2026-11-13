using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using VFMS.Client.Services;
using VFMS.Contracts.Memberships;
using VFMS.Contracts.Memberships.Validators;

namespace VFMS.Client.Pages.Manager;

public partial class MembershipFreezePolicy : IDisposable
{
    private const string Label = "block text-sm font-semibold text-vfms-text";
    private const string Field = "mt-1 block min-h-[44px] w-full rounded-lg border border-vfms-border bg-vfms-surface px-3 py-2 text-base text-vfms-text sm:text-sm";
    private const string SecondaryButton = "min-h-[44px] rounded-lg border border-vfms-border bg-vfms-surface px-3 py-2 text-sm font-semibold text-vfms-text hover:bg-vfms-muted";

    [Inject] private MembershipPlanApiClient Api { get; set; } = default!;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly MembershipFreezePolicyRequestValidator _validator = new();
    private PolicyForm? _form;
    private EditContext? _editContext;
    private ValidationMessageStore? _messages;
    private int _nextQuotaId;
    private string? _loadError;
    private string? _saveError;
    private bool _loading;
    private bool _saving;
    private bool _configured;
    private bool _submitted;
    private bool _success;
    private bool _focusError;
    private bool _focusSuccess;
    private bool _focusAddQuota;
    private QuotaForm? _quotaToFocus;
    private ElementReference _errorNotice;
    private ElementReference _successNotice;
    private ElementReference _addQuotaButton;

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
        else if (_quotaToFocus is { } quota)
        {
            _quotaToFocus = null;
            if (quota.DurationInput?.Element is { } input)
                await input.FocusAsync();
        }
        else if (_focusAddQuota)
        {
            _focusAddQuota = false;
            await _addQuotaButton.FocusAsync();
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
        _configured = response.Policy is not null;
        _submitted = false;
        _form = new()
        {
            MinimumFreezeDays = response.Policy?.MinimumFreezeDays,
            MaximumFreezeDays = response.Policy?.MaximumFreezeDays,
            MinimumRequestDays = response.Policy?.MinimumRequestDays,
            EligibilityPaidDays = response.Policy?.EligibilityPaidDays
        };
        foreach (var quota in response.Quotas)
        {
            _form.Quotas.Add(new(++_nextQuotaId)
            {
                Duration = quota.Duration,
                Unit = quota.Unit,
                MaximumCumulativeFreezeDays = quota.MaximumCumulativeFreezeDays
            });
        }

        _editContext = new(_form);
        _messages = new(_editContext);
        _editContext.OnValidationRequested += ValidateSubmit;
        _editContext.OnFieldChanged += ValidateChange;
    }

    private void AddQuota()
    {
        if (_saving || _form is not { } form)
            return;

        ClearFeedback();
        var quota = new QuotaForm(++_nextQuotaId);
        form.Quotas.Add(quota);
        _quotaToFocus = quota;
    }

    private void RemoveQuota(QuotaForm quota)
    {
        if (_saving || _form is not { } form)
            return;

        form.Quotas.Remove(quota);
        ClearFeedback();
        Validate();
        _focusAddQuota = true;
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
        foreach (var failure in _validator.Validate(ToRequest(_form!)).Errors)
        {
            var field = ResolveField(failure.PropertyName);
            if (_submitted || _editContext!.IsModified(field) || field.FieldName == nameof(PolicyForm.Quotas))
                _messages.Add(field, failure.ErrorMessage);
        }
        _editContext!.NotifyValidationStateChanged();
    }

    private FieldIdentifier ResolveField(string path)
    {
        const string prefix = nameof(PolicyForm.Quotas) + "[";
        var close = path.IndexOf(']');
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && close >= prefix.Length &&
            close + 2 < path.Length && path[close + 1] == '.' &&
            int.TryParse(path.AsSpan(prefix.Length, close - prefix.Length), out var index) &&
            index >= 0 && index < _form!.Quotas.Count)
            return new(_form.Quotas[index], path[(close + 2)..]);

        return new(_form!, path);
    }

    private static MembershipFreezePolicyRequest ToRequest(PolicyForm form) =>
        new(form.MinimumFreezeDays, form.MaximumFreezeDays, form.MinimumRequestDays, form.EligibilityPaidDays,
            form.Quotas.Select(quota => new MembershipFreezeTierQuotaRequest(
                quota.Duration, quota.Unit, quota.MaximumCumulativeFreezeDays)).ToArray());

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
        public int? MinimumFreezeDays { get; set; }
        public int? MaximumFreezeDays { get; set; }
        public int? MinimumRequestDays { get; set; }
        public int? EligibilityPaidDays { get; set; }
        public List<QuotaForm> Quotas { get; } = [];
    }

    private sealed class QuotaForm(int id)
    {
        public int Id { get; } = id;
        public int? Duration { get; set; }
        public string? Unit { get; set; }
        public int? MaximumCumulativeFreezeDays { get; set; }
        public InputNumber<int?>? DurationInput { get; set; }
    }
}
