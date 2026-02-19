using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace BrowserSmoothScroll;

internal sealed class BrowserProcessTracker : IDisposable
{
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Timer _refreshTimer;
    private readonly object _stateLock = new();
    private HashSet<int> _trackedProcessIds = [];
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
        if (settings.EnableForAllAppsByDefault)
        {
            lock (_stateLock)
            {
                _trackedProcessIds = [];
            }

            return;
        }

        var next = new HashSet<int>();

        foreach (var processName in settings.AllowedProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        next.Add(process.Id);
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
            _trackedProcessIds = next;
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
