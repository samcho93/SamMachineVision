using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MVXTester.App.ViewModels;
using MVXTester.Core.Registry;

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

    private void Editor_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("NodeRegistryEntry"))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("NodeRegistryEntry") is NodeRegistryEntry entry
            && DataContext is EditorViewModel vm)
        {
            // Convert drop position to graph coordinates
            var dropPos = e.GetPosition(Editor);
            var graphPos = new Point(
                dropPos.X / Editor.ViewportZoom + Editor.ViewportLocation.X,
                dropPos.Y / Editor.ViewportZoom + Editor.ViewportLocation.Y);

            var nodeVm = vm.AddNode(entry, graphPos);
            vm.SelectNode(nodeVm);
        }
    }
}
