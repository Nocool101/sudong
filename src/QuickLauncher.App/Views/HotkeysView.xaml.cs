using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class HotkeysView : UserControl
{
    private List<HotkeyRow> _rows = new();

    private sealed class HotkeyRow
    {
        public HotkeyCommand Command { get; set; } = new();
        public string Keys => string.IsNullOrEmpty(Command.ActionKey)
            ? $"{Command.LeaderKey}（单独按）"
            : $"{Command.LeaderKey} → {Command.Modifiers}+{Command.ActionKey}".Replace("+", " + ");
        public string ActionName { get; set; } = "";
    }

    public HotkeysView()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        if (App.Store is null) return;
        var actions = App.Store.LoadActions().ToDictionary(a => a.Id, a => a.Name);
        _rows = App.Store.LoadHotkeyCommands().Select(c => new HotkeyRow
        {
            Command = c,
            ActionName = actions.TryGetValue(c.ActionId, out var name) ? name : "（动作已删除）"
        }).ToList();
        HotkeysList.ItemsSource = _rows;
    }

    private HotkeyRow? Selected => HotkeysList.SelectedItem as HotkeyRow;

    private void NewHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new HotkeyEditorWindow(new HotkeyCommand { LeaderKey = "CapsLock" })
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() == true)
        {
            ((App)Application.Current).RefreshHotkeyCommands();
            Refresh();
        }
    }

    private void DeleteHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        if (MessageBox.Show($"确定删除扩展热键「{row.Keys}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Store!.DeleteHotkeyCommand(row.Command.Id);
            ((App)Application.Current).RefreshHotkeyCommands();
            Refresh();
        }
    }

    private void HotkeysList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row)
        {
            var editor = new HotkeyEditorWindow(row.Command) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                ((App)Application.Current).RefreshHotkeyCommands();
                Refresh();
            }
        }
    }
}
