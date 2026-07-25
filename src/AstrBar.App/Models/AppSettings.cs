namespace AstrBar.Models;

public sealed class AppSettings
{
    public string ProtocolVersion { get; set; } = string.Empty;
    public bool IsInitialized { get; set; }
    public bool UseEmbeddedSshTunnel { get; set; } = true;
    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = "root";
    public string SshHostKeyFingerprint { get; set; } = string.Empty;
    public string AstrBotRemoteHost { get; set; } = "127.0.0.1";

    // Kept under the old property name so v0.3.x settings migrate without data loss.
    // In v1 this is the AstrBar Protocol gateway port, not the default WebChat port.
    public int AstrBotRemotePort { get; set; } = 6190;
    public int LocalForwardPort { get; set; } = 6190;
    public bool AutoReconnectTunnel { get; set; } = true;
    public bool AutoReconnectProtocol { get; set; } = true;

    // Kept for v0.3.x settings compatibility. It now points at the AstrBar gateway.
    public string BaseUrl { get; set; } = "http://127.0.0.1:6190";
    public string Username { get; set; } = "astrbar-local";
    public string SessionId { get; set; } = "astrbar-main";
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string WakePrefix { get; set; } = string.Empty;
    public string[] CommandPrefixes { get; set; } = ["/"];

    public string ThemeId { get; set; } = "violet";
    public string OrbColorId { get; set; } = "follow";

    public bool NotifyOnComplete { get; set; } = true;
    public bool NotifyProactiveMessages { get; set; } = true;
    public bool NotifyErrors { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public int LongTaskThresholdSeconds { get; set; } = 15;

    public bool StartWithWindows { get; set; }
    public bool KeepPopupTopmost { get; set; } = true;
    public bool OrbSnapToEdge { get; set; } = true;
    public bool OrbPositionSaved { get; set; }
    public double OrbLeft { get; set; }
    public double OrbTop { get; set; }
}
