using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.Move,
            X = (int)pos.X,
            Y = (int)pos.Y
        });
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.LeftDown,
            X = (int)pos.X,
            Y = (int)pos.Y,
            Button = 0
        });
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.LeftUp,
            X = (int)pos.X,
            Y = (int)pos.Y,
            Button = 0
        });
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.RightDown,
            X = (int)pos.X,
            Y = (int)pos.Y,
            Button = 2
        });
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.RightUp,
            X = (int)pos.X,
            Y = (int)pos.Y,
            Button = 2
        });
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        VM?.RaiseMouseEvent(new MouseEventData
        {
            EventType = MouseEventType.Wheel,
            X = (int)pos.X,
            Y = (int)pos.Y,
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
