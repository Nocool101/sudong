using System;
using System.Windows;
using System.Windows.Controls;

namespace QuickLauncher.App.Views;

/// <summary>
/// 添加动作菜单的选择结果（对标 Quicker 的"添加动作"菜单）。
/// </summary>
public enum AddActionChoice
{
    Cancel,
    /// <summary>新建基础动作（默认打开网址，可在编辑器中改类型）</summary>
    NewBasic,
    /// <summary>新建组合动作</summary>
    NewComposite,
    /// <summary>启动软件（RunProgram）</summary>
    NewRunProgram,
    /// <summary>打开文件（OpenFile）</summary>
    NewOpenFile,
    /// <summary>打开文件夹（OpenFolder）</summary>
    NewOpenFolder,
    /// <summary>运行命令（RunProgram，预置 cmd.exe /c）</summary>
    NewRunCommand,
    /// <summary>打开网址（OpenUrl）</summary>
    NewOpenUrl,
    /// <summary>模拟按键（SendKeys）</summary>
    NewSendKeys,
    /// <summary>发送文本（SendText）</summary>
    NewSendText,
    /// <summary>执行脚本（组合动作 + 预置 C# 脚本步骤）</summary>
    NewScript,
    /// <summary>加载动作页（SwitchPage）</summary>
    NewSwitchPage,
    /// <summary>从 JSON 文件导入动作</summary>
    ImportAction,
    /// <summary>从已有动作选择</summary>
    PickExisting,
}

public partial class AddActionDialog : Window
{
    public AddActionChoice Choice { get; private set; } = AddActionChoice.Cancel;

    public AddActionDialog()
    {
        InitializeComponent();
        // 小分辨率屏幕保护：不超过可用屏幕高度（内容可滚动）
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string name }
            || !Enum.TryParse<AddActionChoice>(name, ignoreCase: true, out var choice))
            return;

        Choice = choice;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = AddActionChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
