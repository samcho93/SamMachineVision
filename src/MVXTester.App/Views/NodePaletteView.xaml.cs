using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MVXTester.App.ViewModels;
using MVXTester.Core.Registry;

namespace MVXTester.App.Views;

public partial class NodePaletteView : UserControl
{
    public NodePaletteView()
    {
        InitializeComponent();
    }

    private void NodeItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NodeRegistryEntry entry)
        {
            if (DataContext is NodePaletteViewModel vm)
            {
                vm.AddNodeCommand.Execute(entry);
            }
        }
    }
}
