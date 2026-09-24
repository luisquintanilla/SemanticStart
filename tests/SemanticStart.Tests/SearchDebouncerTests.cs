using System.Diagnostics;
using SemanticStart.App;

namespace SemanticStart.Tests;

/// <summary>
/// The debounce exists to answer the question the user finished asking. These tests cover the two
/// ways it can be wrong: running while the user is still working, and never running at all.
/// </summary>
public sealed class SearchDebouncerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(60);

    [Fact]
    public async Task RunsOnceTheIntervalHasPassed()
    {
        var ran = new TaskCompletionSource();
        var debouncer = new SearchDebouncer(Interval, NotHeld);

        debouncer.Schedule(_ =>
        {
            ran.TrySetResult();
            return Task.CompletedTask;
        });

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReschedulingAbandonsTheRunThatWasWaiting()
    {
        var runs = 0;
        var debouncer = new SearchDebouncer(RescheduleInterval, NotHeld);

        // Rescheduled with no delay between calls, and with an interval far longer than a slow
        // runner can stall a tight loop, so no earlier run can legitimately expire first.
        for (var i = 0; i < 5; i++)
        {
            debouncer.Schedule(_ =>
            {
                Interlocked.Increment(ref runs);
                return Task.CompletedTask;
            });
        }

        await Task.Delay(RescheduleInterval * 4);
        Assert.Equal(1, runs);
    }

    private static readonly TimeSpan RescheduleInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The reported defect. Windows starts repeating a held key after 500 ms by default, which is
    /// also the default debounce, so deleting used to rebuild the list under a finger that was
    /// still down. Nothing may run until the key is released.
    /// </summary>
    [Fact]
    public async Task WaitsForAHeldEditingKeyToComeUp()
    {
        var held = true;
        var ranAt = new TaskCompletionSource<TimeSpan>();
        var clock = Stopwatch.StartNew();

        var debouncer = new SearchDebouncer(Interval, _ => Task.FromResult(Volatile.Read(ref held)));

        debouncer.Schedule(_ =>
        {
            ranAt.TrySetResult(clock.Elapsed);
            return Task.CompletedTask;
        });

        await Task.Delay(Interval * 5);
        Assert.False(ranAt.Task.IsCompleted);

        Volatile.Write(ref held, false);
        var elapsed = await ranAt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(elapsed >= Interval * 5, $"ran after {elapsed.TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// A key-up lost to a focus change must not cost the user their search. The extension is
    /// bounded, so a probe that never reports a release still ends in a result.
    /// </summary>
    [Fact]
    public async Task RunsAnywayWhenTheKeyNeverReportsAsReleased()
    {
        var ran = new TaskCompletionSource();
        var debouncer = new SearchDebouncer(TimeSpan.FromMilliseconds(2), _ => Task.FromResult(true));

        debouncer.Schedule(_ =>
        {
            ran.TrySetResult();
            return Task.CompletedTask;
        });

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Turning the wait off in settings has to mean off, not "one tick".
    /// </summary>
    [Fact]
    public void RunsSynchronouslyWhenTheIntervalIsZero()
    {
        var ran = false;
        var debouncer = new SearchDebouncer(TimeSpan.Zero, NotHeld);

        debouncer.Schedule(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(ran);
    }

    [Fact]
    public async Task CancelStopsThePendingRun()
    {
        var runs = 0;
        var debouncer = new SearchDebouncer(Interval, NotHeld);

        debouncer.Schedule(_ =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        });

        debouncer.Cancel();

        await Task.Delay(Interval * 6);
        Assert.Equal(0, runs);
    }

    private static Task<bool> NotHeld(CancellationToken cancellationToken) => Task.FromResult(false);
}
