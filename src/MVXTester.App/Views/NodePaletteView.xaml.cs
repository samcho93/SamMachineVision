using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MVXTester.Core.Registry;

namespace MVXTester.App.Views;

public partial class NodePaletteView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    public NodePaletteView()
    {
        InitializeComponent();
    }

    private void NodeItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void NodeItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var pos = e.GetPosition(null);
        var diff = pos - _dragStartPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is FrameworkElement fe && fe.DataContext is NodeRegistryEntry entry)
            {
                _isDragging = true;
                var data = new DataObject("NodeRegistryEntry", entry);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
                _isDragging = false;
            }
        }
    }
}
