using SemanticStart.App;
using Xunit;

namespace SemanticStart.Tests;

/// <summary>
/// Covers when a background refresh runs and — more importantly — when it does not.
///
/// Every one of these is a decision about spending the user's machine without being asked, so the
/// scheduler takes its clock, its busy signal and the refresh itself as delegates. Nothing here
/// indexes anything or waits on a timer; the tick is driven directly and the clock is a variable.
/// </summary>
public sealed class IndexRefreshSchedulerTests
{
    [Fact]
    public async Task NothingRunsBeforeTheStartupDelayHasPassed()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        clock.Advance(IndexRefreshScheduler.StartupDelay - TimeSpan.FromSeconds(30));

        Assert.False(await scheduler.TickAsync());
        Assert.Equal(0, runs);
    }

    /// <summary>
    /// The reason for refreshing at startup at all: the app was closed while something was
    /// installed or removed, and nothing else will notice.
    /// </summary>
    [Fact]
    public async Task TheFirstRefreshRunsShortlyAfterStartup()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        clock.Advance(IndexRefreshScheduler.StartupDelay);

        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task TheNextRefreshWaitsForTheInterval()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        await scheduler.TickAsync();

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(2, runs);
    }

    /// <summary>
    /// The reason the tick compares wall-clock times instead of asking a timer to fire in an hour.
    /// A timer does not run while the machine is asleep, so a laptop closed overnight would come
    /// back with most of an hour still to wait on an index that is now a day stale.
    /// </summary>
    [Fact]
    public async Task AMachineComingBackFromSleepRefreshesImmediately()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        await scheduler.TickAsync();

        clock.Advance(TimeSpan.FromHours(9));

        Assert.True(await scheduler.TickAsync());
        Assert.Equal(2, runs);
    }

    /// <summary>
    /// A refresh ends by reloading the search engine. Doing that under a query the user is typing
    /// buys nothing: the next tick is a minute away.
    /// </summary>
    [Fact]
    public async Task NothingRunsWhileTheOverlayIsOpen()
    {
        var clock = new Clock();
        var runs = 0;
        var overlayOpen = true;
        using var scheduler = Scheduler(clock, () => runs++, isUserBusy: () => overlayOpen);

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        Assert.False(await scheduler.TickAsync());

        overlayOpen = false;
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// An empty index means the first build: minutes of work, choices that have to be made before
    /// it starts, and the one build the user is actually told about. A timer must not turn that
    /// into something that happens silently two minutes after launch.
    /// </summary>
    [Fact]
    public async Task AnEmptyIndexIsNeverBuiltInTheBackground()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++, entityCount: () => 0);

        clock.Advance(TimeSpan.FromHours(4));

        Assert.False(await scheduler.TickAsync());
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task NothingRunsWhenTheSettingIsOff()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++, settings: new AppSettings { BackgroundRefresh = false });

        clock.Advance(TimeSpan.FromHours(4));

        Assert.False(await scheduler.TickAsync());
        Assert.Equal(0, runs);
    }

    /// <summary>
    /// The case the whole feature exists for: a program is installed, its shortcut appears, and the
    /// index picks it up in under a minute instead of within the hour.
    /// </summary>
    [Fact]
    public async Task AStartMenuChangeRefreshesOnceItHasSettled()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        scheduler.NotifyPossibleChange();

        // Still mid-install. Scanning now would index a half-written Start Menu folder.
        clock.Advance(IndexRefreshScheduler.ChangeSettleDelay - TimeSpan.FromSeconds(5));
        Assert.False(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// An installer writes its shortcuts over several seconds and a suite writes dozens. The wait
    /// restarts on every event, so what is scanned is a finished install rather than a partial one.
    /// </summary>
    [Fact]
    public async Task ABurstOfChangesRestartsTheWait()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        scheduler.NotifyPossibleChange();
        clock.Advance(IndexRefreshScheduler.ChangeSettleDelay - TimeSpan.FromSeconds(5));

        // The installer is still going.
        scheduler.NotifyPossibleChange();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(await scheduler.TickAsync());

        clock.Advance(IndexRefreshScheduler.ChangeSettleDelay);
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// A refresh triggered by a change must not leave that change pending, or the scheduler would
    /// refresh again on the very next tick for something it has already picked up.
    /// </summary>
    [Fact]
    public async Task AChangeIsOnlyActedOnOnce()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () => runs++);

        scheduler.NotifyPossibleChange();
        clock.Advance(IndexRefreshScheduler.ChangeSettleDelay);
        Assert.True(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// The user clicked Rebuild, or first-run setup is still going. That serves the same purpose
    /// this would, so the interval restarts rather than firing the moment their build ends.
    /// </summary>
    [Fact]
    public async Task ARebuildTheUserStartedRestartsTheInterval()
    {
        var clock = new Clock();
        var runs = 0;
        var rebuilding = true;
        using var scheduler = Scheduler(clock, () => runs++, isRebuildRunning: () => rebuilding);

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        Assert.False(await scheduler.TickAsync());

        rebuilding = false;

        // Their build just finished. A scheduled refresh immediately afterwards would scan a
        // machine that was scanned seconds ago.
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromMinutes(56));
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    /// <summary>The interval is measured from the end of a refresh, not from when it started.</summary>
    [Fact]
    public async Task ALongRefreshDoesNotShortenTheGapAfterIt()
    {
        var clock = new Clock();
        var runs = 0;
        using var scheduler = Scheduler(clock, () =>
        {
            runs++;
            clock.Advance(TimeSpan.FromMinutes(10));
        });

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        await scheduler.TickAsync();

        clock.Advance(TimeSpan.FromMinutes(55));
        Assert.False(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(2, runs);
    }

    /// <summary>
    /// A failing refresh must not stop the scheduler. It runs for the life of the process, and one
    /// bad pass - a locked database, a collector throwing - is not a reason to stop trying.
    /// </summary>
    [Fact]
    public async Task AFailedRefreshIsSwallowedAndTheScheduleContinues()
    {
        var clock = new Clock();
        var attempts = 0;
        using var scheduler = Scheduler(clock, () =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("indexing blew up");
        });

        clock.Advance(IndexRefreshScheduler.StartupDelay);
        Assert.False(await scheduler.TickAsync());

        clock.Advance(TimeSpan.FromHours(2));
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// Reading the setting on every tick is what lets the checkbox take effect without anything
    /// having to be told it changed.
    /// </summary>
    [Fact]
    public async Task TurningTheSettingBackOnTakesEffectWithoutARestart()
    {
        var clock = new Clock();
        var runs = 0;
        var settings = new AppSettings { BackgroundRefresh = false };
        using var scheduler = Scheduler(clock, () => runs++, settingsSource: () => settings);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.False(await scheduler.TickAsync());

        settings = new AppSettings { BackgroundRefresh = true };
        Assert.True(await scheduler.TickAsync());
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task NothingRunsAfterDisposal()
    {
        var clock = new Clock();
        var runs = 0;
        var scheduler = Scheduler(clock, () => runs++);

        scheduler.Dispose();
        clock.Advance(TimeSpan.FromHours(2));

        Assert.False(await scheduler.TickAsync());
        Assert.Equal(0, runs);
    }

    private static IndexRefreshScheduler Scheduler(
        Clock clock,
        Action refresh,
        Func<int>? entityCount = null,
        Func<bool>? isRebuildRunning = null,
        Func<bool>? isUserBusy = null,
        AppSettings? settings = null,
        Func<AppSettings>? settingsSource = null)
    {
        var fixedSettings = settings ?? new AppSettings();

        return new IndexRefreshScheduler(
            settingsSource ?? (() => fixedSettings),
            entityCount ?? (() => 583),
            isRebuildRunning ?? (() => false),
            isUserBusy ?? (() => false),
            () => clock.Now,
            _ =>
            {
                refresh();
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// A clock the test moves by hand. Every decision the scheduler makes is a comparison of two
    /// instants, so this is the whole of what has to be faked.
    /// </summary>
    private sealed class Clock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => Now += by;
    }
}
