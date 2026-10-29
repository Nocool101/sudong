using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Engine;

namespace QuickLauncher.App.Views;

public partial class StepPickerWindow : Window
{
    private sealed class Node
    {
        public string Display { get; set; } = "";
        public string? StepType { get; set; }
        public List<Node> Children { get; set; } = new();
    }

    public string? SelectedStepType { get; private set; }

    public StepPickerWindow()
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        BuildTree("");
    }

    private void BuildTree(string keyword)
    {
        var registry = StepRegistry.Default;
        var categories = registry.Categories;
        var roots = new List<Node>();

        foreach (var category in categories)
        {
            var steps = registry.GetByCategory(category)
                .Where(m => string.IsNullOrEmpty(keyword)
                    || m.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || m.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || m.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (steps.Count == 0) continue;

            var root = new Node { Display = category };
            foreach (var m in steps)
                root.Children.Add(new Node { Display = m.DisplayName, StepType = m.Type });
            roots.Add(root);
        }

        CategoryTree.ItemsSource = roots;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => BuildTree(SearchBox.Text.Trim());

    private Node? SelectedNode
    {
        get
        {
            if (CategoryTree.SelectedItem is Node { StepType: not null } node)
                return node;
            return null;
        }
    }

    private void CategoryTree_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (SelectedNode is null)
        {
            MessageBox.Show("请选择一个步骤类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedStepType = SelectedNode.StepType;
        DialogResult = true;
        Close();
    }
}
