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
        Current = Normalize(Load());
        if (Current.IsInitialized)
        {
            Persist(Current);
        }
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        Persist(settings);

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

    private void Persist(AppSettings settings)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var temporaryPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.CommandPrefixes = NormalizePrefixes(settings.CommandPrefixes);
        if (string.IsNullOrWhiteSpace(settings.DeviceId))
        {
            settings.DeviceId = $"windows-{Guid.NewGuid():N}";
        }
        if (string.IsNullOrWhiteSpace(settings.DeviceName))
        {
            settings.DeviceName = Environment.MachineName;
        }

        if (settings.IsInitialized &&
            !string.Equals(
                settings.ProtocolVersion,
                ProtocolEnvelope.CurrentProtocol,
                StringComparison.Ordinal))
        {
            // v1 replaces WebChat/OpenAPI with the native AstrBar platform adapter.
            // Reopen the setup wizard so the user supplies the new gateway token.
            settings.IsInitialized = false;
        }

        // v0.3.x used 6185 for WebChat. An untouched default is migrated to the
        // AstrBar Essential gateway default, while user-selected custom ports remain.
        if (settings.AstrBotRemotePort == 6185 &&
            settings.LocalForwardPort == 6185 &&
            settings.BaseUrl.EndsWith(":6185", StringComparison.OrdinalIgnoreCase))
        {
            settings.AstrBotRemotePort = 6190;
            settings.LocalForwardPort = 6190;
            settings.BaseUrl = "http://127.0.0.1:6190";
        }

        settings.LongTaskThresholdSeconds = Math.Clamp(
            settings.LongTaskThresholdSeconds,
            1,
            3600);
        return settings;
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
