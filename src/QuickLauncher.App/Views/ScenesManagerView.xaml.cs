using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class ScenesManagerView : UserControl
{
    private List<SceneRow> _rows = new();

    private sealed class SceneRow
    {
        public Scene Scene { get; set; } = new();
        public string Name => Scene.Name;
        public string PageDescription { get; set; } = "";
        public string MatchDescription => Scene.Type switch
        {
            SceneType.Global => "全局场景（任何程序）",
            SceneType.Application => $"进程: {Scene.ProcessMatch}",
            SceneType.Custom => $"自定义: {Scene.ProcessMatch} {Scene.WindowClassMatch} {Scene.WindowTitleMatch}",
            _ => ""
        };
    }

    public ScenesManagerView()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        if (App.Store is null) return;
        var pages = App.Store.LoadPages().ToDictionary(p => p.Id, p => p.Name);
        _rows = App.Store.LoadScenes().Select(s => new SceneRow
        {
            Scene = s,
            PageDescription = s.PanelPageId is { } pid && pages.TryGetValue(pid, out var n)
                ? $"磁贴页: {n}"
                : "磁贴页: （未配置，回落全局）"
        }).ToList();
        ScenesList.ItemsSource = _rows;
    }

    private SceneRow? Selected => ScenesList.SelectedItem as SceneRow;

    private void NewSceneButton_Click(object sender, RoutedEventArgs e)
    {
        // 不预填 ProcessMatch="*"："*"会匹配所有程序，一旦配了页面就会漏到所有程序的下区，
        // 破坏“专用磁贴只在该程序出现”的隔离性。留空交由用户在场景编辑器里填写具体进程名。
        var scene = new Scene { Name = "新场景", Type = SceneType.Application, ProcessMatch = "" };
        App.Store!.SaveScene(scene);
        ((App)Application.Current).RefreshSceneMatcher();
        OpenEditor(scene);
        Refresh();
    }

    private void EditSceneButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            OpenEditor(row.Scene);
    }

    private void OpenEditor(Scene scene)
    {
        var editor = new SceneEditorWindow(scene) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
            Refresh();
    }

    private void DeleteSceneButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (row.Scene.Type == SceneType.Global)
        {
            MessageBox.Show("全局场景不能删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"确定删除场景「{row.Scene.Name}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Store!.DeleteScene(row.Scene.Id);
            ((App)Application.Current).RefreshSceneMatcher();
            Refresh();
        }
    }

    private void ScenesList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
            OpenEditor(row.Scene);
    }
}
