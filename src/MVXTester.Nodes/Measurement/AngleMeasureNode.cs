using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Measurement;

[NodeInfo("Angle Measure", NodeCategories.Measurement,
    Description = "Measures angle between two dominant lines/edges")]
public class AngleMeasureNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double> _angleOutput = null!;
    private OutputPort<double> _line1AngleOutput = null!;
    private OutputPort<double> _line2AngleOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _cannyLow = null!;
    private NodeProperty _cannyHigh = null!;
    private NodeProperty _minLineLength = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _angleOutput = AddOutput<double>("Angle");
        _line1AngleOutput = AddOutput<double>("Line1Angle");
        _line2AngleOutput = AddOutput<double>("Line2Angle");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _cannyLow = AddIntProperty("CannyLow", "Canny Low", 50, 1, 500, "Canny edge lower threshold");
        _cannyHigh = AddIntProperty("CannyHigh", "Canny High", 150, 1, 500, "Canny edge upper threshold");
        _minLineLength = AddDoubleProperty("MinLineLength", "Min Line Length", 50.0, 1.0, 10000.0,
            "Minimum line length for HoughLinesP");
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
            var cannyLow = _cannyLow.GetValue<int>();
            var cannyHigh = _cannyHigh.GetValue<int>();
            var minLineLength = _minLineLength.GetValue<double>();

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

            // Canny edge detection
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, cannyLow, cannyHigh);

            // HoughLinesP
            var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180.0, 50, minLineLength, 10);

            if (lines.Length < 2)
            {
                Error = $"Need at least 2 lines, found {lines.Length}";
                return;
            }

            // Sort by line length descending, take top 2
            var sortedLines = lines
                .Select(seg =>
                {
                    double dx = seg.P2.X - seg.P1.X;
                    double dy = seg.P2.Y - seg.P1.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    return new { Segment = seg, Length = length, Angle = angle };
                })
                .OrderByDescending(x => x.Length)
                .ToList();

            var line1 = sortedLines[0];
            var line2 = sortedLines[1];

            // Compute angle between the two lines
            double angle1 = line1.Angle;
            double angle2 = line2.Angle;
            double angleBetween = Math.Abs(angle1 - angle2);
            if (angleBetween > 180.0)
                angleBetween = 360.0 - angleBetween;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw line 1 in blue
            Cv2.Line(result, line1.Segment.P1, line1.Segment.P2, new Scalar(255, 0, 0), 2);
            // Draw line 2 in green
            Cv2.Line(result, line2.Segment.P1, line2.Segment.P2, new Scalar(0, 255, 0), 2);

            // Compute intersection point for arc drawing
            var intersection = ComputeIntersection(line1.Segment, line2.Segment);
            if (intersection.HasValue)
            {
                var iPt = intersection.Value;

                // Draw arc showing the angle
                int arcRadius = 30;
                double startAngle = Math.Min(angle1, angle2);
                double endAngle = Math.Max(angle1, angle2);
                if (endAngle - startAngle > 180.0)
                {
                    (startAngle, endAngle) = (endAngle, startAngle + 360.0);
                }
                Cv2.Ellipse(result, iPt, new Size(arcRadius, arcRadius), 0,
                    startAngle, endAngle, new Scalar(0, 255, 255), 2);

                // Label with angle
                var labelPos = new Point(iPt.X + 10, iPt.Y - 10);
                Cv2.PutText(result, $"{angleBetween:F1} deg", labelPos,
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);
            }
            else
            {
                // Lines are parallel, label at center of image
                var labelPos = new Point(10, 30);
                Cv2.PutText(result, $"{angleBetween:F1} deg (parallel)", labelPos,
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_angleOutput, angleBetween);
            SetOutputValue(_line1AngleOutput, angle1);
            SetOutputValue(_line2AngleOutput, angle2);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Angle Measure error: {ex.Message}";
        }
    }

    private static Point? ComputeIntersection(LineSegmentPoint seg1, LineSegmentPoint seg2)
    {
        double x1 = seg1.P1.X, y1 = seg1.P1.Y, x2 = seg1.P2.X, y2 = seg1.P2.Y;
        double x3 = seg2.P1.X, y3 = seg2.P1.Y, x4 = seg2.P2.X, y4 = seg2.P2.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-10)
            return null; // Parallel lines

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;

        int ix = (int)(x1 + t * (x2 - x1));
        int iy = (int)(y1 + t * (y2 - y1));

        return new Point(ix, iy);
    }
}
