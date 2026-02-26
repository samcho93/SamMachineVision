using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MVXTester.App.ViewModels;
using MVXTester.Core.Models;

namespace MVXTester.App.Views;

public partial class ExecuteOutputView : UserControl
{
    public ExecuteOutputView()
    {
        InitializeComponent();
    }

    private ExecuteOutputViewModel? VM => DataContext as ExecuteOutputViewModel;

    /// <summary>
    /// Converts mouse position relative to the Image control into actual image pixel coordinates,
    /// accounting for Stretch="Uniform" scaling and letterbox/pillarbox margins.
    /// Returns (-1,-1) if conversion is not possible.
    /// </summary>
    private (int X, int Y) GetImagePixelPosition(MouseEventArgs e)
    {
        var image = OutputImageElement;
        if (image.Source is not BitmapSource bmp)
            return (-1, -1);

        var pos = e.GetPosition(image);
        var controlWidth = image.ActualWidth;
        var controlHeight = image.ActualHeight;

        if (controlWidth <= 0 || controlHeight <= 0)
            return (-1, -1);

        var imgWidth = (double)bmp.PixelWidth;
        var imgHeight = (double)bmp.PixelHeight;

        // Calculate the scale and offset for Stretch="Uniform"
        var scaleX = controlWidth / imgWidth;
        var scaleY = controlHeight / imgHeight;
        var scale = Math.Min(scaleX, scaleY);

        var renderWidth = imgWidth * scale;
        var renderHeight = imgHeight * scale;
        var offsetX = (controlWidth - renderWidth) / 2.0;
        var offsetY = (controlHeight - renderHeight) / 2.0;

        // Convert control coordinates to image pixel coordinates
        var pixelX = (pos.X - offsetX) / scale;
        var pixelY = (pos.Y - offsetY) / scale;

        // Clamp to image bounds
        var x = (int)Math.Clamp(pixelX, 0, imgWidth - 1);
        var y = (int)Math.Clamp(pixelY, 0, imgHeight - 1);

        return (x, y);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.Move,
            X = x,
            Y = y
        });
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.LeftDown,
            X = x,
            Y = y,
            Button = 0
        });
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.LeftUp,
            X = x,
            Y = y,
            Button = 0
        });
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.RightDown,
            X = x,
            Y = y,
            Button = 2
        });
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.RightUp,
            X = x,
            Y = y,
            Button = 2
        });
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var (x, y) = GetImagePixelPosition(e);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.Wheel,
            X = x,
            Y = y,
            Delta = e.Delta
        });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        VM?.RaiseKeyboardEvent(new KeyboardEventData
        {
            EventType = KeyEventType.KeyDown,
            KeyCode = KeyInterop.VirtualKeyFromKey(e.Key),
            KeyName = e.Key.ToString(),
            IsCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl),
            IsShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift),
            IsAlt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)
        });
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        VM?.RaiseKeyboardEvent(new KeyboardEventData
        {
            EventType = KeyEventType.KeyUp,
            KeyCode = KeyInterop.VirtualKeyFromKey(e.Key),
            KeyName = e.Key.ToString(),
            IsCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl),
            IsShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift),
            IsAlt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)
        });
    }
}
