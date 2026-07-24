using System.Windows;
using System.Windows.Controls;
using AstrBar.Models;
using AstrBar.Services;

namespace AstrBar.Views;

public partial class SetupWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly AstrBotClient _astrBotClient;
    private readonly SshTunnelService _sshTunnelService;
    private readonly ThemeService _themeService;
    private string _verifiedFingerprint = string.Empty;

    public SetupWindow(
        SettingsService settingsService,
        CredentialService credentialService,
        AstrBotClient astrBotClient,
        SshTunnelService sshTunnelService,
        ThemeService themeService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _credentialService = credentialService;
        _astrBotClient = astrBotClient;
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
        ApiKeyInput.Password = _credentialService.LoadApiKey();
        UsernameInput.Text = settings.Username;
        SessionIdInput.Text = settings.SessionId;
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

            StatusText.Text = "SSH 隧道已建立，正在检查 AstrBot OpenAPI…";
            await _astrBotClient.TestConnectionAsync(
                candidate.BaseUrl,
                ApiKeyInput.Password,
                candidate.Username);

            candidate.IsInitialized = true;
            _credentialService.SaveSshPassword(SshPasswordInput.Password);
            _credentialService.SaveApiKey(ApiKeyInput.Password);
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
            throw new InvalidOperationException("请填写 AstrBot API Key。");
        }
        if (string.IsNullOrWhiteSpace(UsernameInput.Text) ||
            string.IsNullOrWhiteSpace(SessionIdInput.Text))
        {
            throw new InvalidOperationException("username 与 session_id 不能为空。");
        }

        var sshPort = ParsePort(SshPortInput.Text, "SSH 端口");
        var remotePort = ParsePort(RemotePortInput.Text, "AstrBot 端口");
        var localPort = ParsePort(LocalPortInput.Text, "本地端口");
        var old = _settingsService.Current;
        var theme = ThemeInput.SelectedItem as ThemeOption ?? _themeService.Themes[0];
        var orb = OrbColorInput.SelectedItem as OrbColorOption ?? _themeService.OrbColors[0];

        return new AppSettings
        {
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
            WakePrefix = old.WakePrefix,
            CommandPrefixes = old.CommandPrefixes ?? ["/"],
            ThemeId = theme.Id,
            OrbColorId = orb.Id,
            NotifyOnComplete = old.NotifyOnComplete,
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
