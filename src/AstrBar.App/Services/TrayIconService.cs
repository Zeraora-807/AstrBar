using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace AstrBar.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Action _showPopup;
    private readonly Action _openSettings;
    private readonly Action _testNotification;
    private readonly Action _exitApplication;
    private Forms.NotifyIcon? _notifyIcon;

    public TrayIconService(
        Action showPopup,
        Action openSettings,
        Action testNotification,
        Action exitApplication)
    {
        _showPopup = showPopup;
        _openSettings = openSettings;
        _testNotification = testNotification;
        _exitApplication = exitApplication;
    }

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 AstrBar", null, (_, _) => Dispatch(_showPopup));
        menu.Items.Add("设置", null, (_, _) => Dispatch(_openSettings));
        menu.Items.Add("测试 Windows 通知", null, (_, _) => Dispatch(_testNotification));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatch(_exitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "AstrBar",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = LoadIcon()
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatch(_showPopup);
            }
        };
    }

    private static Drawing.Icon LoadIcon()
    {
        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "astrbar.ico");

        try
        {
            return File.Exists(iconPath)
                ? new Drawing.Icon(iconPath)
                : Drawing.SystemIcons.Application;
        }
        catch
        {
            return Drawing.SystemIcons.Application;
        }
    }

    private static void Dispatch(Action action)
    {
        WpfApplication.Current.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }
}
