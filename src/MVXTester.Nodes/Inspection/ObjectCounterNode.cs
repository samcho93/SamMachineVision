using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Object Counter", NodeCategories.Inspection, Description = "Counts objects in an image and outputs count, centers, and areas")]
public class ObjectCounterNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<Point[]> _centersOutput = null!;
    private OutputPort<double[]> _areasOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _useAdaptive = null!;
    private NodeProperty _blockSize = null!;
    private NodeProperty _constant = null!;
    private NodeProperty _invertBinary = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _maxArea = null!;
    private NodeProperty _morphSize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _countOutput = AddOutput<int>("Count");
        _centersOutput = AddOutput<Point[]>("Centers");
        _areasOutput = AddOutput<double[]>("Areas");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Gaussian blur kernel size");
        _useAdaptive = AddBoolProperty("UseAdaptiveThreshold", "Use Adaptive Threshold", false, "Use adaptive threshold instead of Otsu");
        _blockSize = AddIntProperty("BlockSize", "Block Size", 11, 3, 201, "Adaptive threshold block size");
        _constant = AddDoubleProperty("Constant", "Constant", 2.0, -50.0, 50.0, "Adaptive threshold constant");
        _invertBinary = AddBoolProperty("InvertBinary", "Invert Binary", true, "Invert binary (dark objects on light background)");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 200.0, 0.0, 1000000.0, "Minimum object area");
        _maxArea = AddDoubleProperty("MaxArea", "Max Area", 10000000.0, 0.0, 10000000.0, "Maximum object area");
        _morphSize = AddIntProperty("MorphSize", "Morph Kernel Size", 3, 0, 21, "Morphology kernel size (0 = skip)");
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
            var useAdaptive = _useAdaptive.GetValue<bool>();
            var blockSize = _blockSize.GetValue<int>();
            var constant = _constant.GetValue<double>();
            var invertBinary = _invertBinary.GetValue<bool>();
            var minArea = _minArea.GetValue<double>();
            var maxArea = _maxArea.GetValue<double>();
            var morphSize = _morphSize.GetValue<int>();

            // Ensure odd blur size
            if (blurSize % 2 == 0) blurSize++;
            if (blurSize < 1) blurSize = 1;

            // Ensure odd block size
            if (blockSize % 2 == 0) blockSize++;
            if (blockSize < 3) blockSize = 3;

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
            if (useAdaptive)
            {
                var adaptiveType = invertBinary
                    ? AdaptiveThresholdTypes.GaussianC
                    : AdaptiveThresholdTypes.GaussianC;
                var threshType = invertBinary ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.AdaptiveThreshold(blurred, binary, 255, adaptiveType, threshType, blockSize, constant);
            }
            else
            {
                var threshType = invertBinary
                    ? ThresholdTypes.BinaryInv | ThresholdTypes.Otsu
                    : ThresholdTypes.Binary | ThresholdTypes.Otsu;
                Cv2.Threshold(blurred, binary, 0, 255, threshType);
            }
            blurred.Dispose();

            // Step 4: Morphology close (fill small gaps)
            if (morphSize > 0)
            {
                var mSize = morphSize % 2 == 0 ? morphSize + 1 : morphSize;
                var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(mSize, mSize));
                Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
                kernel.Dispose();
            }

            // Step 5: Find contours
            Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            binary.Dispose();

            // Step 6: Filter by area and collect results
            var centers = new List<Point>();
            var areas = new List<double>();
            var filteredContours = new List<Point[]>();

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area >= minArea && area <= maxArea)
                {
                    filteredContours.Add(contour);
                    areas.Add(area);

                    var moments = Cv2.Moments(contour);
                    if (moments.M00 > 0)
                    {
                        int cx = (int)(moments.M10 / moments.M00);
                        int cy = (int)(moments.M01 / moments.M00);
                        centers.Add(new Point(cx, cy));
                    }
                    else
                    {
                        var rect = Cv2.BoundingRect(contour);
                        centers.Add(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                    }
                }
            }

            int count = filteredContours.Count;

            // Build result visualization
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw contours
            Cv2.DrawContours(result, filteredContours, -1, new Scalar(0, 255, 0), 2);

            // Draw numbered labels at each center
            for (int i = 0; i < centers.Count; i++)
            {
                var center = centers[i];
                string label = $"#{i + 1}";

                Cv2.Circle(result, center, 4, new Scalar(0, 0, 255), -1);
                Cv2.PutText(result, label, new Point(center.X + 5, center.Y - 5),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1);
            }

            // Draw total count at top-left
            Cv2.PutText(result, $"Count: {count}", new Point(10, 30),
                HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 255), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_countOutput, count);
            SetOutputValue(_centersOutput, centers.ToArray());
            SetOutputValue(_areasOutput, areas.ToArray());
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Object Counter error: {ex.Message}";
        }
    }
}
