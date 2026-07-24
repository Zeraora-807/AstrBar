using System.Text.Json;
using AstrBar.Models;

namespace AstrBar.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AstrBar");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public void Save(AppSettings settings)
    {
        settings.CommandPrefixes = NormalizePrefixes(settings.CommandPrefixes);
        Directory.CreateDirectory(_settingsDirectory);

        var temporaryPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _settingsPath, overwrite: true);

        Current = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveOrbPosition(double left, double top)
    {
        Current.OrbLeft = left;
        Current.OrbTop = top;
        Current.OrbPositionSaved = true;
        Save(Current);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            settings.CommandPrefixes = NormalizePrefixes(settings.CommandPrefixes);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static string[] NormalizePrefixes(IEnumerable<string>? prefixes)
    {
        var normalized = (prefixes ?? new[] { "/" })
            .Select(prefix => prefix?.Trim() ?? string.Empty)
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? ["/"] : normalized;
    }
}
