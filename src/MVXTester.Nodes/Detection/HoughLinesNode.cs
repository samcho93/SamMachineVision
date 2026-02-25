using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Detection;

[NodeInfo("Hough Lines", NodeCategories.Detection, Description = "Detect lines using Hough transform")]
public class HoughLinesNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _rho = null!;
    private NodeProperty _theta = null!;
    private NodeProperty _threshold = null!;
    private NodeProperty _minLineLength = null!;
    private NodeProperty _maxLineGap = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _rho = AddDoubleProperty("Rho", "Rho", 1.0, 0.1, 10.0, "Distance resolution in pixels");
        _theta = AddDoubleProperty("Theta", "Theta (degrees)", 1.0, 0.1, 90.0, "Angle resolution in degrees");
        _threshold = AddIntProperty("Threshold", "Threshold", 100, 1, 1000, "Accumulator threshold");
        _minLineLength = AddDoubleProperty("MinLineLength", "Min Line Length", 50.0, 0.0, 10000.0, "Minimum line length");
        _maxLineGap = AddDoubleProperty("MaxLineGap", "Max Line Gap", 10.0, 0.0, 1000.0, "Maximum gap between line segments");
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

            var rho = _rho.GetValue<double>();
            var theta = _theta.GetValue<double>() * Math.PI / 180.0;
            var threshold = _threshold.GetValue<int>();
            var minLineLength = _minLineLength.GetValue<double>();
            var maxLineGap = _maxLineGap.GetValue<double>();

            // Ensure single channel for detection
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var lines = Cv2.HoughLinesP(gray, rho, theta, threshold, minLineLength, maxLineGap);
            if (needDispose) gray.Dispose();

            // Draw lines on result
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            foreach (var line in lines)
            {
                Cv2.Line(result, line.P1, line.P2, new Scalar(0, 0, 255), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Hough Lines error: {ex.Message}";
        }
    }
}
