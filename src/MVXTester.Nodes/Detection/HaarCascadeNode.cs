using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Detection;

[NodeInfo("Haar Cascade", NodeCategories.Detection, Description = "Object detection using Haar cascade classifier")]
public class HaarCascadeNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _filePath = null!;
    private NodeProperty _scaleFactor = null!;
    private NodeProperty _minNeighbors = null!;
    private NodeProperty _minSizeW = null!;
    private NodeProperty _minSizeH = null!;

    private CascadeClassifier? _classifier;
    private string _lastFilePath = "";

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _filePath = AddFilePathProperty("FilePath", "Cascade XML", "", "Path to Haar cascade XML file");
        _scaleFactor = AddDoubleProperty("ScaleFactor", "Scale Factor", 1.1, 1.01, 2.0, "Image scale factor per detection step");
        _minNeighbors = AddIntProperty("MinNeighbors", "Min Neighbors", 3, 0, 50, "Min neighbors for detection quality");
        _minSizeW = AddIntProperty("MinSizeW", "Min Size W", 30, 0, 1000, "Minimum detection width");
        _minSizeH = AddIntProperty("MinSizeH", "Min Size H", 30, 0, 1000, "Minimum detection height");
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

            var filePath = _filePath.GetValue<string>();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Error = "Cascade file path is empty";
                return;
            }

            if (!File.Exists(filePath))
            {
                Error = $"Cascade file not found: {filePath}";
                return;
            }

            // Load classifier if needed
            if (_classifier == null || filePath != _lastFilePath)
            {
                _classifier?.Dispose();
                _classifier = new CascadeClassifier(filePath);
                _lastFilePath = filePath;
            }

            var scaleFactor = _scaleFactor.GetValue<double>();
            var minNeighbors = _minNeighbors.GetValue<int>();
            var minSize = new Size(_minSizeW.GetValue<int>(), _minSizeH.GetValue<int>());

            // Convert to grayscale
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var detections = _classifier.DetectMultiScale(gray, scaleFactor, minNeighbors, HaarDetectionTypes.ScaleImage, minSize);
            if (needDispose) gray.Dispose();

            // Draw detections
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            foreach (var rect in detections)
            {
                Cv2.Rectangle(result, rect, new Scalar(0, 255, 0), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Haar Cascade error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        _classifier?.Dispose();
        _classifier = null;
        _lastFilePath = "";
        base.Cleanup();
    }
}
