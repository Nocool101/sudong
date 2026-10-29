using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace QuickLauncher.App.Views;

public partial class VariablePickerDialog : Window
{
    public string? SelectedVariable { get; private set; }

    public VariablePickerDialog(List<string> variables)
    {
        InitializeComponent();
        // 小分辨率屏幕保护：窗口不超过可用屏幕高度
        MaxHeight = SystemParameters.PrimaryScreenHeight - 60;
        VarsList.ItemsSource = variables;
        if (variables.Count > 0)
            VarsList.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void VarsList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (VarsList.SelectedItem is string name)
        {
            SelectedVariable = name;
            DialogResult = true;
            Close();
        }
    }
}
