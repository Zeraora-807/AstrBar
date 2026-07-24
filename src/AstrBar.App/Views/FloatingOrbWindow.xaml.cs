using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstrBar.Models;
using Forms = System.Windows.Forms;

namespace AstrBar.Views;

public sealed class OrbPositionEventArgs : EventArgs
{
    public OrbPositionEventArgs(double left, double top)
    {
        Left = left;
        Top = top;
    }

    public double Left { get; }
    public double Top { get; }
}

public partial class FloatingOrbWindow : Window
{
    private const double DragThresholdPixels = 5;
    private bool _dragging;
    private bool _moved;
    private System.Drawing.Point _mouseDownScreenPoint;
    private double _windowDownLeft;
    private double _windowDownTop;
    private bool _snapToEdge = true;

    public FloatingOrbWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? RestoreRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<OrbPositionEventArgs>? PositionCommitted;

    public void ShowAtSavedOrDefault(AppSettings settings)
    {
        _snapToEdge = settings.OrbSnapToEdge;
        if (!IsVisible)
        {
            Show();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        if (settings.OrbPositionSaved)
        {
            Left = settings.OrbLeft;
            Top = settings.OrbTop;
        }
        else
        {
            var cursor = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(cursor);
            Left = screen.WorkingArea.Right / dpi.DpiScaleX - Width - 12;
            Top = screen.WorkingArea.Top / dpi.DpiScaleY +
                  (screen.WorkingArea.Height / dpi.DpiScaleY - Height) / 2;
        }

        ClampToVisibleWorkArea();
        Activate();
    }

    public void SetUnread(bool unread)
    {
        UnreadDot.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Orb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _mouseDownScreenPoint = Forms.Cursor.Position;
        _windowDownLeft = Left;
        _windowDownTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void Orb_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = Forms.Cursor.Position;
        var dxPixels = current.X - _mouseDownScreenPoint.X;
        var dyPixels = current.Y - _mouseDownScreenPoint.Y;
        if (Math.Abs(dxPixels) >= DragThresholdPixels ||
            Math.Abs(dyPixels) >= DragThresholdPixels)
        {
            _moved = true;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _windowDownLeft + dxPixels / dpi.DpiScaleX;
        Top = _windowDownTop + dyPixels / dpi.DpiScaleY;
        e.Handled = true;
    }

    private void Orb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        if (_moved)
        {
            if (_snapToEdge)
            {
                SnapAndClamp();
            }
            else
            {
                ClampToVisibleWorkArea();
            }
            PositionCommitted?.Invoke(this, new OrbPositionEventArgs(Left, Top));
        }
        else
        {
            SetUnread(false);
            RestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void SnapAndClamp()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point(
            (int)((Left + Width / 2) * dpi.DpiScaleX),
            (int)((Top + Height / 2) * dpi.DpiScaleY));
        var screen = Forms.Screen.FromPoint(center);

        var workLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var workTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var workRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        var workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        Top = Math.Clamp(Top, workTop + 6, workBottom - Height - 6);
        var distanceToLeft = Math.Abs(Left - workLeft);
        var distanceToRight = Math.Abs(workRight - (Left + Width));
        Left = distanceToLeft <= distanceToRight
            ? workLeft + 6
            : workRight - Width - 6;
    }

    private void ClampToVisibleWorkArea()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var point = new System.Drawing.Point(
            (int)((Left + Width / 2) * dpi.DpiScaleX),
            (int)((Top + Height / 2) * dpi.DpiScaleY));
        var screen = Forms.Screen.FromPoint(point);
        var workLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var workTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var workRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        var workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;

        Left = Math.Clamp(Left, workLeft + 6, workRight - Width - 6);
        Top = Math.Clamp(Top, workTop + 6, workBottom - Height - 6);
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
