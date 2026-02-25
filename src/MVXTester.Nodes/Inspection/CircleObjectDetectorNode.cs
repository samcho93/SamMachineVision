using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Circle Detector", NodeCategories.Inspection,
    Description = "Detects circular objects and outputs their positions, radii, and count")]
public class CircleObjectDetectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _centersOutput = null!;
    private OutputPort<double[]> _radiiOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _dp = null!;
    private NodeProperty _minDist = null!;
    private NodeProperty _cannyThreshold = null!;
    private NodeProperty _accumThreshold = null!;
    private NodeProperty _minRadius = null!;
    private NodeProperty _maxRadius = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _centersOutput = AddOutput<Point[]>("Centers");
        _radiiOutput = AddOutput<double[]>("Radii");
        _countOutput = AddOutput<int>("Count");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 9, 1, 51, "Pre-processing blur kernel");
        _dp = AddDoubleProperty("Dp", "Dp", 1.2, 0.1, 10.0, "Inverse accumulator ratio");
        _minDist = AddDoubleProperty("MinDist", "Min Distance", 50.0, 1.0, 10000.0, "Min distance between centers");
        _cannyThreshold = AddDoubleProperty("CannyThreshold", "Canny Threshold", 100.0, 1.0, 500.0, "Canny upper threshold");
        _accumThreshold = AddDoubleProperty("AccumThreshold", "Accum Threshold", 50.0, 1.0, 500.0, "Accumulator threshold");
        _minRadius = AddIntProperty("MinRadius", "Min Radius", 10, 0, 5000, "Minimum circle radius");
        _maxRadius = AddIntProperty("MaxRadius", "Max Radius", 200, 0, 5000, "Maximum circle radius");
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

            var blurSize = _blurSize.GetValue<int>();
            var dp = _dp.GetValue<double>();
            var minDist = _minDist.GetValue<double>();
            var cannyThresh = _cannyThreshold.GetValue<double>();
            var accumThresh = _accumThreshold.GetValue<double>();
            var minRadius = _minRadius.GetValue<int>();
            var maxRadius = _maxRadius.GetValue<int>();

            // Ensure odd blur size
            if (blurSize % 2 == 0) blurSize++;

            // Grayscale
            using var gray = new Mat();
            if (image.Channels() > 1)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(gray);

            // Gaussian blur
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(blurSize, blurSize), 0);

            // Hough circles
            var circles = Cv2.HoughCircles(blurred, HoughModes.Gradient, dp, minDist,
                cannyThresh, accumThresh, minRadius, maxRadius);

            // Filter by radius range and collect results
            var centerList = new List<Point>();
            var radiiList = new List<double>();

            foreach (var circle in circles)
            {
                var radius = (double)circle.Radius;
                if (radius < minRadius || (maxRadius > 0 && radius > maxRadius)) continue;

                centerList.Add(new Point((int)circle.Center.X, (int)circle.Center.Y));
                radiiList.Add(radius);
            }

            var centers = centerList.ToArray();
            var radii = radiiList.ToArray();
            var count = centers.Length;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            for (int i = 0; i < count; i++)
            {
                var center = centers[i];
                var radius = (int)radii[i];

                // Green circle
                Cv2.Circle(result, center, radius, new Scalar(0, 255, 0), 2);

                // Red center dot
                Cv2.Circle(result, center, 3, new Scalar(0, 0, 255), -1);

                // Label with radius
                var label = $"r={radii[i]:F1}";
                Cv2.PutText(result, label, new Point(center.X + radius + 3, center.Y),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_centersOutput, centers);
            SetOutputValue(_radiiOutput, radii);
            SetOutputValue(_countOutput, count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Circle Detector error: {ex.Message}";
        }
    }
}
