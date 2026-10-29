using System;
using System.Collections.Generic;
using System.Windows;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class SceneEditorWindow : Window
{
    private readonly Scene _scene;

    public SceneEditorWindow(Scene scene)
    {
        InitializeComponent();
        _scene = scene.Clone();
        Title = $"编辑场景 - {scene.Name}";

        NameBox.Text = _scene.Name;
        TypeCombo.SelectedIndex = _scene.Type switch
        {
            SceneType.Global => 0,
            SceneType.Application => 1,
            SceneType.Custom => 2,
            _ => 1
        };
        ProcessBox.Text = _scene.ProcessMatch;
        ClassBox.Text = _scene.WindowClassMatch;
        TitleBox.Text = _scene.WindowTitleMatch;
        EnabledBox.IsChecked = _scene.Enabled;

        // 页面下拉（含"（无）"选项）
        ReloadPageCombo(_scene.PanelPageId);
    }

    /// <summary>刷新页面下拉并选中指定页</summary>
    private void ReloadPageCombo(Guid? selectId)
    {
        var pages = App.Store!.LoadPages();
        var withNone = new List<ActionPage> { new ActionPage { Name = "（无）" } };
        withNone.AddRange(pages);
        PanelPageCombo.ItemsSource = withNone;
        PanelPageCombo.SelectedValue = selectId ?? Guid.Empty;
    }

    /// <summary>为此场景新建专用动作页</summary>
    private void NewPageButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.InputDialog("新动作页名称", $"{_scene.Name} 页");
        if (dlg.ShowDialog() != true) return;
        var name = dlg.ResultText.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var page = new ActionPage { Name = name, Columns = 4 };
        App.Store!.SavePage(page);
        ReloadPageCombo(page.Id);
    }

    /// <summary>编辑所选动作页的磁贴编排</summary>
    private void EditPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (PanelPageCombo.SelectedValue is not Guid pid || pid == Guid.Empty)
        {
            MessageBox.Show("请先选择一个动作页（或用「＋ 新建页」创建专用页）。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var page = App.Store!.LoadPage(pid);
        if (page is null) return;

        var editor = new Views.PageEditorWindow(page) { Owner = this };
        if (editor.ShowDialog() == true)
            ReloadPageCombo(pid); // 名称等可能变化，刷新下拉
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _scene.Name = NameBox.Text.Trim();
        _scene.Type = TypeCombo.SelectedIndex switch
        {
            0 => SceneType.Global,
            1 => SceneType.Application,
            2 => SceneType.Custom,
            _ => SceneType.Application
        };
        _scene.ProcessMatch = ProcessBox.Text.Trim();
        _scene.WindowClassMatch = ClassBox.Text.Trim();
        _scene.WindowTitleMatch = TitleBox.Text.Trim();
        _scene.Enabled = EnabledBox.IsChecked == true;

        _scene.PanelPageId = PanelPageCombo.SelectedValue is Guid pid && pid != Guid.Empty ? pid : null;

        App.Store!.SaveScene(_scene);
        ((App)Application.Current).RefreshSceneMatcher(); // 面板立即识别场景变更
        DialogResult = true;
        Close();
    }
}
