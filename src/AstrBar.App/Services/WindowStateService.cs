using System.Windows;
using AstrBar.Views;

namespace AstrBar.Services;

public sealed class WindowStateService
{
    private readonly SettingsService _settingsService;
    private ChatPopupWindow? _chatWindow;
    private FloatingOrbWindow? _orbWindow;
    private Rect? _chatBounds;

    public WindowStateService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Attach(ChatPopupWindow chatWindow, FloatingOrbWindow orbWindow)
    {
        _chatWindow = chatWindow;
        _orbWindow = orbWindow;

        chatWindow.CollapseToOrbRequested += (_, _) => CollapseToOrb();
        chatWindow.ToggleRequested += (_, _) => Toggle();
        orbWindow.RestoreRequested += (_, _) => RestoreFromOrb();
        orbWindow.PositionCommitted += (_, position) =>
            _settingsService.SaveOrbPosition(position.Left, position.Top);
    }

    public void ShowPopupNearTray()
    {
        _orbWindow?.SetUnread(false);
        _orbWindow?.Hide();
        _chatWindow?.ShowNearTray();
    }

    public void CollapseToOrb()
    {
        if (_chatWindow is null || _orbWindow is null)
        {
            return;
        }

        if (_chatWindow.IsVisible)
        {
            _chatBounds = new Rect(
                _chatWindow.Left,
                _chatWindow.Top,
                _chatWindow.ActualWidth > 0 ? _chatWindow.ActualWidth : _chatWindow.Width,
                _chatWindow.ActualHeight > 0 ? _chatWindow.ActualHeight : _chatWindow.Height);
        }

        _chatWindow.Hide();
        _orbWindow.ShowAtSavedOrDefault(_settingsService.Current);
    }

    public void RestoreFromOrb()
    {
        if (_chatWindow is null)
        {
            return;
        }

        _orbWindow?.SetUnread(false);
        _orbWindow?.Hide();

        if (_chatBounds is { } bounds)
        {
            _chatWindow.ShowAt(bounds);
        }
        else
        {
            _chatWindow.ShowNearTray();
        }
    }

    public void Toggle()
    {
        if (_orbWindow?.IsVisible == true)
        {
            RestoreFromOrb();
            return;
        }

        if (_chatWindow?.IsVisible == true && _chatWindow.IsActive)
        {
            _chatWindow.Hide();
        }
        else
        {
            ShowPopupNearTray();
        }
    }
}
