using System.IO;
using SemanticStart.Core.Collectors;
using Threading = System.Threading;

namespace SemanticStart.App;

/// <summary>
/// Keeps the index current without anyone asking.
///
/// <para>
/// An index built once at setup is correct for exactly as long as the machine stands still, and
/// the moment it stops being correct is the moment it matters most: installing a program is when a
/// user is most likely to reach for a launcher to run it, and until something rescans, the one
/// thing they just installed is the one thing that cannot be found. The reverse is worse - an
/// uninstalled program keeps ranking until it is launched and fails.
/// </para>
/// <para>
/// This is affordable only because a rebuild is incremental. Discovery runs, stored content hashes
/// are compared, and everything unchanged is skipped before any documentation is gathered or any
/// text is embedded, so an unchanged machine costs a pass over the collectors and nothing else.
/// <see cref="Core.Indexing.IndexBuilder"/> has said so since it was written; what was missing was
/// something to run it.
/// </para>
/// </summary>
public sealed class IndexRefreshScheduler : IDisposable
{
    /// <summary>
    /// How long after launch the first refresh runs. Long enough to stay out of the way of a login,
    /// where every startup program is competing for the same disk, and short enough that a user who
    /// installed something while the app was closed does not wait long for it to appear.
    /// </summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How quiet the Start Menu has to go before a change there counts as finished. An installer
    /// writes its shortcuts over several seconds and a suite writes dozens, so the wait restarts on
    /// every event; refreshing on the first one would scan a half-installed program.
    /// </summary>
    public static readonly TimeSpan ChangeSettleDelay = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How often the clock is consulted. Deliberately far shorter than the refresh interval,
    /// because the decision is made by comparing wall-clock times rather than by asking a timer to
    /// fire in an hour: a machine that sleeps for the night would otherwise come back with its
    /// hourly timer still holding most of an hour, and a laptop that is only ever open for twenty
    /// minutes at a time would never refresh at all.
    /// </summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly Func<AppSettings> _settings;
    private readonly Func<int> _entityCount;
    private readonly Func<bool> _isRebuildRunning;
    private readonly Func<bool> _isUserBusy;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<AppSettings, Task> _refresh;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _lock = new();

    private DateTimeOffset _dueAt;
    private DateTimeOffset? _changeSeenAt;
    private Threading.Timer? _timer;
    private bool _running;
    private bool _disposed;

    public IndexRefreshScheduler(
        AppSettingsService settingsService,
        SemanticSearchService searchService,
        IndexRebuildCoordinator rebuilds,
        Func<bool> isUserBusy)
        : this(
            // Read back from disk rather than captured, so a change made in the settings window
            // takes effect on the next tick without anything having to be told about it. Once a
            // minute, this is not a cost worth engineering around.
            settingsService.Load,
            () => searchService.Count,
            () => rebuilds.IsRunning,
            isUserBusy,
            () => DateTimeOffset.Now,
            settings => rebuilds.StartAsync(settings, force: false, RebuildTrigger.Automatic))
    {
    }

    /// <summary>Every input as a delegate, so the policy can be tested without a clock or an index.</summary>
    internal IndexRefreshScheduler(
        Func<AppSettings> settings,
        Func<int> entityCount,
        Func<bool> isRebuildRunning,
        Func<bool> isUserBusy,
        Func<DateTimeOffset> now,
        Func<AppSettings, Task> refresh)
    {
        _settings = settings;
        _entityCount = entityCount;
        _isRebuildRunning = isRebuildRunning;
        _isUserBusy = isUserBusy;
        _now = now;
        _refresh = refresh;
        _dueAt = now() + StartupDelay;
    }

    /// <summary>Begins ticking and starts watching the Start Menu.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _timer is not null)
                return;

            _dueAt = _now() + StartupDelay;
            _timer = new Threading.Timer(_ => _ = TickAsync(), null, TickInterval, TickInterval);
        }

        WatchStartMenu();
    }

    /// <summary>
    /// Records that something may have changed on the machine. Does not refresh: the caller is a
    /// file system event, which arrives in bursts and in the middle of the change it is reporting.
    /// </summary>
    public void NotifyPossibleChange()
    {
        lock (_lock)
            _changeSeenAt = _now();
    }

    /// <summary>
    /// One decision point. Returns whether a refresh was started, which is what the tests assert
    /// on; the timer ignores it.
    /// </summary>
    internal async Task<bool> TickAsync()
    {
        try
        {
            var settings = _settings();
            var now = _now();

            // Someone else is already indexing - the user clicked Rebuild, or first-run setup is
            // still going. That serves the same purpose as this would, so the interval restarts
            // from here rather than firing the moment their build ends.
            if (_isRebuildRunning())
            {
                lock (_lock)
                {
                    _dueAt = now + Interval(settings);
                    _changeSeenAt = null;
                }

                return false;
            }

            string reason;
            lock (_lock)
            {
                if (_disposed || _running || !ShouldRefresh(now, settings, out reason))
                    return false;

                _running = true;
                _changeSeenAt = null;
            }

            try
            {
                Log.Info($"Background index refresh starting ({reason}).");
                await _refresh(settings);
            }
            finally
            {
                lock (_lock)
                {
                    _running = false;

                    // Measured from the end of the refresh, not the start. A refresh that took
                    // four minutes has just confirmed the machine; counting the interval from
                    // when it began would shorten the gap by however long the work took.
                    _dueAt = _now() + Interval(settings);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            // A tray app has nowhere to show this and no reason to stop ticking because one pass
            // failed. The coordinator already swallows rebuild failures; this is for everything
            // around it.
            Log.Error(ex, "Background index refresh failed");
            return false;
        }
    }

    private bool ShouldRefresh(DateTimeOffset now, AppSettings settings, out string reason)
    {
        reason = string.Empty;

        if (!settings.BackgroundRefresh)
            return false;

        // An empty index means the first build, which is minutes of work, needs choices made
        // before it starts, and is the one the user is told about. That is first-run setup's job,
        // not a timer's.
        if (_entityCount() == 0)
            return false;

        // The overlay is open, so the user is mid-search. A refresh ends by reloading the search
        // engine, which would stall the query they are typing. Nothing is lost by waiting - the
        // next tick is a minute away.
        if (_isUserBusy())
            return false;

        if (_changeSeenAt is { } seen && now - seen >= ChangeSettleDelay)
        {
            reason = "the Start Menu changed";
            return true;
        }

        if (now >= _dueAt)
        {
            reason = "scheduled";
            return true;
        }

        return false;
    }

    private static TimeSpan Interval(AppSettings settings) =>
        TimeSpan.FromMinutes(Math.Clamp(settings.RefreshIntervalMinutes, 15, 24 * 60));

    /// <summary>
    /// Watches the directories <see cref="StartShortcutCollector"/> reads. Installing or removing
    /// a program writes there, which makes this the cheapest reliable signal that the machine has
    /// changed - far cheaper than scanning to find out, and it covers the uninstall case that a
    /// user never thinks to ask about.
    /// </summary>
    private void WatchStartMenu()
    {
        foreach (var root in StartShortcutCollector.Roots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,

                    // An installer can write a whole folder faster than the events can be drained,
                    // and an overflowed buffer drops them silently. The handler only sets a flag,
                    // so a large buffer costs nothing but makes the flag far likelier to be set.
                    InternalBufferSize = 64 * 1024,
                };

                watcher.Created += OnStartMenuChanged;
                watcher.Deleted += OnStartMenuChanged;
                watcher.Renamed += OnStartMenuChanged;

                // Overflow means something happened and we do not know what, which is exactly the
                // condition a rescan answers.
                watcher.Error += (_, _) => NotifyPossibleChange();

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                // Losing a watcher costs responsiveness, not correctness: the interval still
                // catches the change within the hour.
                Log.Error(ex, $"Could not watch {root} for changes");
            }
        }
    }

    private void OnStartMenuChanged(object sender, FileSystemEventArgs e)
    {
        // Only the files the collector reads. Shortcuts land next to uninstall logs, icon caches
        // and desktop.ini, none of which change what is installed.
        if (e.Name is { } name
            && !name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
            && Path.HasExtension(name))
            return;

        NotifyPossibleChange();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop a Start Menu watcher");
            }
        }

        _watchers.Clear();
    }
}
