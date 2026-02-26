using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("Image Show", NodeCategories.Input, Description = "Display image in OpenCV window with mouse event support")]
public class ImageShowNode : BaseNode, IMouseEventReceiver
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<int> _mouseXOutput = null!;
    private OutputPort<int> _mouseYOutput = null!;
    private OutputPort<string> _mouseEventOutput = null!;
    private OutputPort<bool> _mousePressedOutput = null!;
    private NodeProperty _windowName = null!;

    private MouseEventData? _lastEvent;
    private bool _isPressed;
    private readonly object _lock = new();
    private string? _currentWindowName;
    private bool _callbackRegistered;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _mouseXOutput = AddOutput<int>("MouseX");
        _mouseYOutput = AddOutput<int>("MouseY");
        _mouseEventOutput = AddOutput<string>("MouseEvent");
        _mousePressedOutput = AddOutput<bool>("MousePressed");
        _windowName = AddStringProperty("WindowName", "Window Name", "Image", "Display window name");
    }

    public void OnMouseEvent(MouseEventData eventData)
    {
        lock (_lock)
        {
            _lastEvent = eventData;

            switch (eventData.EventType)
            {
                case MouseEventType.LeftDown:
                case MouseEventType.RightDown:
                case MouseEventType.MiddleDown:
                    _isPressed = true;
                    break;
                case MouseEventType.LeftUp:
                case MouseEventType.RightUp:
                case MouseEventType.MiddleUp:
                    _isPressed = false;
                    break;
            }
        }

        IsDirty = true;
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var windowName = _windowName.GetValue<string>();
            if (string.IsNullOrWhiteSpace(windowName))
                windowName = "Image";

            // Destroy old window if name changed
            if (_currentWindowName != null && _currentWindowName != windowName)
            {
                try { Cv2.DestroyWindow(_currentWindowName); } catch { }
                _callbackRegistered = false;
            }
            _currentWindowName = windowName;

            Cv2.ImShow(windowName, image);

            // Register mouse callback after ImShow (window must exist)
            if (!_callbackRegistered)
            {
                Cv2.SetMouseCallback(windowName, OnOpenCvMouseCallback);
                _callbackRegistered = true;
            }

            Cv2.WaitKey(1);

            // Output mouse event data
            lock (_lock)
            {
                if (_lastEvent != null)
                {
                    SetOutputValue(_mouseXOutput, _lastEvent.X);
                    SetOutputValue(_mouseYOutput, _lastEvent.Y);
                    SetOutputValue(_mouseEventOutput, _lastEvent.EventType.ToString());
                    SetOutputValue(_mousePressedOutput, _isPressed);
                }
            }

            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Image Show error: {ex.Message}";
        }
    }

    private void OnOpenCvMouseCallback(MouseEventTypes eventType, int x, int y, MouseEventFlags flags, IntPtr userdata)
    {
        var mapped = MapMouseEventType(eventType);
        if (mapped == null) return;

        OnMouseEvent(new MouseEventData
        {
            EventType = mapped.Value,
            X = x,
            Y = y,
            Button = GetButton(eventType)
        });
    }

    private static MouseEventType? MapMouseEventType(MouseEventTypes cvEvent)
    {
        return cvEvent switch
        {
            MouseEventTypes.MouseMove => MouseEventType.Move,
            MouseEventTypes.LButtonDown => MouseEventType.LeftDown,
            MouseEventTypes.LButtonUp => MouseEventType.LeftUp,
            MouseEventTypes.RButtonDown => MouseEventType.RightDown,
            MouseEventTypes.RButtonUp => MouseEventType.RightUp,
            MouseEventTypes.MButtonDown => MouseEventType.MiddleDown,
            MouseEventTypes.MButtonUp => MouseEventType.MiddleUp,
            MouseEventTypes.MouseWheel => MouseEventType.Wheel,
            _ => null
        };
    }

    private static int GetButton(MouseEventTypes cvEvent)
    {
        return cvEvent switch
        {
            MouseEventTypes.LButtonDown or MouseEventTypes.LButtonUp => 0,
            MouseEventTypes.MButtonDown or MouseEventTypes.MButtonUp => 1,
            MouseEventTypes.RButtonDown or MouseEventTypes.RButtonUp => 2,
            _ => -1
        };
    }

    public override void Cleanup()
    {
        try
        {
            if (_currentWindowName != null)
                Cv2.DestroyWindow(_currentWindowName);
        }
        catch { }
        _callbackRegistered = false;
        _currentWindowName = null;
        base.Cleanup();
    }
}
