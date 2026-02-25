using System.Windows;
using Microsoft.Win32;

namespace MVXTester.App.Views;

public partial class CodePreviewDialog : Window
{
    private readonly string _language;

    public CodePreviewDialog(string code, string language = "Python")
    {
        InitializeComponent();
        _language = language;
        LanguageLabel.Text = $"Generated {language} Code";
        CodeTextBox.Text = code;
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(CodeTextBox.Text);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var ext = _language == "Python" ? "py" : "cs";
        var filter = _language == "Python"
            ? "Python Files (*.py)|*.py|All Files (*.*)|*.*"
            : "C# Files (*.cs)|*.cs|All Files (*.*)|*.*";

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = $".{ext}"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, CodeTextBox.Text);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
