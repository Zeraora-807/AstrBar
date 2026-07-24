using AstrBar.Models;
using Renci.SshNet;

namespace AstrBar.Services;

public sealed record TunnelStartResult(
    bool IsConnected,
    string HostKeyFingerprint,
    int LocalPort);

public sealed class TunnelStatusChangedEventArgs : EventArgs
{
    public TunnelStatusChangedEventArgs(string status, bool connected)
    {
        Status = status;
        Connected = connected;
    }

    public string Status { get; }
    public bool Connected { get; }
}

public sealed class SshTunnelService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly CredentialService _credentialService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly Task _monitorTask;

    private SshClient? _client;
    private ForwardedPortLocal? _forwardedPort;
    private bool _disposed;
    private bool _manualStop;

    public SshTunnelService(
        SettingsService settingsService,
        CredentialService credentialService)
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _monitorTask = MonitorAsync(_monitorCancellation.Token);
    }

    public bool IsRunning =>
        _client?.IsConnected == true &&
        _forwardedPort?.IsStarted == true;

    public event EventHandler<TunnelStatusChangedEventArgs>? StatusChanged;

    public Task<TunnelStartResult> StartStoredAsync(
        CancellationToken cancellationToken = default)
    {
        return StartAsync(
            _settingsService.Current,
            _credentialService.LoadSshPassword(),
            trustNewHostKey: false,
            cancellationToken);
    }

    public async Task<TunnelStartResult> StartAsync(
        AppSettings settings,
        string password,
        bool trustNewHostKey,
        CancellationToken cancellationToken = default)
    {
        if (!settings.UseEmbeddedSshTunnel)
        {
            await StopAsync();
            return new TunnelStartResult(false, string.Empty, settings.LocalForwardPort);
        }

        ValidateSettings(settings, password);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _manualStop = false;
            StopCore();
            Publish("正在建立 SSH 隧道…", false);

            var authentication = new PasswordAuthenticationMethod(
                settings.SshUsername,
                password);
            var connectionInfo = new ConnectionInfo(
                settings.SshHost,
                settings.SshPort,
                settings.SshUsername,
                authentication)
            {
                Timeout = TimeSpan.FromSeconds(12)
            };

            var client = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(25)
            };

            string observedFingerprint = string.Empty;
            var fingerprintMismatch = false;
            client.HostKeyReceived += (_, args) =>
            {
                observedFingerprint = "SHA256:" + args.FingerPrintSHA256;
                var expected = settings.SshHostKeyFingerprint?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(expected))
                {
                    args.CanTrust = trustNewHostKey;
                    return;
                }

                args.CanTrust = string.Equals(
                    expected,
                    observedFingerprint,
                    StringComparison.Ordinal);
                fingerprintMismatch = !args.CanTrust;
            };

            try
            {
                await client.ConnectAsync(cancellationToken);
                if (!client.IsConnected)
                {
                    throw new InvalidOperationException("SSH 服务器没有完成连接。");
                }

                var forwardedPort = new ForwardedPortLocal(
                    "127.0.0.1",
                    checked((uint)settings.LocalForwardPort),
                    settings.AstrBotRemoteHost,
                    checked((uint)settings.AstrBotRemotePort));
                client.AddForwardedPort(forwardedPort);
                await Task.Run(forwardedPort.Start, cancellationToken);

                _client = client;
                _forwardedPort = forwardedPort;
                Publish($"SSH 隧道已连接：127.0.0.1:{forwardedPort.BoundPort}", true);

                return new TunnelStartResult(
                    true,
                    observedFingerprint,
                    checked((int)forwardedPort.BoundPort));
            }
            catch
            {
                client.Dispose();
                if (fingerprintMismatch)
                {
                    throw new InvalidOperationException(
                        "SSH 主机指纹发生变化。为防止连接到错误服务器，AstrBar 已拒绝连接。请确认服务器后在设置中重新信任主机。");
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            Publish($"SSH 隧道连接失败：{ex.Message}", false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _manualStop = true;
            StopCore();
            Publish("SSH 隧道已停止", false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(12));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var settings = _settingsService.Current;
                if (_disposed || _manualStop || !settings.IsInitialized ||
                    !settings.UseEmbeddedSshTunnel || !settings.AutoReconnectTunnel ||
                    IsRunning)
                {
                    continue;
                }

                try
                {
                    await StartStoredAsync(cancellationToken);
                }
                catch
                {
                    // The status event already carries the failure. Try again later.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopCore()
    {
        if (_forwardedPort is not null)
        {
            try
            {
                if (_forwardedPort.IsStarted)
                {
                    _forwardedPort.Stop();
                }
            }
            catch
            {
            }
            _forwardedPort.Dispose();
            _forwardedPort = null;
        }

        if (_client is not null)
        {
            try
            {
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }
            }
            catch
            {
            }
            _client.Dispose();
            _client = null;
        }
    }

    private void Publish(string status, bool connected)
    {
        StatusChanged?.Invoke(this, new TunnelStatusChangedEventArgs(status, connected));
    }

    private static void ValidateSettings(AppSettings settings, string password)
    {
        if (string.IsNullOrWhiteSpace(settings.SshHost))
        {
            throw new InvalidOperationException("请填写服务器公网 IP 或域名。");
        }
        if (string.IsNullOrWhiteSpace(settings.SshUsername))
        {
            throw new InvalidOperationException("请填写 SSH 用户名。");
        }
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("请填写 SSH 密码。");
        }
        if (settings.SshPort is < 1 or > 65535 ||
            settings.AstrBotRemotePort is < 1 or > 65535 ||
            settings.LocalForwardPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("端口必须位于 1 到 65535 之间。");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SshTunnelService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitorCancellation.Cancel();
        try
        {
            _monitorTask.GetAwaiter().GetResult();
        }
        catch
        {
        }
        StopCore();
        _monitorCancellation.Dispose();
        _gate.Dispose();
    }
}
