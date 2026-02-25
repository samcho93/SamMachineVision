using System.Windows;
using MVXTester.App.ViewModels;

namespace MVXTester.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
