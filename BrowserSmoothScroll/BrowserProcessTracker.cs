using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace BrowserSmoothScroll;

internal sealed class BrowserProcessTracker : IDisposable
{
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Timer _refreshTimer;
    private readonly object _stateLock = new();
    private HashSet<int> _trackedProcessIds = [];
    private HashSet<int> _blockedProcessIds = [];
    private bool _disposed;

    public BrowserProcessTracker(Func<AppSettings> settingsProvider)
    {
        _settingsProvider = settingsProvider;
        _refreshTimer = new Timer(_ => RefreshTrackedProcesses(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public bool IsTracked(uint pid)
    {
        lock (_stateLock)
        {
            return _trackedProcessIds.Contains(unchecked((int)pid));
        }
    }

    public bool IsBlocked(uint pid)
    {
        lock (_stateLock)
        {
            return _blockedProcessIds.Contains(unchecked((int)pid));
        }
    }

    public void RefreshNow()
    {
        RefreshTrackedProcesses();
    }

    private void RefreshTrackedProcesses()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _settingsProvider();
        var blocked = new HashSet<int>();
        foreach (var processName in settings.BlockedProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    blocked.Add(process.Id);
                    process.Dispose(); // Using Dispose explicitly inside loop
                }
            }
            catch {}
        }

        if (settings.EnableForAllAppsByDefault)
        {
            lock (_stateLock)
            {
                _trackedProcessIds = [];
                _blockedProcessIds = blocked;
            }

            return;
        }

        var tracked = new HashSet<int>();

        foreach (var processName in settings.AllowedProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        tracked.Add(process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore transient access/process enumeration failures.
            }
        }

        lock (_stateLock)
        {
            _trackedProcessIds = tracked;
            _blockedProcessIds = blocked;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Dispose();
    }
}
