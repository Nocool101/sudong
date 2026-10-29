using System;
using System.Collections.Generic;

namespace QuickLauncher.Services;

/// <summary>
/// 通知服务（Toast 提示）
/// </summary>
public interface INotificationService
{
    void Show(string message, string level = "info");
}

/// <summary>
/// 对话框服务
/// </summary>
public interface IDialogService
{
    /// <summary>提示输入。取消返回 null。</summary>
    string? Prompt(string prompt, string defaultValue = "", bool multiline = false);

    /// <summary>列表选择。取消返回 (null, -1)。</summary>
    (string? value, int index) Select(string prompt, List<string> options);
}

/// <summary>
/// 面板 UI 服务（加载页面、显示面板等，由 App 层实现）
/// </summary>
public interface IPanelService
{
    void ShowPanel(int mouseX, int mouseY);
    void HidePanel();
    bool IsPanelOpen { get; }

    /// <summary>是否正在进行磁贴拖放等交互（拖放期间交互控制器应暂停自动关闭/触发）</summary>
    bool IsInteracting { get; }
    /// <summary>获取面板窗口矩形（屏幕坐标），未打开返回 null</summary>
    (int x, int y, int w, int h)? GetPanelRect();

    /// <summary>面板成功显示时触发（用于最小存活时间保护）</summary>
    event Action? PanelShown;

    /// <summary>切换面板当前显示的动作页（加载动作页动作/步骤调用）</summary>
    void SwitchToPage(Guid pageId);

    /// <summary>当前是否挂起（全局禁用）</summary>
    bool IsSuspended { get; set; }
}
