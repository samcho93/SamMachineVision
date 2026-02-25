using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Presence Checker", NodeCategories.Inspection,
    Description = "Checks if an expected feature/object is present in a ROI")]
public class PresenceCheckerNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double> _fillRatioOutput = null!;
    private OutputPort<int> _pixelCountOutput = null!;
    private OutputPort<bool> _passOutput = null!;

    private NodeProperty _roiX = null!;
    private NodeProperty _roiY = null!;
    private NodeProperty _roiWidth = null!;
    private NodeProperty _roiHeight = null!;
    private NodeProperty _thresholdValue = null!;
    private NodeProperty _invertBinary = null!;
    private NodeProperty _minFillRatio = null!;
    private NodeProperty _maxFillRatio = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _fillRatioOutput = AddOutput<double>("FillRatio");
        _pixelCountOutput = AddOutput<int>("PixelCount");
        _passOutput = AddOutput<bool>("Pass");

        _roiX = AddIntProperty("RoiX", "ROI X", 0, 0, 10000, "ROI top-left X coordinate");
        _roiY = AddIntProperty("RoiY", "ROI Y", 0, 0, 10000, "ROI top-left Y coordinate");
        _roiWidth = AddIntProperty("RoiWidth", "ROI Width", 100, 1, 10000, "ROI width");
        _roiHeight = AddIntProperty("RoiHeight", "ROI Height", 100, 1, 10000, "ROI height");
        _thresholdValue = AddIntProperty("ThresholdValue", "Threshold", 128, 0, 255, "Binary threshold value");
        _invertBinary = AddBoolProperty("InvertBinary", "Invert Binary", false, "Invert threshold result");
        _minFillRatio = AddDoubleProperty("MinFillRatio", "Min Fill Ratio", 0.3, 0.0, 1.0,
            "Min fill ratio for PASS");
        _maxFillRatio = AddDoubleProperty("MaxFillRatio", "Max Fill Ratio", 1.0, 0.0, 1.0,
            "Max fill ratio for PASS");
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

            var roiX = _roiX.GetValue<int>();
            var roiY = _roiY.GetValue<int>();
            var roiWidth = _roiWidth.GetValue<int>();
            var roiHeight = _roiHeight.GetValue<int>();
            var thresholdValue = _thresholdValue.GetValue<int>();
            var invertBinary = _invertBinary.GetValue<bool>();
            var minFillRatio = _minFillRatio.GetValue<double>();
            var maxFillRatio = _maxFillRatio.GetValue<double>();

            // Clamp ROI to image bounds
            roiX = Math.Max(0, Math.Min(roiX, image.Width - 1));
            roiY = Math.Max(0, Math.Min(roiY, image.Height - 1));
            roiWidth = Math.Max(1, Math.Min(roiWidth, image.Width - roiX));
            roiHeight = Math.Max(1, Math.Min(roiHeight, image.Height - roiY));

            var roiRect = new Rect(roiX, roiY, roiWidth, roiHeight);

            // Extract ROI
            using var roi = new Mat(image, roiRect);

            // Grayscale
            using var roiGray = new Mat();
            if (roi.Channels() > 1)
                Cv2.CvtColor(roi, roiGray, ColorConversionCodes.BGR2GRAY);
            else
                roi.CopyTo(roiGray);

            // Threshold
            using var roiBinary = new Mat();
            var threshType = invertBinary ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
            Cv2.Threshold(roiGray, roiBinary, thresholdValue, 255, threshType);

            // Count non-zero pixels
            int pixelCount = Cv2.CountNonZero(roiBinary);
            int totalPixels = roiWidth * roiHeight;
            double fillRatio = (double)pixelCount / totalPixels;

            // Pass/fail
            bool pass = fillRatio >= minFillRatio && fillRatio <= maxFillRatio;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Highlight the ROI area with a semi-transparent overlay
            using var overlay = result.Clone();
            var roiColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.Rectangle(overlay, roiRect, roiColor, -1);
            Cv2.AddWeighted(overlay, 0.2, result, 0.8, 0, result);

            // Draw ROI rectangle border
            Cv2.Rectangle(result, roiRect, roiColor, 2);

            // Status text
            double fillPercent = fillRatio * 100.0;
            var statusStr = pass ? "PASS" : "FAIL";
            var statusColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            var label = $"Fill: {fillPercent:F1}% [{statusStr}]";
            var textY = roiY > 25 ? roiY - 8 : roiY + roiHeight + 20;
            Cv2.PutText(result, label, new Point(roiX, textY),
                HersheyFonts.HersheySimplex, 0.6, statusColor, 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_fillRatioOutput, fillRatio);
            SetOutputValue(_pixelCountOutput, pixelCount);
            SetOutputValue(_passOutput, pass);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Presence Checker error: {ex.Message}";
        }
    }
}
