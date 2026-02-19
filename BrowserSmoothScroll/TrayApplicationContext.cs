using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Linq;
using System.Linq;

namespace BrowserSmoothScroll;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly BrowserProcessTracker _processTracker;
    private readonly ScrollDebugLogger _debugLogger;
    private readonly SmoothScrollService _scrollService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _blockAppMenuItem;
    private AppSettings _settings;

    public TrayApplicationContext()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logsPath = Path.Combine(appDataRoot, "BrowserSmoothScroll", "logs");
        _debugLogger = new ScrollDebugLogger(logsPath);

        _processTracker = new BrowserProcessTracker(GetCurrentSettings);
        _scrollService = new SmoothScrollService(GetCurrentSettings, _processTracker, _debugLogger);

        _enabledMenuItem = new ToolStripMenuItem("Enabled")
        {
            CheckOnClick = true
        };
        _enabledMenuItem.Click += (_, _) => ToggleEnabled();

        var settingsMenuItem = new ToolStripMenuItem("Settings...");
        settingsMenuItem.Click += (_, _) => ShowSettingsDialog();

        var openLogsMenuItem = new ToolStripMenuItem("Open Logs Folder");
        openLogsMenuItem.Click += (_, _) => OpenLogsFolder();

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsMenuItem);
        menu.Items.Add(openLogsMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        
        _blockAppMenuItem = new ToolStripMenuItem("Block App...");
        _blockAppMenuItem.Click += (_, _) => ToggleBlockCurrentApp();
        menu.Items.Add(_blockAppMenuItem);
        
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        menu.Opening += (_, _) => UpdateBlockAppMenuItem();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = "Browser Smooth Scroll",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsDialog();

        ApplySettings(_settings, persist: false);

        try
        {
            _scrollService.Start();
            _notifyIcon.ShowBalloonTip(3000, "Browser Smooth Scroll Started", "Smooth sailing! App is active in the tray.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to start mouse hook.\n\n{ex.Message}",
                "Browser Smooth Scroll",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitThread();
        }
    }

    private void ToggleEnabled()
    {
        var updated = GetEditableSettings();
        updated.Enabled = _enabledMenuItem.Checked;
        ApplySettings(updated, persist: true);
    }

    private void ShowSettingsDialog()
    {
        using var form = new SettingsForm(GetEditableSettings());
        if (form.ShowDialog() != DialogResult.OK || form.UpdatedSettings is null)
        {
            return;
        }

        ApplySettings(form.UpdatedSettings, persist: true);
    }

    private void ApplySettings(AppSettings settings, bool persist)
    {
        settings.Normalize();
        Volatile.Write(ref _settings, settings.Clone());

        _enabledMenuItem.Checked = settings.Enabled;
        _notifyIcon.Text = settings.Enabled
            ? "Browser Smooth Scroll (Enabled)"
            : "Browser Smooth Scroll (Paused)";

        _processTracker.RefreshNow();
        StartupRegistration.Apply(settings.AutoStartOnLogin);
        _debugLogger.SetEnabled(settings.DebugMode);

        if (persist)
        {
            _settingsStore.Save(settings);
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(_debugLogger.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _debugLogger.LogsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open logs folder.\n\n{ex.Message}",
                "Browser Smooth Scroll",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UpdateBlockAppMenuItem()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            _blockAppMenuItem.Enabled = false;
            return;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var processName = process.ProcessName;
            var settings = GetCurrentSettings();
            
            var isBlocked = settings.BlockedProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

            _blockAppMenuItem.Text = isBlocked 
                ? $"Unblock {processName}" 
                : $"Block {processName}";
            
            _blockAppMenuItem.Tag = processName;
            _blockAppMenuItem.Enabled = true;
        }
        catch
        {
            _blockAppMenuItem.Text = "Block App...";
            _blockAppMenuItem.Enabled = false;
        }
    }

    private void ToggleBlockCurrentApp()
    {
        if (_blockAppMenuItem.Tag is not string processName) return;

        var settings = GetEditableSettings();
        var blocked = settings.BlockedProcessNames.ToList();
        
        var removed = blocked.RemoveAll(x => string.Equals(x, processName, StringComparison.OrdinalIgnoreCase)) > 0;
        
        if (!removed)
        {
            blocked.Add(processName);
        }

        settings.ProcessBlockList = string.Join(",", blocked);
        ApplySettings(settings, persist: true);
    }

    private AppSettings GetCurrentSettings()
    {
        return Volatile.Read(ref _settings);
    }

    private AppSettings GetEditableSettings()
    {
        return GetCurrentSettings().Clone();
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _scrollService.Dispose();
        _processTracker.Dispose();
        _debugLogger.Dispose();
        base.ExitThreadCore();
    }
}
