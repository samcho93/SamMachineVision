using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Measurement;

[NodeInfo("Object Measure", NodeCategories.Measurement,
    Description = "Measures dimensions of detected objects using contour analysis")]
public class ObjectMeasureNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double[]> _widthsOutput = null!;
    private OutputPort<double[]> _heightsOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _pixelsPerUnit = null!;
    private NodeProperty _unitName = null!;
    private NodeProperty _invertBinary = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _widthsOutput = AddOutput<double[]>("Widths");
        _heightsOutput = AddOutput<double[]>("Heights");
        _countOutput = AddOutput<int>("Count");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 500.0, 0.0, 1000000.0, "Minimum contour area in pixels");
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

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var widthList = new List<double>();
            var heightList = new List<double>();

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < minArea) continue;
                if (contour.Length < 5) continue;

                var rotatedRect = Cv2.MinAreaRect(contour);

                // Compute width and height in real units
                double widthPx = rotatedRect.Size.Width;
                double heightPx = rotatedRect.Size.Height;

                // Ensure width >= height for consistency
                if (widthPx < heightPx)
                    (widthPx, heightPx) = (heightPx, widthPx);

                double widthReal = widthPx / pixelsPerUnit;
                double heightReal = heightPx / pixelsPerUnit;

                widthList.Add(widthReal);
                heightList.Add(heightReal);

                // Draw MinAreaRect
                var vertices = Cv2.BoxPoints(rotatedRect);
                var points = vertices.Select(v => new Point((int)v.X, (int)v.Y)).ToArray();
                for (int i = 0; i < 4; i++)
                {
                    Cv2.Line(result, points[i], points[(i + 1) % 4], new Scalar(0, 255, 0), 2);
                }

                // Label with dimensions
                var label = $"{widthReal:F1} x {heightReal:F1} {unitName}";
                var center = new Point((int)rotatedRect.Center.X, (int)rotatedRect.Center.Y);
                Cv2.PutText(result, label, new Point(center.X - 40, center.Y - 10),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_widthsOutput, widthList.ToArray());
            SetOutputValue(_heightsOutput, heightList.ToArray());
            SetOutputValue(_countOutput, widthList.Count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Object Measure error: {ex.Message}";
        }
    }
}
