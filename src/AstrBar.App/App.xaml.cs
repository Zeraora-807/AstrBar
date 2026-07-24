using System.Threading;
using System.Windows;
using AstrBar.Services;
using AstrBar.Views;

namespace AstrBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ChatPopupWindow? _popupWindow;
    private FloatingOrbWindow? _orbWindow;
    private WindowStateService? _windowStateService;
    private TrayIconService? _trayIconService;
    private NotificationService? _notificationService;
    private SettingsService? _settingsService;
    private CredentialService? _credentialService;
    private StartupService? _startupService;
    private AstrBotClient? _astrBotClient;
    private AttachmentService? _attachmentService;
    private SshTunnelService? _sshTunnelService;
    private ThemeService? _themeService;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\AstrBar.SingleInstance",
            createdNew: out var createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            MessageBox.Show(
                "AstrBar 已经在运行，请查看任务栏右下角的托盘图标。",
                "AstrBar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settingsService = new SettingsService();
        _credentialService = new CredentialService();
        _startupService = new StartupService();
        _astrBotClient = new AstrBotClient();
        _attachmentService = new AttachmentService();
        _themeService = new ThemeService();
        _themeService.Apply(_settingsService.Current);
        _sshTunnelService = new SshTunnelService(_settingsService, _credentialService);

        if (!_settingsService.Current.IsInitialized)
        {
            var setup = new SetupWindow(
                _settingsService,
                _credentialService,
                _astrBotClient,
                _sshTunnelService,
                _themeService);
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }
        else if (_settingsService.Current.UseEmbeddedSshTunnel)
        {
            try
            {
                await _sshTunnelService.StartStoredAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"AstrBar 无法自动建立 SSH 隧道：\n{ex.Message}\n\n程序仍会启动，可在设置中修正连接信息并重试。",
                    "AstrBar 连接提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        _settingsService.SettingsChanged += SettingsService_SettingsChanged;

        _notificationService = new NotificationService();
        _notificationService.Activated += OnNotificationActivated;
        _notificationService.TryRegister();

        _popupWindow = new ChatPopupWindow(
            _settingsService,
            _credentialService,
            _startupService,
            _astrBotClient,
            _attachmentService,
            _notificationService,
            _sshTunnelService,
            _themeService);

        _orbWindow = new FloatingOrbWindow();
        _popupWindow.UnreadReplyAvailable += (_, _) => _orbWindow.SetUnread(true);
        _orbWindow.OpenSettingsRequested += (_, _) => Dispatcher.Invoke(OpenSettings);
        _orbWindow.ExitRequested += (_, _) => Dispatcher.Invoke(Shutdown);

        _windowStateService = new WindowStateService(_settingsService);
        _windowStateService.Attach(_popupWindow, _orbWindow);

        _popupWindow.Show();
        _popupWindow.Hide();

        _trayIconService = new TrayIconService(
            showPopup: () => Dispatcher.Invoke(
                () => _windowStateService.ShowPopupNearTray()),
            openSettings: () => Dispatcher.Invoke(OpenSettings),
            testNotification: () => _notificationService.Show(
                "AstrBar 通知测试",
                "Windows 原生通知链路已经工作。",
                _settingsService.Current.SessionId),
            exitApplication: () => Dispatcher.Invoke(Shutdown));
        _trayIconService.Initialize();
    }

    private void SettingsService_SettingsChanged(object? sender, EventArgs e)
    {
        if (_settingsService is null || _themeService is null)
        {
            return;
        }
        _themeService.Apply(_settingsService.Current);
        _popupWindow?.RefreshSettings();
    }

    private void OpenSettings()
    {
        if (_settingsService is null ||
            _credentialService is null ||
            _startupService is null ||
            _astrBotClient is null ||
            _sshTunnelService is null ||
            _themeService is null)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(
            _settingsService,
            _credentialService,
            _startupService,
            _astrBotClient,
            _sshTunnelService,
            _themeService)
        {
            Owner = _popupWindow?.IsVisible == true ? _popupWindow : null
        };
        settingsWindow.ShowDialog();
    }

    private void OnNotificationActivated(string? sessionId)
    {
        Dispatcher.Invoke(() =>
        {
            _windowStateService?.ShowPopupNearTray();
            _popupWindow?.Activate();
        });
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged -= SettingsService_SettingsChanged;
        }

        if (_notificationService is not null)
        {
            _notificationService.Activated -= OnNotificationActivated;
            _notificationService.Dispose();
        }

        _popupWindow?.Dispose();
        _orbWindow?.Close();
        _sshTunnelService?.Dispose();
        _astrBotClient?.Dispose();

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
    }
}
