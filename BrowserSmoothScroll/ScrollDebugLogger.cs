using System.Globalization;
using System.Text;

namespace BrowserSmoothScroll;

internal sealed class ScrollDebugLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _logsDirectory;
    private StreamWriter? _writer;
    private bool _enabled;
    private bool _disposed;

    public ScrollDebugLogger(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
    }

    public string LogsDirectory => _logsDirectory;

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || enabled == _enabled)
            {
                return;
            }

            _enabled = enabled;

            if (_enabled)
            {
                OpenWriterLocked();
                WriteLineLocked($"SESSION START app={Application.ProductVersion}");
            }
            else
            {
                WriteLineLocked("SESSION STOP");
                CloseWriterLocked();
            }
        }
    }

    public void LogImpulse(
        bool horizontal,
        int rawDelta,
        double targetDelta,
        double accelerationFactor,
        int durationMs,
        int combo,
        double comboRatio,
        long elapsedMs,
        double cadenceRatio)
    {
        var direction = horizontal ? "H" : "V";
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"IMPULSE dir={direction} raw={rawDelta} target={targetDelta:F3} accel={accelerationFactor:F3} durationMs={durationMs} combo={combo} comboRatio={comboRatio:F3} elapsedMs={elapsedMs} cadence={cadenceRatio:F3}");
        LogRaw(line);
    }

    public void LogHookWheel(
        bool horizontal,
        int rawDelta,
        bool injected,
        bool selfInjected,
        uint pid,
        string action)
    {
        var direction = horizontal ? "H" : "V";
        var source = !injected
            ? "raw"
            : selfInjected
                ? "self-injected"
                : "external-injected";

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"HOOK action={action} dir={direction} raw={rawDelta} source={source} pid={pid}");
        LogRaw(line);
    }

    public void LogTick(
        int outVertical,
        int outHorizontal,
        double residualVertical,
        double residualHorizontal,
        int activeVerticalImpulses,
        int activeHorizontalImpulses)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"TICK outV={outVertical} outH={outHorizontal} resV={residualVertical:F3} resH={residualHorizontal:F3} activeV={activeVerticalImpulses} activeH={activeHorizontalImpulses}");
        LogRaw(line);
    }

    public void LogSkippedWheel(string reason, uint pid, int rawDelta)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"SKIP reason={reason} pid={pid} raw={rawDelta}");
        LogRaw(line);
    }

    private void LogRaw(string line)
    {
        lock (_sync)
        {
            if (!_enabled || _writer is null || _disposed)
            {
                return;
            }

            WriteLineLocked(line);
        }
    }

    private void OpenWriterLocked()
    {
        Directory.CreateDirectory(_logsDirectory);
        var fileName = $"scroll_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var path = Path.Combine(_logsDirectory, fileName);
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    private void WriteLineLocked(string message)
    {
        _writer?.WriteLine($"{DateTime.Now:O} {message}");
    }

    private void CloseWriterLocked()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _enabled = false;
            WriteLineLocked("SESSION FORCE-CLOSE");
            CloseWriterLocked();
        }
    }
}
