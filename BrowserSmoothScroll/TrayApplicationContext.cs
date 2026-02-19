using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace BrowserSmoothScroll;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly BrowserProcessTracker _processTracker;
    private readonly ScrollDebugLogger _debugLogger;
    private readonly SmoothScrollService _scrollService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledMenuItem;
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
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Browser Smooth Scroll",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettingsDialog();

        ApplySettings(_settings, persist: false);

        try
        {
            _scrollService.Start();
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
