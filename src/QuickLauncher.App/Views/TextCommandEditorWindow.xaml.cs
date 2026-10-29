using System;
using System.Windows;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class TextCommandEditorWindow : Window
{
    private readonly TextCommand _command;

    public TextCommandEditorWindow(TextCommand command)
    {
        InitializeComponent();
        _command = command.Clone();

        AbbrBox.Text = _command.Abbreviation;
        TriggerBox.Text = _command.TriggerChar;
        EnabledBox.IsChecked = _command.Enabled;

        ActionCombo.ItemsSource = App.Store!.LoadActions();
        ActionCombo.SelectedValue = _command.ActionId != Guid.Empty ? _command.ActionId : null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var abbr = AbbrBox.Text.Trim();
        if (string.IsNullOrEmpty(abbr))
        {
            MessageBox.Show("缩写词不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ActionCombo.SelectedValue is not Guid actionId)
        {
            MessageBox.Show("请选择触发的动作。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _command.Abbreviation = abbr;
        _command.ActionId = actionId;
        _command.TriggerChar = TriggerBox.Text.Trim();
        _command.Enabled = EnabledBox.IsChecked == true;

        App.Store!.SaveTextCommand(_command);
        DialogResult = true;
        Close();
    }
}
