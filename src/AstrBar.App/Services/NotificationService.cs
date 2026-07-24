using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AstrBar.Services;

public sealed class NotificationService : IDisposable
{
    private bool _registered;

    public event Action<string?>? Activated;

    public bool IsAvailable => _registered;

    public void TryRegister()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            // Notifications can fail when the process is elevated, the Windows
            // App SDK runtime is missing, or the OS does not support the API.
            _registered = false;
        }
    }

    public void Show(string title, string body, string? sessionId = null)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open-chat")
                .AddText(title)
                .AddText(body);

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                builder.AddArgument("sessionId", sessionId);
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // A notification failure must not terminate the chat client.
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        string? sessionId = null;

        if (args.Arguments.TryGetValue("sessionId", out var value))
        {
            sessionId = value;
        }

        Activated?.Invoke(sessionId);
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
            // Ignore shutdown-time notification subsystem errors.
        }

        _registered = false;
    }
}
