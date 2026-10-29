using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;
using QuickLauncher.Storage;

namespace QuickLauncher.App.Views;

public partial class ActionsManagerView : UserControl
{
    private List<ActionItem> _actions = new();

    public ActionsManagerView()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        if (App.Store is null) return;
        _actions = App.Store.LoadActions().OrderBy(a => a.Name).ToList();
        ActionsList.ItemsSource = null;
        ActionsList.ItemsSource = _actions;
        StatusText.Text = $"共 {_actions.Count} 个动作";
    }

    private ActionItem? Selected => ActionsList.SelectedItem as ActionItem;

    private void NewActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = new ActionItem
        {
            Name = "新动作",
            Kind = ActionKind.Simple,
            SimpleType = "OpenUrl",
            SimpleParam = "https://"
        };
        App.Store!.SaveAction(action);
        OpenEditor(action);
        RefreshList();
    }

    private void EditActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } action)
            OpenEditor(action);
    }

    private void OpenEditor(ActionItem action)
    {
        var editor = new ActionEditorWindow(action);
        editor.Owner = Window.GetWindow(this);
        if (editor.ShowDialog() == true)
            RefreshList();
    }

    private void RunActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } action) return;
        if (App.Executor is null) return;
        StatusText.Text = $"正在运行: {action.Name}";
        var result = App.Executor.Execute(action);
        StatusText.Text = result.Success
            ? $"已运行: {action.Name}（{result.Duration.TotalMilliseconds:F0}ms）"
            : $"运行失败: {result.Error}";
    }

    private void EditPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Store is null) return;
        // 默认编辑全局场景的面板页
        var global = App.Store.LoadScenes().FirstOrDefault(s => s.Type == SceneType.Global);
        if (global?.PanelPageId is not { } pageId)
        {
            MessageBox.Show("未找到全局场景的面板页，请先在场景管理中配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var page = App.Store.LoadPage(pageId);
        if (page is null)
        {
            MessageBox.Show("面板页不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var editor = new PageEditorWindow(page) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
            StatusText.Text = $"面板页已更新: {page.Name}";
    }

    private void DeleteActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } action) return;
        var confirm = MessageBox.Show($"确定删除动作「{action.Name}」？", "确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        App.Store!.DeleteAction(action.Id);
        RefreshList();
    }

    private void ExportActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } action)
        {
            MessageBox.Show("请先选择要导出的动作。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出动作",
            FileName = $"{action.Name}.json",
            Filter = "动作 JSON|*.json|所有文件|*.*",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, Json.Serialize(action));
            StatusText.Text = $"已导出: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportActionButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入动作",
            Filter = "动作 JSON|*.json|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var imported = Json.Deserialize<ActionItem>(System.IO.File.ReadAllText(dlg.FileName));
            if (imported is null || string.IsNullOrEmpty(imported.Name))
            {
                MessageBox.Show("无法解析该文件（不是有效的动作 JSON）。", "导入失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 重新分配 Id，避免与现有动作冲突
            imported.Id = Guid.NewGuid();
            App.Store!.SaveAction(imported);
            RefreshList();
            StatusText.Text = $"已导入: {imported.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败: {ex.Message}", "导入失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ActionsList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } action)
            OpenEditor(action);
    }
}
