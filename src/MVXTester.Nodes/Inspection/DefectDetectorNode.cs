using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Defect Detector", NodeCategories.Inspection,
    Description = "Compares image against a reference to find defects/differences")]
public class DefectDetectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Mat> _referenceInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Mat> _diffMaskOutput = null!;
    private OutputPort<Rect[]> _defectsOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _blurSize = null!;
    private NodeProperty _threshold = null!;
    private NodeProperty _minDefectArea = null!;
    private NodeProperty _morphKernel = null!;
    private NodeProperty _showOverlay = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _referenceInput = AddInput<Mat>("Reference");

        _resultOutput = AddOutput<Mat>("Result");
        _diffMaskOutput = AddOutput<Mat>("DiffMask");
        _defectsOutput = AddOutput<Rect[]>("Defects");
        _countOutput = AddOutput<int>("Count");

        _blurSize = AddIntProperty("BlurSize", "Blur Size", 5, 1, 51, "Blur before diff comparison");
        _threshold = AddIntProperty("Threshold", "Threshold", 30, 1, 255, "Difference threshold");
        _minDefectArea = AddDoubleProperty("MinDefectArea", "Min Defect Area", 50.0, 0.0, 100000.0, "Minimum defect area in pixels");
        _morphKernel = AddIntProperty("MorphKernel", "Morph Kernel", 3, 1, 31, "Morphology kernel size for noise removal");
        _showOverlay = AddBoolProperty("ShowOverlay", "Show Overlay", true, "Overlay defects on original image");
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

            var reference = GetInputValue(_referenceInput);
            if (reference == null || reference.Empty())
            {
                Error = "No reference image";
                return;
            }

            // Validate same size
            if (image.Size() != reference.Size())
            {
                Error = $"Image size ({image.Cols}x{image.Rows}) does not match reference size ({reference.Cols}x{reference.Rows})";
                return;
            }

            var blurSize = _blurSize.GetValue<int>();
            var threshVal = _threshold.GetValue<int>();
            var minDefectArea = _minDefectArea.GetValue<double>();
            var morphKernelSize = _morphKernel.GetValue<int>();
            var showOverlay = _showOverlay.GetValue<bool>();

            // Ensure odd sizes
            if (blurSize % 2 == 0) blurSize++;
            if (morphKernelSize % 2 == 0) morphKernelSize++;

            // Convert both to grayscale
            using var grayImage = new Mat();
            using var grayRef = new Mat();

            if (image.Channels() > 1)
                Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(grayImage);

            if (reference.Channels() > 1)
                Cv2.CvtColor(reference, grayRef, ColorConversionCodes.BGR2GRAY);
            else
                reference.CopyTo(grayRef);

            // Absolute difference
            using var diff = new Mat();
            Cv2.Absdiff(grayImage, grayRef, diff);

            // Gaussian blur to reduce noise
            using var blurred = new Mat();
            Cv2.GaussianBlur(diff, blurred, new Size(blurSize, blurSize), 0);

            // Threshold
            using var binary = new Mat();
            Cv2.Threshold(blurred, binary, threshVal, 255, ThresholdTypes.Binary);

            // Morphology open then close to clean up noise
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(morphKernelSize, morphKernelSize));
            using var morphed = new Mat();
            Cv2.MorphologyEx(binary, morphed, MorphTypes.Open, kernel);
            using var morphedClosed = new Mat();
            Cv2.MorphologyEx(morphed, morphedClosed, MorphTypes.Close, kernel);

            // Find contours for defect regions
            Cv2.FindContours(morphedClosed, out Point[][] contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Filter by area
            var defectList = new List<Rect>();

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < minDefectArea) continue;

                defectList.Add(Cv2.BoundingRect(contour));
            }

            var defects = defectList.ToArray();
            var count = defects.Length;

            // Output diff mask
            var diffMask = morphedClosed.Clone();

            // Build result image
            Mat result;
            if (showOverlay)
            {
                result = image.Clone();
                if (result.Channels() == 1)
                    Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

                // Draw semi-transparent red overlay on defect regions
                using var overlay = result.Clone();
                foreach (var defect in defects)
                {
                    Cv2.Rectangle(overlay, defect, new Scalar(0, 0, 255), -1);
                }
                Cv2.AddWeighted(overlay, 0.3, result, 0.7, 0, result);

                // Draw defect rectangles with border
                for (int i = 0; i < count; i++)
                {
                    Cv2.Rectangle(result, defects[i], new Scalar(0, 0, 255), 2);
                }

                // Draw count text
                var countText = $"Defects: {count}";
                Cv2.PutText(result, countText, new Point(10, 30),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 0, 255), 2);
            }
            else
            {
                result = morphedClosed.Clone();
                if (result.Channels() == 1)
                    Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

                // Draw count text on mask
                var countText = $"Defects: {count}";
                Cv2.PutText(result, countText, new Point(10, 30),
                    HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 0, 255), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_diffMaskOutput, diffMask);
            SetOutputValue(_defectsOutput, defects);
            SetOutputValue(_countOutput, count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Defect Detector error: {ex.Message}";
        }
    }
}
