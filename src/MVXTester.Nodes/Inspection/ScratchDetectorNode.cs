using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

public enum ScratchDetectMode
{
    DarkOnLight,
    LightOnDark
}

[NodeInfo("Scratch Detector", NodeCategories.Inspection, Description = "Detects linear scratches and defects on surfaces")]
public class ScratchDetectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Mat> _scratchMaskOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<double> _totalLengthOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _morphKernelSize = null!;
    private NodeProperty _threshold = null!;
    private NodeProperty _minLength = null!;
    private NodeProperty _minElongation = null!;
    private NodeProperty _detectMode = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _scratchMaskOutput = AddOutput<Mat>("ScratchMask");
        _countOutput = AddOutput<int>("Count");
        _totalLengthOutput = AddOutput<double>("TotalLength");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 3, 1, 31, "Gaussian blur kernel size");
        _morphKernelSize = AddIntProperty("MorphKernelSize", "Morph Kernel Size", 15, 3, 51, "Morphology kernel size (larger detects wider scratches)");
        _threshold = AddIntProperty("Threshold", "Threshold", 30, 1, 255, "Sensitivity threshold");
        _minLength = AddDoubleProperty("MinLength", "Min Length", 30.0, 10.0, 10000.0, "Minimum scratch length");
        _minElongation = AddDoubleProperty("MinElongation", "Min Elongation", 3.0, 1.0, 100.0, "Minimum length/width ratio to be considered a scratch");
        _detectMode = AddEnumProperty("DetectMode", "Detect Mode", ScratchDetectMode.DarkOnLight, "Dark scratches on light surface or light scratches on dark surface");
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
            var morphKernelSize = _morphKernelSize.GetValue<int>();
            var threshold = _threshold.GetValue<int>();
            var minLength = _minLength.GetValue<double>();
            var minElongation = _minElongation.GetValue<double>();
            var detectMode = _detectMode.GetValue<ScratchDetectMode>();

            // Ensure odd sizes
            if (blurSize % 2 == 0) blurSize++;
            if (blurSize < 1) blurSize = 1;
            if (morphKernelSize % 2 == 0) morphKernelSize++;
            if (morphKernelSize < 3) morphKernelSize = 3;

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

            // Step 3: Morphology TopHat or BlackHat to highlight thin features
            var morphResult = new Mat();
            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(morphKernelSize, morphKernelSize));

            if (detectMode == ScratchDetectMode.DarkOnLight)
            {
                // BlackHat: highlights dark thin features on light background
                Cv2.MorphologyEx(blurred, morphResult, MorphTypes.BlackHat, kernel);
            }
            else
            {
                // TopHat: highlights light thin features on dark background
                Cv2.MorphologyEx(blurred, morphResult, MorphTypes.TopHat, kernel);
            }
            kernel.Dispose();
            blurred.Dispose();

            // Step 4: Threshold the morphology result
            var binary = new Mat();
            Cv2.Threshold(morphResult, binary, threshold, 255, ThresholdTypes.Binary);
            morphResult.Dispose();

            // Step 5: Find contours and filter by elongation
            Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var scratchContours = new List<Point[]>();
            double totalLength = 0;

            foreach (var contour in contours)
            {
                if (contour.Length < 5) continue;

                // Use min-area rotated rectangle to measure elongation
                var rotatedRect = Cv2.MinAreaRect(contour);
                double length = Math.Max(rotatedRect.Size.Width, rotatedRect.Size.Height);
                double width = Math.Min(rotatedRect.Size.Width, rotatedRect.Size.Height);

                if (width < 1) width = 1; // Avoid division by zero
                double elongation = length / width;

                if (length >= minLength && elongation >= minElongation)
                {
                    scratchContours.Add(contour);
                    totalLength += length;
                }
            }

            int count = scratchContours.Count;

            // Create scratch mask
            var scratchMask = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0));
            if (scratchContours.Count > 0)
                Cv2.DrawContours(scratchMask, scratchContours, -1, new Scalar(255), -1);

            // Build result visualization: overlay detected scratches in red
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw scratch contours in red with fill overlay
            if (scratchContours.Count > 0)
            {
                using var overlay = result.Clone();
                Cv2.DrawContours(overlay, scratchContours, -1, new Scalar(0, 0, 255), -1);
                Cv2.AddWeighted(overlay, 0.4, result, 0.6, 0, result);

                // Draw contour outlines
                Cv2.DrawContours(result, scratchContours, -1, new Scalar(0, 0, 255), 2);
            }

            // Draw summary text
            Cv2.PutText(result, $"Scratches: {count}  Total Length: {totalLength:F0}px", new Point(10, 30),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 255), 2);

            binary.Dispose();

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_scratchMaskOutput, scratchMask);
            SetOutputValue(_countOutput, count);
            SetOutputValue(_totalLengthOutput, totalLength);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Scratch Detector error: {ex.Message}";
        }
    }
}
