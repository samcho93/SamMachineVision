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

[NodeInfo("USB Camera", NodeCategories.Input, Description = "USB/Webcam capture using OpenCvSharp VideoCapture")]
public class UsbCameraNode : BaseNode, IStreamingSource
{
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _cameraIndex = null!;
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
        _cameraIndex = AddIntProperty("CameraIndex", "Camera Index", 0, 0, 10, "Camera device index");
        _width = AddIntProperty("Width", "Width", 640, 0, 4096, "Capture width (0=default)");
        _height = AddIntProperty("Height", "Height", 480, 0, 4096, "Capture height (0=default)");
        _fps = AddIntProperty("FPS", "FPS", 30, 1, 120, "Target frames per second");
        _backend = AddEnumProperty("Backend", "Backend", UsbCameraBackend.DirectShow, "Capture backend API");
    }

    public override void Process()
    {
        try
        {
            var cameraIndex = _cameraIndex.GetValue<int>();
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
                Error = $"{frame.Width}x{frame.Height} {frame.Channels()}ch";
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
