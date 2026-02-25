using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Shape Classifier", NodeCategories.Inspection, Description = "Classifies detected contours as Circle, Rectangle, Triangle, Square, or Polygon")]
public class ShapeClassifierNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<string> _shapeListOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _thresholdValue = null!;
    private NodeProperty _useOtsu = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _approxEpsilon = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _countOutput = AddOutput<int>("Count");
        _shapeListOutput = AddOutput<string>("ShapeList");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _thresholdValue = AddIntProperty("ThresholdValue", "Threshold", 128, 0, 255, "Threshold value");
        _useOtsu = AddBoolProperty("UseOtsu", "Use Otsu", true, "Use Otsu automatic thresholding");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 500.0, 0.0, 1000000.0, "Minimum contour area");
        _approxEpsilon = AddDoubleProperty("ApproxEpsilon", "Approx Epsilon", 0.04, 0.01, 0.1, "Contour approximation accuracy (ratio of perimeter)");
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
            var thresholdValue = _thresholdValue.GetValue<int>();
            var useOtsu = _useOtsu.GetValue<bool>();
            var minArea = _minArea.GetValue<double>();
            var approxEpsilon = _approxEpsilon.GetValue<double>();

            // Ensure odd blur size
            if (blurSize % 2 == 0) blurSize++;
            if (blurSize < 1) blurSize = 1;

            // Step 1: Convert to grayscale
            var gray = new Mat();
            if (image.Channels() > 1)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(gray);

            // Step 2: Gaussian blur
            var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(blurSize, blurSize), 0);
            gray.Dispose();

            // Step 3: Threshold
            var binary = new Mat();
            if (useOtsu)
                Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            else
                Cv2.Threshold(blurred, binary, thresholdValue, 255, ThresholdTypes.BinaryInv);
            blurred.Dispose();

            // Step 4: Find contours
            Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            binary.Dispose();

            // Build result visualization
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var shapeNames = new List<string>();

            // Color palette for different shapes
            var colors = new Dictionary<string, Scalar>
            {
                { "Triangle", new Scalar(255, 0, 0) },       // Blue
                { "Square", new Scalar(0, 255, 0) },         // Green
                { "Rectangle", new Scalar(0, 255, 255) },    // Yellow
                { "Circle", new Scalar(0, 0, 255) },         // Red
                { "Polygon", new Scalar(255, 0, 255) }       // Magenta
            };

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < minArea) continue;

                // Approximate contour
                double perimeter = Cv2.ArcLength(contour, true);
                var approx = Cv2.ApproxPolyDP(contour, approxEpsilon * perimeter, true);
                int vertices = approx.Length;

                // Classify shape
                string shapeName;

                if (vertices == 3)
                {
                    shapeName = "Triangle";
                }
                else if (vertices == 4)
                {
                    // Check aspect ratio to distinguish square vs rectangle
                    var boundingRect = Cv2.BoundingRect(approx);
                    double aspectRatio = (double)boundingRect.Width / boundingRect.Height;
                    shapeName = (aspectRatio >= 0.85 && aspectRatio <= 1.15) ? "Square" : "Rectangle";
                }
                else
                {
                    // Check circularity: 4*pi*area / perimeter^2
                    double circularity = (4.0 * Math.PI * area) / (perimeter * perimeter);
                    shapeName = (vertices >= 5 && circularity > 0.8) ? "Circle" : "Polygon";
                }

                shapeNames.Add(shapeName);

                // Draw contour
                var color = colors.ContainsKey(shapeName) ? colors[shapeName] : new Scalar(128, 128, 128);
                Cv2.DrawContours(result, new[] { contour }, 0, color, 2);

                // Draw label at centroid
                var moments = Cv2.Moments(contour);
                int cx, cy;
                if (moments.M00 > 0)
                {
                    cx = (int)(moments.M10 / moments.M00);
                    cy = (int)(moments.M01 / moments.M00);
                }
                else
                {
                    var rect = Cv2.BoundingRect(contour);
                    cx = rect.X + rect.Width / 2;
                    cy = rect.Y + rect.Height / 2;
                }

                string label = $"{shapeName} ({vertices}v)";
                Cv2.PutText(result, label, new Point(cx - 30, cy - 10),
                    HersheyFonts.HersheySimplex, 0.45, color, 1);
            }

            int count = shapeNames.Count;
            string shapeList = string.Join(",", shapeNames);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_countOutput, count);
            SetOutputValue(_shapeListOutput, shapeList);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Shape Classifier error: {ex.Message}";
        }
    }
}
