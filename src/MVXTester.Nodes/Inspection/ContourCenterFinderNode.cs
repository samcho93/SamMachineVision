using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Contour Center Finder", NodeCategories.Inspection,
    Description = "Complete pipeline from image to object center positions via contour analysis")]
public class ContourCenterFinderNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _centersOutput = null!;
    private OutputPort<double[]> _areasOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _useAutoThreshold = null!;
    private NodeProperty _thresholdValue = null!;
    private NodeProperty _invertBinary = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _maxArea = null!;
    private NodeProperty _drawRadius = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _centersOutput = AddOutput<Point[]>("Centers");
        _areasOutput = AddOutput<double[]>("Areas");
        _countOutput = AddOutput<int>("Count");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _useAutoThreshold = AddBoolProperty("UseAutoThreshold", "Auto Threshold", true, "Use Otsu auto threshold");
        _thresholdValue = AddIntProperty("ThresholdValue", "Threshold", 128, 0, 255, "Manual threshold (if auto off)");
        _invertBinary = AddBoolProperty("InvertBinary", "Invert Binary", false, "Invert threshold result");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 100.0, 0.0, 1000000.0, "Minimum contour area");
        _maxArea = AddDoubleProperty("MaxArea", "Max Area", 10000000.0, 0.0, 10000000.0, "Maximum contour area");
        _drawRadius = AddIntProperty("DrawRadius", "Draw Radius", 5, 1, 20, "Center point draw radius");
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
            var useAuto = _useAutoThreshold.GetValue<bool>();
            var threshVal = _thresholdValue.GetValue<int>();
            var invertBinary = _invertBinary.GetValue<bool>();
            var minArea = _minArea.GetValue<double>();
            var maxArea = _maxArea.GetValue<double>();
            var drawRadius = _drawRadius.GetValue<int>();

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

            // Threshold (Otsu or manual)
            using var binary = new Mat();
            if (useAuto)
            {
                var threshType = invertBinary
                    ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                    : ThresholdTypes.Binary | ThresholdTypes.Otsu;
                Cv2.Threshold(blurred, binary, 0, 255, threshType);
            }
            else
            {
                var threshType = invertBinary ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.Threshold(blurred, binary, threshVal, 255, threshType);
            }

            // Morphology open to remove noise
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            using var opened = new Mat();
            Cv2.MorphologyEx(binary, opened, MorphTypes.Open, kernel);

            // Find contours
            Cv2.FindContours(opened, out Point[][] contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Filter by area and compute centroids via moments
            var centerList = new List<Point>();
            var areaList = new List<double>();
            var filteredContours = new List<Point[]>();

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < minArea || area > maxArea) continue;

                var moments = Cv2.Moments(contour);
                if (moments.M00 < 1e-5) continue;

                var cx = (int)(moments.M10 / moments.M00);
                var cy = (int)(moments.M01 / moments.M00);

                centerList.Add(new Point(cx, cy));
                areaList.Add(area);
                filteredContours.Add(contour);
            }

            var centers = centerList.ToArray();
            var areas = areaList.ToArray();
            var count = centers.Length;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw contours (green)
            for (int i = 0; i < count; i++)
            {
                Cv2.DrawContours(result, filteredContours, i, new Scalar(0, 255, 0), 2);

                // Red center dot
                Cv2.Circle(result, centers[i], drawRadius, new Scalar(0, 0, 255), -1);

                // Label with index + coordinates
                var label = $"#{i} ({centers[i].X},{centers[i].Y})";
                Cv2.PutText(result, label, new Point(centers[i].X + drawRadius + 2, centers[i].Y - 5),
                    HersheyFonts.HersheySimplex, 0.45, new Scalar(0, 255, 0), 1);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_centersOutput, centers);
            SetOutputValue(_areasOutput, areas);
            SetOutputValue(_countOutput, count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Contour Center Finder error: {ex.Message}";
        }
    }
}
