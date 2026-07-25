using System.Windows;
using System.Windows.Controls;
using AstrBar.Models;
using AstrBar.Services;

namespace AstrBar.Views;

public partial class SetupWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly AstrBarProtocolClient _protocolClient;
    private readonly SshTunnelService _sshTunnelService;
    private readonly ThemeService _themeService;
    private string _verifiedFingerprint = string.Empty;

    public SetupWindow(
        SettingsService settingsService,
        CredentialService credentialService,
        AstrBarProtocolClient protocolClient,
        SshTunnelService sshTunnelService,
        ThemeService themeService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _credentialService = credentialService;
        _protocolClient = protocolClient;
        _sshTunnelService = sshTunnelService;
        _themeService = themeService;

        ThemeInput.ItemsSource = _themeService.Themes;
        OrbColorInput.ItemsSource = _themeService.OrbColors;
        LoadValues();
    }

    private void LoadValues()
    {
        var settings = _settingsService.Current;
        SshHostInput.Text = settings.SshHost;
        SshPortInput.Text = settings.SshPort.ToString();
        SshUsernameInput.Text = settings.SshUsername;
        SshPasswordInput.Password = _credentialService.LoadSshPassword();
        RemotePortInput.Text = settings.AstrBotRemotePort.ToString();
        LocalPortInput.Text = settings.LocalForwardPort.ToString();
        ApiKeyInput.Password = _credentialService.LoadProtocolToken();
        UsernameInput.Text = settings.Username;
        SessionIdInput.Text = settings.SessionId;
        DeviceNameInput.Text = settings.DeviceName;
        DeviceIdInput.Text = settings.DeviceId;
        _verifiedFingerprint = settings.SshHostKeyFingerprint;
        ThemeInput.SelectedItem = _themeService.GetTheme(settings.ThemeId);
        OrbColorInput.SelectedItem = _themeService.GetOrbColor(settings.OrbColorId);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        StatusText.Text = "正在连接 SSH 服务器…";

        try
        {
            var candidate = BuildSettings();
            var tunnel = await _sshTunnelService.StartAsync(
                candidate,
                SshPasswordInput.Password,
                trustNewHostKey: true);
            _verifiedFingerprint = tunnel.HostKeyFingerprint;
            candidate.SshHostKeyFingerprint = _verifiedFingerprint;
            candidate.BaseUrl = $"http://127.0.0.1:{tunnel.LocalPort}";

            StatusText.Text = "SSH 隧道已建立，正在进行 AstrBar Protocol 握手…";
            await _protocolClient.TestConnectionAsync(
                candidate,
                ApiKeyInput.Password);

            candidate.IsInitialized = true;
            _credentialService.SaveSshPassword(SshPasswordInput.Password);
            _credentialService.SaveProtocolToken(ApiKeyInput.Password);
            _settingsService.Save(candidate);
            _themeService.Apply(candidate);

            StatusText.Text = $"连接成功。主机指纹：{_verifiedFingerprint}";
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"连接失败：{ex.Message}";
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private AppSettings BuildSettings()
    {
        if (string.IsNullOrWhiteSpace(SshHostInput.Text))
        {
            throw new InvalidOperationException("请填写服务器公网 IP 或域名。");
        }
        if (string.IsNullOrWhiteSpace(SshUsernameInput.Text))
        {
            throw new InvalidOperationException("请填写 SSH 用户名。");
        }
        if (string.IsNullOrEmpty(SshPasswordInput.Password))
        {
            throw new InvalidOperationException("请填写 SSH 密码。");
        }
        if (string.IsNullOrWhiteSpace(ApiKeyInput.Password))
        {
            throw new InvalidOperationException("请填写 AstrBar Protocol Token。");
        }
        if (string.IsNullOrWhiteSpace(UsernameInput.Text) ||
            string.IsNullOrWhiteSpace(SessionIdInput.Text) ||
            string.IsNullOrWhiteSpace(DeviceIdInput.Text))
        {
            throw new InvalidOperationException("user_id、session_id 与 device_id 不能为空。");
        }

        var sshPort = ParsePort(SshPortInput.Text, "SSH 端口");
        var remotePort = ParsePort(RemotePortInput.Text, "AstrBar Protocol 端口");
        var localPort = ParsePort(LocalPortInput.Text, "本地端口");
        var old = _settingsService.Current;
        var theme = ThemeInput.SelectedItem as ThemeOption ?? _themeService.Themes[0];
        var orb = OrbColorInput.SelectedItem as OrbColorOption ?? _themeService.OrbColors[0];

        return new AppSettings
        {
            ProtocolVersion = ProtocolEnvelope.CurrentProtocol,
            IsInitialized = true,
            UseEmbeddedSshTunnel = true,
            SshHost = SshHostInput.Text.Trim(),
            SshPort = sshPort,
            SshUsername = SshUsernameInput.Text.Trim(),
            SshHostKeyFingerprint = _verifiedFingerprint,
            AstrBotRemoteHost = "127.0.0.1",
            AstrBotRemotePort = remotePort,
            LocalForwardPort = localPort,
            AutoReconnectTunnel = true,
            BaseUrl = $"http://127.0.0.1:{localPort}",
            Username = UsernameInput.Text.Trim(),
            SessionId = SessionIdInput.Text.Trim(),
            DeviceId = DeviceIdInput.Text.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(DeviceNameInput.Text)
                ? Environment.MachineName
                : DeviceNameInput.Text.Trim(),
            WakePrefix = old.WakePrefix,
            CommandPrefixes = old.CommandPrefixes ?? ["/"],
            ThemeId = theme.Id,
            OrbColorId = orb.Id,
            NotifyOnComplete = old.NotifyOnComplete,
            NotifyProactiveMessages = old.NotifyProactiveMessages,
            NotifyErrors = old.NotifyErrors,
            DoNotDisturb = old.DoNotDisturb,
            LongTaskThresholdSeconds = old.LongTaskThresholdSeconds,
            AutoReconnectProtocol = true,
            StartWithWindows = old.StartWithWindows,
            KeepPopupTopmost = old.KeepPopupTopmost,
            OrbSnapToEdge = old.OrbSnapToEdge,
            OrbPositionSaved = old.OrbPositionSaved,
            OrbLeft = old.OrbLeft,
            OrbTop = old.OrbTop
        };
    }

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value.Trim(), out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{name}必须位于 1 到 65535 之间。");
        }
        return port;
    }

    private void ThemeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var theme = ThemeInput.SelectedItem as ThemeOption ?? _themeService.Themes[0];
        var orb = OrbColorInput.SelectedItem as OrbColorOption ?? _themeService.OrbColors[0];
        _themeService.Apply(new AppSettings
        {
            ThemeId = theme.Id,
            OrbColorId = orb.Id
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
