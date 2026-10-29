using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class PageEditorWindow : Window
{
    private readonly ActionPage _page;
    private List<ActionItem> _actions = new();
    private const int CellWidth = 110;
    private const int CellHeight = 86;

    public PageEditorWindow(ActionPage page)
    {
        InitializeComponent();
        _page = page.Clone();
        Title = $"编辑动作页 - {page.Name}";
        NameBox.Text = _page.Name;
        ColumnsBox.Text = Math.Max(1, _page.Columns).ToString();
        var rows = Math.Max(1, (_page.Cells.Count + Math.Max(1, _page.Columns) - 1) / Math.Max(1, _page.Columns));
        RowsBox.Text = _page.Rows > 0 ? _page.Rows.ToString() : rows.ToString();
        Loaded += (_, _) => RenderGrid();
    }

    private int Columns => int.TryParse(ColumnsBox.Text, out var c) && c > 0 ? c : 4;
    private int Rows => int.TryParse(RowsBox.Text, out var r) && r > 0 ? r : 2;

    private void LayoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CellsGrid is null) return;
        RenderGrid();
    }

    private void RenderGrid()
    {
        if (CellsGrid is null || !IsLoaded) return;
        _actions = App.Store!.LoadActions();

        var cols = Columns;
        var rows = Rows;
        CellsGrid.Columns = cols;
        CellsGrid.Children.Clear();

        var total = cols * rows;
        for (int i = 0; i < total; i++)
        {
            var cell = _page.Cells.FirstOrDefault(c => c.Index == i);
            var action = cell is not null ? _actions.FirstOrDefault(a => a.Id == cell.ActionId) : null;
            CellsGrid.Children.Add(CreateCell(i, action));
        }
    }

    private Button CreateCell(int index, ActionItem? action)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("CellButton"),
            Width = CellWidth,
            Height = CellHeight,
            Tag = index
        };

        if (action is null)
        {
            btn.Content = new TextBlock
            {
                Text = "＋",
                FontSize = 22,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x77, 0x77, 0x77))
            };
        }
        else
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(PanelWindow.CreateIconElement(action.Icon, 26));
            panel.Children.Add(new TextBlock
            {
                Text = action.Name,
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 92,
                TextAlignment = TextAlignment.Center
            });
            btn.Content = panel;
            btn.ToolTip = action.Description;
        }

        btn.Click += CellButton_Click;
        return btn;
    }

    private void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        var picker = new ActionPickerDialog { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedAction is { } action)
        {
            var cell = _page.Cells.FirstOrDefault(c => c.Index == index);
            if (cell is null)
            {
                cell = new ActionCell { Index = index };
                _page.Cells.Add(cell);
            }
            cell.ActionId = action.Id;
            RenderGrid();
        }
    }

    private void CellsGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 找到鼠标下的格子按钮
        var element = e.OriginalSource as FrameworkElement;
        while (element is not null and not Button)
            element = element.Parent as FrameworkElement;
        if (element is Button { Tag: int index })
        {
            _page.Cells.RemoveAll(c => c.Index == index);
            RenderGrid();
            e.Handled = true;
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定清空所有格子？", "确认", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _page.Cells.Clear();
            RenderGrid();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _page.Name = NameBox.Text.Trim();
        _page.Columns = Columns;
        _page.Rows = Rows;
        App.Store!.SavePage(_page);
        DialogResult = true;
        Close();
    }
}
