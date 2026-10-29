using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace QuickLauncher.App.Views;

public partial class SettingsView : UserControl
{
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        LoadSettings();
        _loading = false;
    }

    private void LoadSettings()
    {
        var s = App.Settings;
        var buttons = s.PanelTrigger.TriggerButtons.Count > 0
            ? s.PanelTrigger.TriggerButtons
            : (s.PanelTrigger.MiddleClick ? new List<string> { "Middle" } : new List<string>());
        BtnMiddle.IsChecked = buttons.Contains("Middle");
        BtnX1.IsChecked = buttons.Contains("X1");
        BtnX2.IsChecked = buttons.Contains("X2");
        BtnRight.IsChecked = buttons.Contains("Right");
        BtnLeft.IsChecked = buttons.Contains("Left");
        CtrlClickBox.IsChecked = s.PanelTrigger.CtrlClick;
        ButtonSizeSlider.Value = s.Appearance.PanelButtonSize;
        RunAtStartupBox.IsChecked = s.General.RunAtStartup;
        TrackUsageBox.IsChecked = s.General.TrackUsage;
        PlaySoundsBox.IsChecked = s.Appearance.PlaySounds;
        FloatingButtonBox.IsChecked = s.General.ShowFloatingButton;
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        ButtonSizeText.Text = ((int)ButtonSizeSlider.Value).ToString();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Settings;
        var buttons = new List<string>();
        if (BtnMiddle.IsChecked == true) buttons.Add("Middle");
        if (BtnX1.IsChecked == true) buttons.Add("X1");
        if (BtnX2.IsChecked == true) buttons.Add("X2");
        if (BtnRight.IsChecked == true) buttons.Add("Right");
        if (BtnLeft.IsChecked == true) buttons.Add("Left");
        s.PanelTrigger.TriggerButtons = buttons;
        s.PanelTrigger.MiddleClick = buttons.Contains("Middle");
        s.PanelTrigger.CtrlClick = CtrlClickBox.IsChecked == true;
        s.Appearance.PanelButtonSize = (int)ButtonSizeSlider.Value;
        s.General.RunAtStartup = RunAtStartupBox.IsChecked == true;
        s.General.TrackUsage = TrackUsageBox.IsChecked == true;
        s.Appearance.PlaySounds = PlaySoundsBox.IsChecked == true;
        s.General.ShowFloatingButton = FloatingButtonBox.IsChecked == true;

        ((App)Application.Current).SaveSettings();
        ((App)Application.Current).ApplyFloatingButtonSetting();
        ApplyStartup(s.General.RunAtStartup);
        UpdateLabels();
        SavedText.Text = $"已自动保存 {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>设置/取消开机自启（改为“计划任务·登录时·最高权限”，替代注册表 Run 项）</summary>
    private void ApplyStartup(bool enable)
    {
        var (ok, msg) = QuickLauncher.App.Services.StartupService.Apply(enable);
        if (!ok)
            MessageBox.Show(msg, "开机自启", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ExportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出速动配置",
            Filter = "速动配置备份 (*.zip)|*.zip",
            FileName = $"速动配置_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ((App)Application.Current).ExportConfig(dlg.FileName);
            SavedText.Text = $"已导出配置 {DateTime.Now:HH:mm:ss}";
            MessageBox.Show($"配置已导出到：\n{dlg.FileName}", "速动",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message, "速动",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入速动配置",
            Filter = "速动配置备份 (*.zip)|*.zip"
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "导入将覆盖当前所有配置，并在导入前自动备份原配置，然后自动重启速动。\n\n确定继续吗？",
            "导入配置", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            ((App)Application.Current).ImportConfig(dlg.FileName);
            // 成功后应用会自动重启，通常不会执行到此处
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败：" + ex.Message, "速动",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
