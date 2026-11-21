namespace VFMS.Client.Services;

/// <summary>Success toasts (spec MSG-03, MSG-04, MSG-05). Field and request errors stay inline on the page.</summary>
public sealed class ToastService
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private readonly List<Toast> _items = [];
    private int _nextId;

    public IReadOnlyList<Toast> Items => _items;
    public event Action? Changed;

    public void Show(string message)
    {
        var toast = new Toast(++_nextId, message);
        _items.Add(toast);
        Changed?.Invoke();
        _ = DismissLaterAsync(toast);
    }

    public void Dismiss(int id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0)
            Changed?.Invoke();
    }

    private async Task DismissLaterAsync(Toast toast)
    {
        await Task.Delay(Duration);
        Dismiss(toast.Id);
    }

    public sealed record Toast(int Id, string Message);
}
