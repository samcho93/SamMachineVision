using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Detection;

[NodeInfo("Hough Circles", NodeCategories.Detection, Description = "Detect circles using Hough transform")]
public class HoughCirclesNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _dp = null!;
    private NodeProperty _minDist = null!;
    private NodeProperty _param1 = null!;
    private NodeProperty _param2 = null!;
    private NodeProperty _minRadius = null!;
    private NodeProperty _maxRadius = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _dp = AddDoubleProperty("Dp", "Dp", 1.0, 0.1, 10.0, "Inverse ratio of resolution");
        _minDist = AddDoubleProperty("MinDist", "Min Distance", 50.0, 1.0, 10000.0, "Minimum distance between circle centers");
        _param1 = AddDoubleProperty("Param1", "Param 1", 200.0, 1.0, 1000.0, "Upper threshold for Canny");
        _param2 = AddDoubleProperty("Param2", "Param 2", 100.0, 1.0, 1000.0, "Accumulator threshold");
        _minRadius = AddIntProperty("MinRadius", "Min Radius", 0, 0, 5000, "Minimum circle radius");
        _maxRadius = AddIntProperty("MaxRadius", "Max Radius", 0, 0, 5000, "Maximum circle radius (0 = unlimited)");
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

            var dp = _dp.GetValue<double>();
            var minDist = _minDist.GetValue<double>();
            var param1 = _param1.GetValue<double>();
            var param2 = _param2.GetValue<double>();
            var minRadius = _minRadius.GetValue<int>();
            var maxRadius = _maxRadius.GetValue<int>();

            // Ensure grayscale
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var circles = Cv2.HoughCircles(gray, HoughModes.Gradient, dp, minDist, param1, param2, minRadius, maxRadius);
            if (needDispose) gray.Dispose();

            // Draw circles on result
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            foreach (var circle in circles)
            {
                var center = new Point((int)circle.Center.X, (int)circle.Center.Y);
                var radius = (int)circle.Radius;
                Cv2.Circle(result, center, radius, new Scalar(0, 255, 0), 2);
                Cv2.Circle(result, center, 2, new Scalar(0, 0, 255), 3);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Hough Circles error: {ex.Message}";
        }
    }
}
