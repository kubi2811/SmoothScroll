using System.Text.Json;

namespace BrowserSmoothScroll;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDir = Path.Combine(appData, "BrowserSmoothScroll");
        Directory.CreateDirectory(settingsDir);
        _settingsFilePath = Path.Combine(settingsDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaults = new AppSettings();
                defaults.Normalize();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            loaded.Normalize();
            return loaded;
        }
        catch
        {
            var defaults = new AppSettings();
            defaults.Normalize();
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }
}
