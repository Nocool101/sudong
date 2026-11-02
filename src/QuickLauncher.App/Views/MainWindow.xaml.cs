using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using QuickLauncher.App.Themes;

namespace QuickLauncher.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        NavActions_Click(this, new RoutedEventArgs());
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    /// <summary>真正退出（托盘菜单调用）</summary>
    public void AllowCloseAndExit()
    {
        _allowClose = true;
        // 静默启动（开机自启）时主窗口从未显示过；此时用 Application.Shutdown()
        // 更可靠（Shutdown 会关闭所有窗口并走 OnExit 释放资源）。
        if (!IsVisible) Application.Current?.Shutdown();
        else Close();
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        // 关闭主窗口 = 最小化到托盘，保持后台运行
        e.Cancel = true;
        Hide();
    }

    /// <summary>窗口句柄就绪后开启系统毛玻璃背板（Acrylic/Mica）</summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        try
        {
            WindowBackdrop.TryApply(this, CurrentTint());
        }
        catch
        {
            // 没有毛玻璃背板也不影响使用：窗口自身底色已是半透明玻璃色
        }
    }

    /// <summary>主题切换后重新应用背板（Win10 路径的磨砂颜色随主题变化）</summary>
    private void OnThemeChanged(bool isDark)
    {
        Dispatcher.BeginInvoke(new Action(ApplyBackdrop));
    }

    private static System.Windows.Media.Color CurrentTint()
        => ThemeManager.IsDark
            ? System.Windows.Media.Color.FromArgb(0x99, 0x0A, 0x0D, 0x15)
            : System.Windows.Media.Color.FromArgb(0x99, 0xF2, 0xF6, 0xFC);

    /// <summary>最大化/还原切换：同步窗口圆角与标题栏图标</summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;

        // 最大化时去掉圆角，否则窗口内容会被圆角区域裁掉
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
            chrome.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);
        RootPanel.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);

        if (MaximizeButton is { } btn)
        {
            var icon = btn.Content as Views.IconView;
            if (icon is not null) icon.Kind = maximized ? "Restore" : "Maximize";
            btn.ToolTip = maximized ? "向下还原" : "最大化";
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SetActiveNav(Button active)
    {
        // 选中态由 NavButton 样式中 Tag="Active" 触发器渲染（渐变底 + 左竖条 + 高亮文字）
        foreach (var btn in new[] { NavActions, NavScenes, NavCommands, NavHotkeys, NavSettings })
            btn.Tag = btn == active ? "Active" : null;
    }

    private void NavActions_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavActions);
        ContentArea.Content = new ActionsManagerView();
    }

    private void NavScenes_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavScenes);
        ContentArea.Content = new ScenesManagerView();
    }

    private void NavCommands_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavCommands);
        ContentArea.Content = new TextCommandsView();
    }

    private void NavHotkeys_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavHotkeys);
        ContentArea.Content = new HotkeysView();
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavSettings);
        ContentArea.Content = new SettingsView();
    }
}
