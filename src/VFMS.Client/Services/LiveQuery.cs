namespace VFMS.Client.Services;

/// <summary>
/// Runs a list page's load as filters change, so results follow what the user types or picks.
/// Typing waits for a short pause before loading, so "whey" sends one request rather than four.
/// Every new run cancels the one before it, so a slow older response never replaces newer results.
/// </summary>
public sealed class LiveQuery(CancellationToken lifetime, TimeSpan? typingDelay = null) : IDisposable
{
    /// <summary>Pause after the last keystroke before searching.</summary>
    public static readonly TimeSpan DefaultTypingDelay = TimeSpan.FromMilliseconds(300);

    private readonly TimeSpan _typingDelay = typingDelay ?? DefaultTypingDelay;
    private CancellationTokenSource? _current;

    /// <summary>For a keystroke in a search box: loads once typing pauses.</summary>
    public Task AfterTypingAsync(Func<CancellationToken, Task> load) => RunAsync(load, _typingDelay);

    /// <summary>For a dropdown, date, page change or Enter: loads straight away.</summary>
    public Task NowAsync(Func<CancellationToken, Task> load) => RunAsync(load, TimeSpan.Zero);

    private async Task RunAsync(Func<CancellationToken, Task> load, TimeSpan delay)
    {
        _current?.Cancel();
        _current?.Dispose();
        var run = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _current = run;
        var token = run.Token;
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token);
            await load(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced by a newer search, or the page was closed.
        }
    }

    public void Dispose()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }
}
