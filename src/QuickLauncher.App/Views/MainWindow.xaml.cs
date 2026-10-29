using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.App.Views;

namespace QuickLauncher.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        NavActions_Click(this, new RoutedEventArgs());
        Closing += MainWindow_Closing;
    }

    /// <summary>真正退出（托盘菜单调用）</summary>
    public void AllowCloseAndExit()
    {
        _allowClose = true;
        Close();
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        // 关闭主窗口 = 最小化到托盘，保持后台运行
        e.Cancel = true;
        Hide();
    }

    private void SetActiveNav(Button active)
    {
        // 选中态由 NavButton 样式中 Tag="Active" 触发器渲染（渐变底 + 左竖条 + 白字）
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
