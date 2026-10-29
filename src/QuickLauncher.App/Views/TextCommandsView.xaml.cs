using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class TextCommandsView : UserControl
{
    private List<CommandRow> _rows = new();

    private sealed class CommandRow
    {
        public TextCommand Command { get; set; } = new();
        public string Abbreviation => Command.Abbreviation + (string.IsNullOrEmpty(Command.TriggerChar) ? "" : $" ({Command.TriggerChar})");
        public string ActionName { get; set; } = "";
    }

    public TextCommandsView()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        if (App.Store is null) return;
        var actions = App.Store.LoadActions().ToDictionary(a => a.Id, a => a.Name);
        _rows = App.Store.LoadTextCommands().Select(c => new CommandRow
        {
            Command = c,
            ActionName = actions.TryGetValue(c.ActionId, out var name) ? name : "（动作已删除）"
        }).ToList();
        CommandsList.ItemsSource = _rows;
    }

    private void NewCommandButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TextCommandEditorWindow(new TextCommand()) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            ((App)Application.Current).RefreshTextCommands();
            Refresh();
        }
    }

    private void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsList.SelectedItem is not CommandRow row) return;
        if (MessageBox.Show($"确定删除指令「{row.Command.Abbreviation}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Store!.DeleteTextCommand(row.Command.Id);
            ((App)Application.Current).RefreshTextCommands();
            Refresh();
        }
    }
}
