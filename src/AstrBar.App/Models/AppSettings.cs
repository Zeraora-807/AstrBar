namespace AstrBar.Models;

public sealed class AppSettings
{
    public bool IsInitialized { get; set; }
    public bool UseEmbeddedSshTunnel { get; set; } = true;
    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = "root";
    public string SshHostKeyFingerprint { get; set; } = string.Empty;
    public string AstrBotRemoteHost { get; set; } = "127.0.0.1";
    public int AstrBotRemotePort { get; set; } = 6185;
    public int LocalForwardPort { get; set; } = 6185;
    public bool AutoReconnectTunnel { get; set; } = true;

    public string BaseUrl { get; set; } = "http://127.0.0.1:6185";
    public string Username { get; set; } = "astrbar-local";
    public string SessionId { get; set; } = "astrbar-main";
    public string WakePrefix { get; set; } = string.Empty;
    public string[] CommandPrefixes { get; set; } = ["/"];

    public string ThemeId { get; set; } = "violet";
    public string OrbColorId { get; set; } = "follow";

    public bool NotifyOnComplete { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool KeepPopupTopmost { get; set; } = true;
    public bool OrbSnapToEdge { get; set; } = true;
    public bool OrbPositionSaved { get; set; }
    public double OrbLeft { get; set; }
    public double OrbTop { get; set; }
}
