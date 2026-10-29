using System;
using System.Linq;
using System.Windows;
using QuickLauncher.Models;

namespace QuickLauncher.App.Views;

public partial class HotkeyEditorWindow : Window
{
    private readonly HotkeyCommand _command;

    public HotkeyEditorWindow(HotkeyCommand command)
    {
        InitializeComponent();
        _command = command.Clone();

        // 引导键
        LeaderCombo.SelectedIndex = _command.LeaderKey switch
        {
            "CapsLock" => 0,
            "Space" => 1,
            "Tab" => 2,
            "`" => 3,
            "Enter" => 4,
            "Esc" => 5,
            _ => 0
        };

        // 动作键候选
        ActionCombo.Items.Add("（无 - 单独按引导键）");
        foreach (var k in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" })
            ActionCombo.Items.Add(k);
        ActionCombo.SelectedIndex = string.IsNullOrEmpty(_command.ActionKey)
            ? 0
            : ActionCombo.Items.IndexOf(_command.ActionKey.ToUpperInvariant());

        // 修饰键
        ModifierCombo.SelectedIndex = _command.Modifiers switch
        {
            "Ctrl" => 1,
            "Alt" => 2,
            "Shift" => 3,
            _ => 0
        };

        ActionTargetCombo.ItemsSource = App.Store!.LoadActions();
        ActionTargetCombo.SelectedValue = _command.ActionId != Guid.Empty ? _command.ActionId : null;

        EnabledBox.IsChecked = _command.Enabled;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _command.LeaderKey = LeaderCombo.SelectedIndex switch
        {
            0 => "CapsLock",
            1 => "Space",
            2 => "Tab",
            3 => "`",
            4 => "Enter",
            5 => "Esc",
            _ => "CapsLock"
        };
        _command.ActionKey = ActionCombo.SelectedIndex > 0 ? ActionCombo.SelectedItem?.ToString() ?? "" : "";
        _command.Modifiers = ModifierCombo.SelectedIndex switch
        {
            1 => "Ctrl",
            2 => "Alt",
            3 => "Shift",
            _ => ""
        };
        _command.Enabled = EnabledBox.IsChecked == true;

        if (ActionTargetCombo.SelectedValue is not Guid actionId)
        {
            MessageBox.Show("请选择执行的动作。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _command.ActionId = actionId;

        App.Store!.SaveHotkeyCommand(_command);
        DialogResult = true;
        Close();
    }
}
