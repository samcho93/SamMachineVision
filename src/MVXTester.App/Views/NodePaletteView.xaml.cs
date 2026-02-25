using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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

                // Create ghost adorner on the top-level window
                var window = Window.GetWindow(this);
                var adornerLayer = window != null ? AdornerLayer.GetAdornerLayer(window.Content as UIElement) : null;
                DragGhostAdorner? ghost = null;

                if (adornerLayer != null && window?.Content is UIElement rootElement)
                {
                    ghost = new DragGhostAdorner(rootElement, entry.Name);
                    adornerLayer.Add(ghost);
                }

                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);

                if (ghost != null && adornerLayer != null)
                    adornerLayer.Remove(ghost);

                _isDragging = false;
            }
        }
    }
}

/// <summary>
/// Adorner that shows a floating label following the cursor during drag-and-drop.
/// </summary>
public class DragGhostAdorner : Adorner
{
    private readonly string _text;
    private Point _lastPos;
    private static readonly Typeface _typeface = new("Segoe UI");

    public DragGhostAdorner(UIElement adornedElement, string text) : base(adornedElement)
    {
        _text = text;
        IsHitTestVisible = false;
        adornedElement.PreviewDragOver += OnDragOver;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        _lastPos = e.GetPosition(AdornedElement);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_lastPos == default) return;

        var formattedText = new FormattedText(
            _text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface, 12, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var padding = 8.0;
        var rect = new Rect(
            _lastPos.X + 14, _lastPos.Y + 6,
            formattedText.Width + padding * 2,
            formattedText.Height + padding);

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0xD0, 0x3B, 0x3B, 0x52)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x89, 0xB4, 0xFA)), 1),
            rect, 4, 4);

        dc.DrawText(formattedText, new Point(rect.X + padding, rect.Y + padding / 2));
    }

    public void Detach()
    {
        AdornedElement.PreviewDragOver -= OnDragOver;
    }
}
