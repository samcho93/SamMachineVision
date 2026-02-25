using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Color Object Detector", NodeCategories.Inspection,
    Description = "Detects objects of a specific color and outputs their positions/count")]
public class ColorObjectDetectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Rect[]> _bboxOutput = null!;
    private OutputPort<Point[]> _centersOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _hueLow = null!;
    private NodeProperty _hueHigh = null!;
    private NodeProperty _satLow = null!;
    private NodeProperty _satHigh = null!;
    private NodeProperty _valLow = null!;
    private NodeProperty _valHigh = null!;
    private NodeProperty _minArea = null!;
    private NodeProperty _showMask = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _bboxOutput = AddOutput<Rect[]>("BoundingBoxes");
        _centersOutput = AddOutput<Point[]>("Centers");
        _countOutput = AddOutput<int>("Count");

        _hueLow = AddIntProperty("HueLow", "Hue Low", 100, 0, 179, "Lower hue bound");
        _hueHigh = AddIntProperty("HueHigh", "Hue High", 130, 0, 179, "Upper hue bound");
        _satLow = AddIntProperty("SatLow", "Saturation Low", 50, 0, 255, "Lower saturation");
        _satHigh = AddIntProperty("SatHigh", "Saturation High", 255, 0, 255, "Upper saturation");
        _valLow = AddIntProperty("ValLow", "Value Low", 50, 0, 255, "Lower value");
        _valHigh = AddIntProperty("ValHigh", "Value High", 255, 0, 255, "Upper value");
        _minArea = AddDoubleProperty("MinArea", "Min Area", 500.0, 0.0, 1000000.0, "Minimum object area");
        _showMask = AddBoolProperty("ShowMask", "Show Mask", false, "Show binary mask instead of annotated image");
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

            var hueLow = _hueLow.GetValue<int>();
            var hueHigh = _hueHigh.GetValue<int>();
            var satLow = _satLow.GetValue<int>();
            var satHigh = _satHigh.GetValue<int>();
            var valLow = _valLow.GetValue<int>();
            var valHigh = _valHigh.GetValue<int>();
            var minArea = _minArea.GetValue<double>();
            var showMask = _showMask.GetValue<bool>();

            // BGR → HSV
            using var hsv = new Mat();
            if (image.Channels() == 1)
                Cv2.CvtColor(image, hsv, ColorConversionCodes.GRAY2BGR);
            else
                image.CopyTo(hsv);

            using var hsvConverted = new Mat();
            Cv2.CvtColor(hsv, hsvConverted, ColorConversionCodes.BGR2HSV);

            // InRange
            using var mask = new Mat();
            var lower = new Scalar(hueLow, satLow, valLow);
            var upper = new Scalar(hueHigh, satHigh, valHigh);
            Cv2.InRange(hsvConverted, lower, upper, mask);

            // Morphology close to fill small holes
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
            using var closed = new Mat();
            Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);

            // Find contours
            Cv2.FindContours(closed, out Point[][] contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            // Filter by area and compute bounding rects / centers
            var bboxList = new List<Rect>();
            var centerList = new List<Point>();
            var areaList = new List<double>();

            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                if (area < minArea) continue;

                var rect = Cv2.BoundingRect(contour);
                bboxList.Add(rect);
                centerList.Add(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                areaList.Add(area);
            }

            var bboxes = bboxList.ToArray();
            var centers = centerList.ToArray();
            var count = bboxes.Length;

            // Build result image
            Mat result;
            if (showMask)
            {
                result = closed.Clone();
            }
            else
            {
                result = image.Clone();
                if (result.Channels() == 1)
                    Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

                for (int i = 0; i < count; i++)
                {
                    var rect = bboxes[i];
                    var center = centers[i];

                    // Green bounding box
                    Cv2.Rectangle(result, rect, new Scalar(0, 255, 0), 2);

                    // Red center dot
                    Cv2.Circle(result, center, 4, new Scalar(0, 0, 255), -1);

                    // Label with index and area
                    var label = $"#{i}: {areaList[i]:F0}px";
                    Cv2.PutText(result, label, new Point(rect.X, rect.Y - 5),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
                }
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_bboxOutput, bboxes);
            SetOutputValue(_centersOutput, centers);
            SetOutputValue(_countOutput, count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Color Object Detector error: {ex.Message}";
        }
    }
}
