using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Edge Inspector", NodeCategories.Inspection, Description = "Finds straight edges/lines in an image for alignment checking")]
public class EdgeInspectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<int> _lineCountOutput = null!;
    private OutputPort<double[]> _anglesOutput = null!;
    private OutputPort<double> _averageAngleOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _cannyLow = null!;
    private NodeProperty _cannyHigh = null!;
    private NodeProperty _minLineLength = null!;
    private NodeProperty _maxLineGap = null!;
    private NodeProperty _rho = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _lineCountOutput = AddOutput<int>("LineCount");
        _anglesOutput = AddOutput<double[]>("Angles");
        _averageAngleOutput = AddOutput<double>("AverageAngle");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _cannyLow = AddIntProperty("CannyLow", "Canny Low", 50, 1, 500, "Canny low threshold");
        _cannyHigh = AddIntProperty("CannyHigh", "Canny High", 150, 1, 500, "Canny high threshold");
        _minLineLength = AddDoubleProperty("MinLineLength", "Min Line Length", 50.0, 1.0, 10000.0, "Minimum line length");
        _maxLineGap = AddDoubleProperty("MaxLineGap", "Max Line Gap", 10.0, 1.0, 500.0, "Maximum gap between line segments");
        _rho = AddDoubleProperty("Rho", "Rho", 1.0, 0.1, 10.0, "Distance resolution in pixels");
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
            var maxLineGap = _maxLineGap.GetValue<double>();
            var rho = _rho.GetValue<double>();

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

            // Step 3: Canny edge detection
            var edges = new Mat();
            Cv2.Canny(blurred, edges, cannyLow, cannyHigh);
            blurred.Dispose();

            // Step 4: Hough Lines P
            var lines = Cv2.HoughLinesP(edges, rho, Math.PI / 180.0, 50, minLineLength, maxLineGap);
            edges.Dispose();

            // Step 5: Compute angles for each line
            var angles = new List<double>();
            foreach (var line in lines)
            {
                double dx = line.P2.X - line.P1.X;
                double dy = line.P2.Y - line.P1.Y;
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                // Normalize to 0-180 range
                if (angle < 0) angle += 180.0;
                angles.Add(angle);
            }

            int lineCount = lines.Length;
            double averageAngle = angles.Count > 0 ? angles.Average() : 0.0;

            // Build result visualization
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw detected lines in green with angle labels
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                Cv2.Line(result, line.P1, line.P2, new Scalar(0, 255, 0), 2);

                // Place angle text at midpoint of line
                int midX = (line.P1.X + line.P2.X) / 2;
                int midY = (line.P1.Y + line.P2.Y) / 2;
                string angleText = $"{angles[i]:F1}";
                Cv2.PutText(result, angleText, new Point(midX + 3, midY - 3),
                    HersheyFonts.HersheySimplex, 0.35, new Scalar(0, 200, 255), 1);
            }

            // Draw summary at top-left
            Cv2.PutText(result, $"Lines: {lineCount}  Avg Angle: {averageAngle:F1}", new Point(10, 30),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_lineCountOutput, lineCount);
            SetOutputValue(_anglesOutput, angles.ToArray());
            SetOutputValue(_averageAngleOutput, averageAngle);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Edge Inspector error: {ex.Message}";
        }
    }
}
