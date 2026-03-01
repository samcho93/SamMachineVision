using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

public enum UsbCameraBackend
{
    DirectShow,
    MSMF,
    Auto
}

// Hidden from palette - use unified CameraNode instead.
// Kept for backward compatibility (serialization) and as backend for CameraNode.
public class UsbCameraNode : BaseNode, IStreamingSource, IDeviceEnumerable
{
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _deviceList = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _fps = null!;
    private NodeProperty _backend = null!;

    private VideoCapture? _capture;
    private int _lastCameraIndex = -1;
    private int _lastWidth;
    private int _lastHeight;

    protected override void Setup()
    {
        _frameOutput = AddOutput<Mat>("Frame");
        _deviceList = AddDeviceListProperty("DeviceList", "Camera", -1, "Select USB camera device");
        _width = AddIntProperty("Width", "Width", 640, 0, 4096, "Capture width (0=default)");
        _height = AddIntProperty("Height", "Height", 480, 0, 4096, "Capture height (0=default)");
        _fps = AddIntProperty("FPS", "FPS", 30, 1, 120, "Target frames per second");
        _backend = AddEnumProperty("Backend", "Backend", UsbCameraBackend.DirectShow, "Capture backend API");

        EnumerateDevices();
    }

    public void EnumerateDevices()
    {
        var devices = new List<(string Name, int Index)>();
        var backend = _backend.GetValue<UsbCameraBackend>();
        var apiPref = backend switch
        {
            UsbCameraBackend.DirectShow => VideoCaptureAPIs.DSHOW,
            UsbCameraBackend.MSMF => VideoCaptureAPIs.MSMF,
            UsbCameraBackend.Auto => VideoCaptureAPIs.ANY,
            _ => VideoCaptureAPIs.ANY
        };

        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, apiPref);
                if (cap.IsOpened())
                {
                    var w = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                    var h = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                    devices.Add(($"Camera {i} ({w}x{h})", i));
                }
            }
            catch { }
        }

        _deviceList.UpdateDeviceOptions(devices);

        // Auto-select first device if none selected
        if (devices.Count > 0 && _deviceList.GetValue<int>() < 0)
            _deviceList.SetValue(devices[0].Index);
    }

    public override void Process()
    {
        try
        {
            var cameraIndex = _deviceList.GetValue<int>();
            if (cameraIndex < 0)
            {
                Error = "No camera selected";
                return;
            }

            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            var fps = _fps.GetValue<int>();

            // Re-open camera if settings changed
            if (_capture == null || !_capture.IsOpened() ||
                cameraIndex != _lastCameraIndex ||
                width != _lastWidth || height != _lastHeight)
            {
                CloseCamera();
                OpenCamera(cameraIndex, width, height, fps);
                if (_capture == null || !_capture.IsOpened())
                    return;
            }

            var frame = new Mat();
            if (_capture.Read(frame) && !frame.Empty())
            {
                SetOutputValue(_frameOutput, frame);
                SetPreview(frame);
                Error = null;
            }
            else
            {
                frame.Dispose();
                Error = "Failed to read frame";
            }
        }
        catch (Exception ex)
        {
            Error = $"USB Camera error: {ex.Message}";
        }
    }

    private void OpenCamera(int index, int width, int height, int fps)
    {
        try
        {
            var backend = _backend.GetValue<UsbCameraBackend>();
            var apiPref = backend switch
            {
                UsbCameraBackend.DirectShow => VideoCaptureAPIs.DSHOW,
                UsbCameraBackend.MSMF => VideoCaptureAPIs.MSMF,
                UsbCameraBackend.Auto => VideoCaptureAPIs.ANY,
                _ => VideoCaptureAPIs.ANY
            };

            _capture = new VideoCapture(index, apiPref);

            if (!_capture.IsOpened())
            {
                Error = $"Failed to open camera {index} ({backend})";
                _capture.Dispose();
                _capture = null;
                return;
            }

            if (width > 0) _capture.Set(VideoCaptureProperties.FrameWidth, width);
            if (height > 0) _capture.Set(VideoCaptureProperties.FrameHeight, height);
            if (fps > 0) _capture.Set(VideoCaptureProperties.Fps, fps);

            _lastCameraIndex = index;
            _lastWidth = width;
            _lastHeight = height;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Open camera failed: {ex.Message}";
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void CloseCamera()
    {
        try
        {
            _capture?.Release();
            _capture?.Dispose();
        }
        catch { }
        _capture = null;
    }

    public override void Cleanup()
    {
        CloseCamera();
        _lastCameraIndex = -1;
        base.Cleanup();
    }
}
