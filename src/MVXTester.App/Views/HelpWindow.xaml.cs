using System.IO;
using System.Windows;

namespace MVXTester.App.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadTextbookPdfAsync();
    }

    private async Task LoadTextbookPdfAsync()
    {
        try
        {
            var pdf = FindTextbookPdf();
            if (pdf == null)
            {
                PdfViewer.Visibility = Visibility.Collapsed;
                PdfNotFound.Visibility = Visibility.Visible;
                return;
            }

            await PdfViewer.EnsureCoreWebView2Async();
            PdfViewer.CoreWebView2.Navigate(new Uri(pdf).AbsoluteUri);
        }
        catch
        {
            PdfViewer.Visibility = Visibility.Collapsed;
            PdfNotFound.Visibility = Visibility.Visible;
        }
    }

    private string? FindTextbookPdf()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(exeDir, "docs", "MVXTester_Textbook.pdf");
        if (File.Exists(path)) return path;

        // Fallback: project-level docs folder (development)
        try
        {
            var devPath = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "docs"));
            if (Directory.Exists(devPath))
            {
                foreach (var f in Directory.GetFiles(devPath, "*.pdf"))
                {
                    if (f.Contains("교재") || f.Contains("Textbook"))
                        return f;
                }
            }
        }
        catch { }
        return null;
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
