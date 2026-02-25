using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

[NodeInfo("Shi-Tomasi Corners", NodeCategories.Feature, Description = "Shi-Tomasi corner detection (Good Features to Track)")]
public class ShiTomasiNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _maxCorners = null!;
    private NodeProperty _qualityLevel = null!;
    private NodeProperty _minDistance = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _maxCorners = AddIntProperty("MaxCorners", "Max Corners", 100, 1, 10000, "Maximum number of corners");
        _qualityLevel = AddDoubleProperty("QualityLevel", "Quality Level", 0.01, 0.0, 1.0, "Minimal accepted quality");
        _minDistance = AddDoubleProperty("MinDistance", "Min Distance", 10.0, 0.0, 1000.0, "Minimum distance between corners");
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

            var maxCorners = _maxCorners.GetValue<int>();
            var qualityLevel = _qualityLevel.GetValue<double>();
            var minDistance = _minDistance.GetValue<double>();

            // Convert to grayscale
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var corners = Cv2.GoodFeaturesToTrack(gray, maxCorners, qualityLevel, minDistance, null!, 3, false, 0.04);
            if (needDispose) gray.Dispose();

            // Draw corners on result
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            foreach (var corner in corners)
            {
                Cv2.Circle(result, (int)corner.X, (int)corner.Y, 5, new Scalar(0, 0, 255), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Shi-Tomasi error: {ex.Message}";
        }
    }
}
