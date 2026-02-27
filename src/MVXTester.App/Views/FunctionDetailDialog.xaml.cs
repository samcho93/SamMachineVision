using System.Windows;
using MVXTester.App.ViewModels;

namespace MVXTester.App.Views;

public partial class FunctionDetailDialog : Window
{
    public FunctionDetailDialog(FunctionDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        FunctionNameText.Text = $"Function: {viewModel.FunctionName}";
        SourceFileText.Text = viewModel.SourceFilePath;

        // 초기 뷰포트를 서브그래프 중심으로 맞추기
        Loaded += (_, _) =>
        {
            if (viewModel.Nodes.Count > 0)
            {
                DetailEditor.FitToScreen();
            }
        };
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
