using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("Image Show", NodeCategories.Input, Description = "Display image in OpenCV window with mouse/keyboard event support")]
public class ImageShowNode : BaseNode, IMouseEventReceiver
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<int> _mouseXOutput = null!;
    private OutputPort<int> _mouseYOutput = null!;
    private OutputPort<string> _mouseEventOutput = null!;
    private OutputPort<bool> _mousePressedOutput = null!;
    private OutputPort<int> _keyCodeOutput = null!;
    private OutputPort<string> _keyNameOutput = null!;
    private NodeProperty _windowName = null!;

    // Mouse/keyboard event data (thread-safe)
    private MouseEventData? _lastEvent;
    private bool _isPressed;
    private int _lastKeyCode = -1;
    private readonly object _eventLock = new();

    // Dedicated display thread for OpenCV HighGUI
    // (ImShow/WaitKey/SetMouseCallback must all run on the same thread)
    private Thread? _displayThread;
    private volatile bool _threadRunning;
    private Mat? _pendingImage;
    private readonly object _imageLock = new();
    private string _activeWindowName = "Image";

    // Must keep a strong reference to prevent GC from collecting the delegate
    // passed to native OpenCV via P/Invoke (Cv2.SetMouseCallback)
    private MouseCallback? _mouseCallbackDelegate;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _mouseXOutput = AddOutput<int>("MouseX");
        _mouseYOutput = AddOutput<int>("MouseY");
        _mouseEventOutput = AddOutput<string>("MouseEvent");
        _mousePressedOutput = AddOutput<bool>("MousePressed");
        _keyCodeOutput = AddOutput<int>("KeyCode");
        _keyNameOutput = AddOutput<string>("KeyName");
        _windowName = AddStringProperty("WindowName", "Window Name", "Image", "Display window name");
    }

    /// <summary>
    /// Receives mouse events from the OpenCV window callback (primary)
    /// or from ExecuteOutput panel routing (secondary).
    /// </summary>
    public void OnMouseEvent(MouseEventData eventData)
    {
        lock (_eventLock)
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

            // Start dedicated display thread if not running
            EnsureDisplayThread();

            // Queue image for display on the dedicated thread
            lock (_imageLock)
            {
                _pendingImage?.Dispose();
                _pendingImage = image.Clone();
            }

            // Output event data from last received events
            lock (_eventLock)
            {
                if (_lastEvent != null)
                {
                    SetOutputValue(_mouseXOutput, _lastEvent.X);
                    SetOutputValue(_mouseYOutput, _lastEvent.Y);
                    SetOutputValue(_mouseEventOutput, _lastEvent.EventType.ToString());
                    SetOutputValue(_mousePressedOutput, _isPressed);
                }
                if (_lastKeyCode >= 0)
                {
                    SetOutputValue(_keyCodeOutput, _lastKeyCode);
                    SetOutputValue(_keyNameOutput, ((char)_lastKeyCode).ToString());
                }
            }

            // Also set preview for ExecuteOutput panel display
            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Image Show error: {ex.Message}";
        }
    }

    private void EnsureDisplayThread()
    {
        if (_displayThread != null && _threadRunning) return;

        _activeWindowName = _windowName.GetValue<string>();
        if (string.IsNullOrWhiteSpace(_activeWindowName))
            _activeWindowName = "Image";

        _threadRunning = true;
        _displayThread = new Thread(DisplayLoop)
        {
            IsBackground = true,
            Name = $"ImageShow_{_activeWindowName}"
        };
        _displayThread.Start();
    }

    /// <summary>
    /// Dedicated thread loop for OpenCV HighGUI operations.
    /// ImShow, WaitKey, and SetMouseCallback all run on this single thread.
    /// </summary>
    private void DisplayLoop()
    {
        var windowName = _activeWindowName;
        bool windowCreated = false;
        bool callbackRegistered = false;

        try
        {
            while (_threadRunning)
            {
                Mat? imageToShow = null;
                lock (_imageLock)
                {
                    if (_pendingImage != null)
                    {
                        imageToShow = _pendingImage;
                        _pendingImage = null;
                    }
                }

                if (imageToShow != null)
                {
                    Cv2.ImShow(windowName, imageToShow);
                    windowCreated = true;
                    if (!callbackRegistered)
                    {
                        // Store delegate in field to prevent GC collection (P/Invoke prevent)
                        _mouseCallbackDelegate = OnOpenCvMouseCallback;
                        Cv2.SetMouseCallback(windowName, _mouseCallbackDelegate);
                        callbackRegistered = true;
                    }
                    imageToShow.Dispose();
                }

                if (windowCreated)
                {
                    // Process window messages - WaitKey MUST run on the same thread as ImShow
                    try
                    {
                        var key = Cv2.WaitKey(1);
                        if (key >= 0)
                        {
                            lock (_eventLock)
                            {
                                _lastKeyCode = key;
                            }
                            IsDirty = true;
                        }
                    }
                    catch
                    {
                        // Window may have been closed by user
                        windowCreated = false;
                        callbackRegistered = false;
                    }
                }
                else
                {
                    // No window yet, avoid busy-waiting
                    Thread.Sleep(16);
                }
            }
        }
        catch { }
        finally
        {
            if (windowCreated)
            {
                try { Cv2.DestroyWindow(windowName); } catch { }
            }
        }
    }

    /// <summary>
    /// Called by OpenCV on the display thread when mouse events occur on the window.
    /// </summary>
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
        _threadRunning = false;
        _displayThread?.Join(1000);
        _displayThread = null;
        _mouseCallbackDelegate = null;

        lock (_imageLock)
        {
            _pendingImage?.Dispose();
            _pendingImage = null;
        }

        base.Cleanup();
    }
}
