using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Alignment Checker", NodeCategories.Inspection,
    Description = "Checks if an object is aligned correctly by measuring orientation angle")]
public class AlignmentCheckerNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double> _angleOutput = null!;
    private OutputPort<double> _angleErrorOutput = null!;
    private OutputPort<bool> _passOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _expectedAngle = null!;
    private NodeProperty _angleTolerance = null!;
    private NodeProperty _invertBinary = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _angleOutput = AddOutput<double>("Angle");
        _angleErrorOutput = AddOutput<double>("AngleError");
        _passOutput = AddOutput<bool>("Pass");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 1000.0, 0.0, 1000000.0, "Minimum contour area in pixels");
        _expectedAngle = AddDoubleProperty("ExpectedAngle", "Expected Angle", 0.0, -180.0, 180.0,
            "Expected orientation angle");
        _angleTolerance = AddDoubleProperty("AngleTolerance", "Angle Tolerance", 5.0, 0.0, 180.0,
            "Allowed angle deviation");
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
            var expectedAngle = _expectedAngle.GetValue<double>();
            var angleTolerance = _angleTolerance.GetValue<double>();
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

            // Take the largest contour by area
            Point[]? largestContour = null;
            double largestArea = 0;
            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area >= minArea && area > largestArea)
                {
                    largestArea = area;
                    largestContour = contour;
                }
            }

            if (largestContour == null)
            {
                Error = "No object found meeting minimum area requirement";
                return;
            }

            // Get orientation angle using FitEllipse (needs >= 5 points) or MinAreaRect
            double detectedAngle;
            RotatedRect fittedShape;

            if (largestContour.Length >= 5)
            {
                fittedShape = Cv2.FitEllipse(largestContour);
                // FitEllipse returns angle 0-180, normalize to -90..90 for comparison
                detectedAngle = fittedShape.Angle;
                if (detectedAngle > 90)
                    detectedAngle -= 180;
            }
            else
            {
                fittedShape = Cv2.MinAreaRect(largestContour);
                detectedAngle = fittedShape.Angle;
                if (detectedAngle > 90)
                    detectedAngle -= 180;
            }

            // Compute angle error (shortest angular distance)
            double angleError = detectedAngle - expectedAngle;
            // Normalize to [-180, 180]
            while (angleError > 180) angleError -= 360;
            while (angleError < -180) angleError += 360;
            double absError = Math.Abs(angleError);

            bool pass = absError <= angleTolerance;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw the contour
            var contourColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.DrawContours(result, new[] { largestContour }, -1, contourColor, 2);

            // Draw the fitted ellipse/rect
            if (largestContour.Length >= 5)
            {
                Cv2.Ellipse(result, fittedShape, new Scalar(255, 255, 0), 2);
            }
            else
            {
                var vertices = Cv2.BoxPoints(fittedShape);
                var points = vertices.Select(v => new Point((int)v.X, (int)v.Y)).ToArray();
                for (int i = 0; i < 4; i++)
                    Cv2.Line(result, points[i], points[(i + 1) % 4], new Scalar(255, 255, 0), 2);
            }

            // Draw angle line from center
            var center = new Point((int)fittedShape.Center.X, (int)fittedShape.Center.Y);
            double angleRad = detectedAngle * Math.PI / 180.0;
            int lineLen = 50;
            var lineEnd = new Point(
                (int)(center.X + lineLen * Math.Cos(angleRad)),
                (int)(center.Y + lineLen * Math.Sin(angleRad)));
            Cv2.Line(result, center, lineEnd, new Scalar(0, 165, 255), 2);
            Cv2.Circle(result, center, 4, new Scalar(0, 165, 255), -1);

            // Status text
            var statusStr = pass ? "PASS" : "FAIL";
            var statusColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            var label = $"Angle: {detectedAngle:F1} deg (Error: {absError:F1} deg) {statusStr}";
            Cv2.PutText(result, label, new Point(10, 30),
                HersheyFonts.HersheySimplex, 0.6, statusColor, 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_angleOutput, detectedAngle);
            SetOutputValue(_angleErrorOutput, absError);
            SetOutputValue(_passOutput, pass);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Alignment Checker error: {ex.Message}";
        }
    }
}
