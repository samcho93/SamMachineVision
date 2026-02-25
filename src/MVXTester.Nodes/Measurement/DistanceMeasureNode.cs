using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Measurement;

[NodeInfo("Distance Measure", NodeCategories.Measurement,
    Description = "Measures distance between two detected objects' centers")]
public class DistanceMeasureNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double> _distanceOutput = null!;
    private OutputPort<Point> _point1Output = null!;
    private OutputPort<Point> _point2Output = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _pixelsPerUnit = null!;
    private NodeProperty _unitName = null!;
    private NodeProperty _invertBinary = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _distanceOutput = AddOutput<double>("Distance");
        _point1Output = AddOutput<Point>("Point1");
        _point2Output = AddOutput<Point>("Point2");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 200.0, 0.0, 1000000.0, "Minimum contour area in pixels");
        _pixelsPerUnit = AddDoubleProperty("PixelsPerUnit", "Pixels Per Unit", 1.0, 0.001, 10000.0,
            "Pixels per real-world unit (1=pixel units)");
        _unitName = AddStringProperty("UnitName", "Unit Name", "px", "Unit name for display");
        _invertBinary = AddBoolProperty("InvertBinary", "Invert Binary", false, "Invert threshold result");
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
            var minArea = _minArea.GetValue<double>();
            var pixelsPerUnit = _pixelsPerUnit.GetValue<double>();
            var unitName = _unitName.GetValue<string>();
            var invertBinary = _invertBinary.GetValue<bool>();

            // Ensure blur size is odd
            if (blurSize % 2 == 0) blurSize++;
            if (blurSize < 1) blurSize = 1;

            // Grayscale
            using var gray = new Mat();
            if (image.Channels() > 1)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(gray);

            // Blur
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(blurSize, blurSize), 0);

            // Threshold (Otsu)
            using var binary = new Mat();
            var threshType = invertBinary
                ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                : ThresholdTypes.Binary | ThresholdTypes.Otsu;
            Cv2.Threshold(blurred, binary, 0, 255, threshType);

            // Find contours
            Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Filter by area and sort by area descending to get top 2 largest
            var filtered = contours
                .Select(c => new { Contour = c, Area = Cv2.ContourArea(c) })
                .Where(x => x.Area >= minArea)
                .OrderByDescending(x => x.Area)
                .Take(2)
                .ToList();

            if (filtered.Count < 2)
            {
                Error = $"Need at least 2 objects, found {filtered.Count}";
                return;
            }

            // Compute centers using moments
            var moments1 = Cv2.Moments(filtered[0].Contour);
            var moments2 = Cv2.Moments(filtered[1].Contour);

            var center1 = new Point(
                (int)(moments1.M10 / moments1.M00),
                (int)(moments1.M01 / moments1.M00));
            var center2 = new Point(
                (int)(moments2.M10 / moments2.M00),
                (int)(moments2.M01 / moments2.M00));

            // Compute distance
            double dx = center2.X - center1.X;
            double dy = center2.Y - center1.Y;
            double distancePx = Math.Sqrt(dx * dx + dy * dy);
            double distanceReal = distancePx / pixelsPerUnit;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw contours of the two objects
            Cv2.DrawContours(result, new[] { filtered[0].Contour }, -1, new Scalar(0, 255, 0), 2);
            Cv2.DrawContours(result, new[] { filtered[1].Contour }, -1, new Scalar(0, 255, 0), 2);

            // Draw centers
            Cv2.Circle(result, center1, 6, new Scalar(255, 0, 0), -1);
            Cv2.Circle(result, center2, 6, new Scalar(255, 0, 0), -1);

            // Draw line between centers
            Cv2.Line(result, center1, center2, new Scalar(0, 0, 255), 2);

            // Label with distance
            var midPoint = new Point((center1.X + center2.X) / 2, (center1.Y + center2.Y) / 2 - 10);
            var label = $"{distanceReal:F2} {unitName}";
            Cv2.PutText(result, label, midPoint,
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_distanceOutput, distanceReal);
            SetOutputValue(_point1Output, center1);
            SetOutputValue(_point2Output, center2);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Distance Measure error: {ex.Message}";
        }
    }
}
