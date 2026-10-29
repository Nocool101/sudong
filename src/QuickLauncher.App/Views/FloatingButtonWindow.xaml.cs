using System;
using System.Windows;
using System.Windows.Input;
using QuickLauncher.App.Interaction;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class FloatingButtonWindow : Window
{
    private bool _dragging;
    private System.Windows.Point _dragStart;
    private DateTime _pressTime;

    /// <summary>请求显示面板</summary>
    public event Action? ShowPanelRequested;

    /// <summary>请求退出</summary>
    public event Action? CloseRequested;

    public FloatingButtonWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeNoActivate();
    }

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void MakeNoActivate()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
    }

    private void Ball_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _pressTime = DateTime.UtcNow;
        Ball.CaptureMouse();
    }

    private void Ball_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = PointToScreen(e.GetPosition(this));
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
        {
            Left += dx;
            Top += dy;
            _dragStart = current;
        }
    }

    private void Ball_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _dragging;
        _dragging = false;
        Ball.ReleaseMouseCapture();
        if (!wasDragging) return;

        var elapsed = (DateTime.UtcNow - _pressTime).TotalMilliseconds;
        if (elapsed < 400)
            ShowPanelRequested?.Invoke();
    }

    private void Ball_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var showItem = new System.Windows.Controls.MenuItem { Header = "显示面板" };
        showItem.Click += (_, _) => ShowPanelRequested?.Invoke();
        var closeItem = new System.Windows.Controls.MenuItem { Header = "关闭悬浮球" };
        closeItem.Click += (_, _) => CloseRequested?.Invoke();
        menu.Items.Add(showItem);
        menu.Items.Add(closeItem);
        menu.IsOpen = true;
    }
}
