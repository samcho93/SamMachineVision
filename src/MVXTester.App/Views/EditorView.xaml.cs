using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MVXTester.App.ViewModels;

namespace MVXTester.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EditorViewModel vm)
        {
            vm.AutoConnectRequested += movedNodes =>
            {
                Editor.UpdateLayout();
                vm.AutoConnectByProximity(movedNodes);
            };
        }
    }

    private void Connection_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ConnectionViewModel connVm)
        {
            if (DataContext is EditorViewModel vm)
            {
                vm.DeleteConnectionCommand.Execute(connVm);
                e.Handled = true;
            }
        }
    }
}
