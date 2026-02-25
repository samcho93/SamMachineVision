using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("Video Read", NodeCategories.Input, Description = "Read video file frame by frame")]
public class VideoReadNode : BaseNode, IStreamingSource
{
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _filePath = null!;

    private VideoCapture? _capture;
    private string _lastFilePath = "";

    protected override void Setup()
    {
        _frameOutput = AddOutput<Mat>("Frame");
        _filePath = AddFilePathProperty("FilePath", "File Path", "", "Path to video file");
    }

    public override void Process()
    {
        try
        {
            var filePath = _filePath.GetValue<string>();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Error = "File path is empty";
                return;
            }

            if (!File.Exists(filePath))
            {
                Error = $"File not found: {filePath}";
                return;
            }

            if (_capture == null || filePath != _lastFilePath)
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = new VideoCapture(filePath);
                _lastFilePath = filePath;

                if (!_capture.IsOpened())
                {
                    Error = $"Failed to open video: {filePath}";
                    return;
                }
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
                // Loop back to beginning
                _capture.Set(VideoCaptureProperties.PosFrames, 0);
                var retryFrame = new Mat();
                if (_capture.Read(retryFrame) && !retryFrame.Empty())
                {
                    SetOutputValue(_frameOutput, retryFrame);
                    SetPreview(retryFrame);
                    Error = null;
                }
                else
                {
                    retryFrame.Dispose();
                    Error = "Failed to read frame from video";
                }
            }
        }
        catch (Exception ex)
        {
            Error = $"Video Read error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        _lastFilePath = "";
        base.Cleanup();
    }
}
