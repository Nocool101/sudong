using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace QuickLauncher.App.Views;

public partial class SelectDialog : Window
{
    public string? SelectedValue { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public SelectDialog(string prompt, List<string> options)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        OptionsList.ItemsSource = options;
        if (options.Count > 0)
            OptionsList.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void OptionsList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (OptionsList.SelectedItem is string value)
        {
            SelectedValue = value;
            SelectedIndex = OptionsList.SelectedIndex;
            DialogResult = true;
            Close();
        }
    }
}
