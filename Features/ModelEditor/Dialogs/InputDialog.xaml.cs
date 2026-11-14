using System.Windows;
namespace JBZUniversalTester.ModelEditor.Dialogs;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text.Trim();
    public InputDialog(string title, string prompt, string initial = "") { InitializeComponent(); JBZUniversalTester.Views.DialogViewport.Attach(this); Title = title; PromptText.Text = prompt; ValueBox.Text = initial; Loaded += (_, __) => { ValueBox.Focus(); ValueBox.SelectAll(); }; }
    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
}
