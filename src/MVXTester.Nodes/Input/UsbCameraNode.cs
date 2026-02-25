using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("USB Camera", NodeCategories.Input, Description = "USB camera capture using OpenCvSharp VideoCapture")]
public class UsbCameraNode : BaseNode, IStreamingSource
{
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _cameraIndex = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _fps = null!;

    private VideoCapture? _capture;
    private int _lastCameraIndex = -1;

    protected override void Setup()
    {
        _frameOutput = AddOutput<Mat>("Frame");
        _cameraIndex = AddIntProperty("CameraIndex", "Camera Index", 0, 0, 10, "Camera device index");
        _width = AddIntProperty("Width", "Width", 640, 0, 4096, "Capture width");
        _height = AddIntProperty("Height", "Height", 480, 0, 4096, "Capture height");
        _fps = AddIntProperty("FPS", "FPS", 30, 1, 120, "Target frames per second");
    }

    public override void Process()
    {
        try
        {
            var cameraIndex = _cameraIndex.GetValue<int>();
            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            var fps = _fps.GetValue<int>();

            if (_capture == null || !_capture.IsOpened() || cameraIndex != _lastCameraIndex)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = new VideoCapture(cameraIndex);

                if (!_capture.IsOpened())
                {
                    Error = $"Failed to open USB camera at index {cameraIndex}";
                    return;
                }

                if (width > 0) _capture.Set(VideoCaptureProperties.FrameWidth, width);
                if (height > 0) _capture.Set(VideoCaptureProperties.FrameHeight, height);
                if (fps > 0) _capture.Set(VideoCaptureProperties.Fps, fps);

                _lastCameraIndex = cameraIndex;
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
                Error = "Failed to read frame from camera";
            }
        }
        catch (Exception ex)
        {
            Error = $"USB Camera error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        _lastCameraIndex = -1;
        base.Cleanup();
    }
}
