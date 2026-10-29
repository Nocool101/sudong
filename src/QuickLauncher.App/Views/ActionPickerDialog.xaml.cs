using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class ActionPickerDialog : Window
{
    private List<ActionItem> _all = new();
    public ActionItem? SelectedAction { get; private set; }

    public ActionPickerDialog()
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        _all = App.Store!.LoadActions().OrderBy(a => a.Name).ToList();
        ApplyFilter("");
    }

    private void ApplyFilter(string keyword)
    {
        var list = string.IsNullOrWhiteSpace(keyword)
            ? _all
            : _all.Where(a => a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || a.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        ActionsList.ItemsSource = list;
        if (list.Count > 0) ActionsList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text.Trim());

    private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();

    private void ActionsList_MouseDoubleClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ActionsList.SelectedItem is ActionItem action)
        {
            SelectedAction = action;
            DialogResult = true;
            Close();
        }
    }
}
