using VFMS.Client.Services;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>Search as you type: waits for a pause, and a newer search always wins.</summary>
public sealed class LiveQueryTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(80);

    [Fact]
    public async Task FastTypingLoadsOnlyOnceWithTheLastText()
    {
        using var live = new LiveQuery(CancellationToken.None, Delay);
        var loaded = new List<string>();

        var runs = new[] { "w", "wh", "whe", "whey" }
            .Select(text => live.AfterTypingAsync(_ => { loaded.Add(text); return Task.CompletedTask; }))
            .ToArray();
        await Task.WhenAll(runs);

        Assert.Equal(["whey"], loaded);
    }

    [Fact]
    public async Task NowLoadsWithoutWaitingAndCancelsPendingTyping()
    {
        using var live = new LiveQuery(CancellationToken.None, TimeSpan.FromSeconds(5));
        var loaded = new List<string>();

        var typing = live.AfterTypingAsync(_ => { loaded.Add("typed"); return Task.CompletedTask; });
        await live.NowAsync(_ => { loaded.Add("dropdown"); return Task.CompletedTask; });
        await typing;

        Assert.Equal(["dropdown"], loaded);
    }

    [Fact]
    public async Task ASlowOlderLoadIsCancelledWhenANewerOneStarts()
    {
        using var live = new LiveQuery(CancellationToken.None, Delay);
        var started = new TaskCompletionSource();
        string? shown = null;

        var older = live.NowAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            shown = "old results";
        });
        await started.Task;
        await live.NowAsync(_ => { shown = "new results"; return Task.CompletedTask; });
        await older;

        Assert.Equal("new results", shown);
    }

    [Fact]
    public async Task ClosingThePageStopsAPendingSearch()
    {
        using var lifetime = new CancellationTokenSource();
        using var live = new LiveQuery(lifetime.Token, Delay);
        var loaded = false;

        var pending = live.AfterTypingAsync(_ => { loaded = true; return Task.CompletedTask; });
        lifetime.Cancel();
        await pending;

        Assert.False(loaded);
    }

    [Fact]
    public async Task OtherErrorsStillReachThePage()
    {
        using var live = new LiveQuery(CancellationToken.None, Delay);
        await Assert.ThrowsAsync<InvalidOperationException>(() => live.NowAsync(_ => throw new InvalidOperationException()));
    }
}
