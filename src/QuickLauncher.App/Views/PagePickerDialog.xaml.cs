using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

/// <summary>动作页选择对话框（用于"加载动作页"动作）</summary>
public partial class PagePickerDialog : Window
{
    private List<ActionPage> _all = new();

    public ActionPage? SelectedPage { get; private set; }

    public PagePickerDialog()
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        _all = App.Store!.LoadPages().OrderBy(p => p.Name).ToList();
        ApplyFilter("");
    }

    private void ApplyFilter(string keyword)
    {
        var list = string.IsNullOrWhiteSpace(keyword)
            ? _all
            : _all.Where(p => p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        PagesList.ItemsSource = list;
        if (list.Count > 0) PagesList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text.Trim());

    private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();

    private void PagesList_MouseDoubleClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (PagesList.SelectedItem is ActionPage page)
        {
            SelectedPage = page;
            DialogResult = true;
            Close();
        }
    }
}
