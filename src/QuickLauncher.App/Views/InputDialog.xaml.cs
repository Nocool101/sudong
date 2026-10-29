using System.Windows;

namespace QuickLauncher.App.Views;

public partial class InputDialog : Window
{
    public string ResultText => InputBox.Text;

    public InputDialog(string prompt, string defaultValue = "", bool multiline = false)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.AcceptsReturn = multiline;
        if (multiline)
        {
            InputBox.MinHeight = 120;
            InputBox.TextWrapping = TextWrapping.Wrap;
            Height = 280;
        }
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
