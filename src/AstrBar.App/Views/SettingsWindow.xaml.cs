using System.Windows;
using System.Windows.Controls;
using AstrBar.Models;
using AstrBar.Services;

namespace AstrBar.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly StartupService _startupService;
    private readonly AstrBotClient _astrBotClient;
    private readonly SshTunnelService _sshTunnelService;
    private readonly ThemeService _themeService;
    private string _fingerprint = string.Empty;
    private bool _loading = true;

    public SettingsWindow(
        SettingsService settingsService,
        CredentialService credentialService,
        StartupService startupService,
        AstrBotClient astrBotClient,
        SshTunnelService sshTunnelService,
        ThemeService themeService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _credentialService = credentialService;
        _startupService = startupService;
        _astrBotClient = astrBotClient;
        _sshTunnelService = sshTunnelService;
        _themeService = themeService;

        ThemeInput.ItemsSource = _themeService.Themes;
        OrbColorInput.ItemsSource = _themeService.OrbColors;
        LoadCurrentValues();
        _loading = false;
    }

    private void LoadCurrentValues()
    {
        var settings = _settingsService.Current;
        UseTunnelInput.IsChecked = settings.UseEmbeddedSshTunnel;
        SshHostInput.Text = settings.SshHost;
        SshPortInput.Text = settings.SshPort.ToString();
        SshUsernameInput.Text = settings.SshUsername;
        SshPasswordInput.Password = _credentialService.LoadSshPassword();
        RemotePortInput.Text = settings.AstrBotRemotePort.ToString();
        LocalPortInput.Text = settings.LocalForwardPort.ToString();
        AutoReconnectInput.IsChecked = settings.AutoReconnectTunnel;
        BaseUrlInput.Text = settings.BaseUrl;
        ApiKeyInput.Password = _credentialService.LoadApiKey();
        UsernameInput.Text = settings.Username;
        SessionIdInput.Text = settings.SessionId;
        WakePrefixInput.Text = settings.WakePrefix;
        CommandPrefixesInput.Text = string.Join(",", settings.CommandPrefixes ?? ["/"]);
        NotifyOnCompleteInput.IsChecked = settings.NotifyOnComplete;
        StartWithWindowsInput.IsChecked = settings.StartWithWindows;
        KeepTopmostInput.IsChecked = settings.KeepPopupTopmost;
        OrbSnapInput.IsChecked = settings.OrbSnapToEdge;
        _fingerprint = settings.SshHostKeyFingerprint;
        FingerprintText.Text = string.IsNullOrWhiteSpace(_fingerprint)
            ? "尚未记录，将在下次成功连接时保存"
            : _fingerprint;
        ThemeInput.SelectedItem = _themeService.GetTheme(settings.ThemeId);
        OrbColorInput.SelectedItem = _themeService.GetOrbColor(settings.OrbColorId);
        UpdateTunnelPanels();
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestStatusText.Text = "正在测试连接…";
        try
        {
            var candidate = BuildSettings();
            if (candidate.UseEmbeddedSshTunnel)
            {
                var result = await _sshTunnelService.StartAsync(
                    candidate,
                    SshPasswordInput.Password,
                    trustNewHostKey: string.IsNullOrWhiteSpace(candidate.SshHostKeyFingerprint));
                _fingerprint = result.HostKeyFingerprint;
                candidate.SshHostKeyFingerprint = _fingerprint;
                candidate.BaseUrl = $"http://127.0.0.1:{result.LocalPort}";
                FingerprintText.Text = _fingerprint;
            }

            await _astrBotClient.TestConnectionAsync(
                candidate.BaseUrl,
                ApiKeyInput.Password,
                candidate.Username);
            TestStatusText.Text = "连接成功，chat 与 file scope 均可用。";
        }
        catch (Exception ex)
        {
            TestStatusText.Text = $"连接失败：{ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SaveStatusText.Text = "正在应用…";
        try
        {
            var settings = BuildSettings();
            settings.IsInitialized = true;
            settings.SshHostKeyFingerprint = _fingerprint;

            // Apply the connection first. A typo must not replace the last known-good
            // settings or credentials on disk.
            if (settings.UseEmbeddedSshTunnel)
            {
                var result = await _sshTunnelService.StartAsync(
                    settings,
                    SshPasswordInput.Password,
                    trustNewHostKey: string.IsNullOrWhiteSpace(settings.SshHostKeyFingerprint));
                settings.SshHostKeyFingerprint = result.HostKeyFingerprint;
                settings.BaseUrl = $"http://127.0.0.1:{result.LocalPort}";
                _fingerprint = result.HostKeyFingerprint;
            }
            else
            {
                await _sshTunnelService.StopAsync();
            }

            _credentialService.SaveApiKey(ApiKeyInput.Password);
            _credentialService.SaveSshPassword(
                settings.UseEmbeddedSshTunnel ? SshPasswordInput.Password : string.Empty);
            _settingsService.Save(settings);
            _themeService.Apply(settings);
            _startupService.Apply(settings.StartWithWindows);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = string.Empty;
            MessageBox.Show(this, ex.Message, "无法保存设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private AppSettings BuildSettings()
    {
        var useTunnel = UseTunnelInput.IsChecked == true;
        if (string.IsNullOrWhiteSpace(ApiKeyInput.Password))
        {
            throw new InvalidOperationException("请填写 AstrBot API Key。");
        }
        if (string.IsNullOrWhiteSpace(UsernameInput.Text) || string.IsNullOrWhiteSpace(SessionIdInput.Text))
        {
            throw new InvalidOperationException("username 与 session_id 不能为空。");
        }
        var prefixes = ParseCommandPrefixes();
        if (prefixes.Length == 0)
        {
            throw new InvalidOperationException("至少填写一个插件命令前缀，例如 /。");
        }

        var old = _settingsService.Current;
        var localPort = useTunnel ? ParsePort(LocalPortInput.Text, "本地端口") : old.LocalForwardPort;
        var baseUrl = useTunnel
            ? $"http://127.0.0.1:{localPort}"
            : ValidateBaseUrl(BaseUrlInput.Text);
        var theme = ThemeInput.SelectedItem as ThemeOption ?? _themeService.Themes[0];
        var orb = OrbColorInput.SelectedItem as OrbColorOption ?? _themeService.OrbColors[0];

        if (useTunnel && string.IsNullOrWhiteSpace(SshHostInput.Text))
        {
            throw new InvalidOperationException("请填写服务器公网 IP 或域名。");
        }
        if (useTunnel && string.IsNullOrWhiteSpace(SshUsernameInput.Text))
        {
            throw new InvalidOperationException("请填写 SSH 用户名。");
        }
        if (useTunnel && string.IsNullOrEmpty(SshPasswordInput.Password))
        {
            throw new InvalidOperationException("请填写 SSH 密码。");
        }

        return new AppSettings
        {
            IsInitialized = true,
            UseEmbeddedSshTunnel = useTunnel,
            SshHost = SshHostInput.Text.Trim(),
            SshPort = useTunnel ? ParsePort(SshPortInput.Text, "SSH 端口") : old.SshPort,
            SshUsername = SshUsernameInput.Text.Trim(),
            SshHostKeyFingerprint = _fingerprint,
            AstrBotRemoteHost = "127.0.0.1",
            AstrBotRemotePort = useTunnel ? ParsePort(RemotePortInput.Text, "AstrBot 端口") : old.AstrBotRemotePort,
            LocalForwardPort = localPort,
            AutoReconnectTunnel = AutoReconnectInput.IsChecked == true,
            BaseUrl = baseUrl,
            Username = UsernameInput.Text.Trim(),
            SessionId = SessionIdInput.Text.Trim(),
            WakePrefix = WakePrefixInput.Text.Trim(),
            CommandPrefixes = prefixes,
            ThemeId = theme.Id,
            OrbColorId = orb.Id,
            NotifyOnComplete = NotifyOnCompleteInput.IsChecked == true,
            StartWithWindows = StartWithWindowsInput.IsChecked == true,
            KeepPopupTopmost = KeepTopmostInput.IsChecked == true,
            OrbSnapToEdge = OrbSnapInput.IsChecked == true,
            OrbPositionSaved = old.OrbPositionSaved,
            OrbLeft = old.OrbLeft,
            OrbTop = old.OrbTop
        };
    }

    private string[] ParseCommandPrefixes()
    {
        return CommandPrefixesInput.Text
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value.Trim(), out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{name}必须位于 1 到 65535 之间。");
        }
        return port;
    }

    private static string ValidateBaseUrl(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("AstrBot 地址必须是有效的 HTTP 或 HTTPS URL。");
        }
        return normalized;
    }

    private void UseTunnelInput_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTunnelPanels();
    }

    private void UpdateTunnelPanels()
    {
        if (TunnelSettingsPanel is null || DirectSettingsPanel is null)
        {
            return;
        }
        var useTunnel = UseTunnelInput.IsChecked == true;
        TunnelSettingsPanel.Visibility = useTunnel ? Visibility.Visible : Visibility.Collapsed;
        DirectSettingsPanel.Visibility = useTunnel ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetFingerprintButton_Click(object sender, RoutedEventArgs e)
    {
        _fingerprint = string.Empty;
        FingerprintText.Text = "已清除。下次测试将记录新的主机指纹。";
    }

    private void ThemeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded)
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
        _themeService.Apply(_settingsService.Current);
        DialogResult = false;
        Close();
    }
}
