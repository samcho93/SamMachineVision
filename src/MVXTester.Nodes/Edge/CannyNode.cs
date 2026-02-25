using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Edge;

[NodeInfo("Canny Edge", NodeCategories.Edge, Description = "Canny edge detection")]
public class CannyNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _threshold1 = null!;
    private NodeProperty _threshold2 = null!;
    private NodeProperty _apertureSize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _threshold1 = AddDoubleProperty("Threshold1", "Threshold 1", 100.0, 0.0, 1000.0, "First threshold");
        _threshold2 = AddDoubleProperty("Threshold2", "Threshold 2", 200.0, 0.0, 1000.0, "Second threshold");
        _apertureSize = AddIntProperty("ApertureSize", "Aperture Size", 3, 3, 7, "Sobel aperture size (3, 5, or 7)");
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

            var threshold1 = _threshold1.GetValue<double>();
            var threshold2 = _threshold2.GetValue<double>();
            var apertureSize = _apertureSize.GetValue<int>();

            // Ensure aperture size is odd and in valid range
            if (apertureSize % 2 == 0) apertureSize++;
            if (apertureSize < 3) apertureSize = 3;
            if (apertureSize > 7) apertureSize = 7;

            var result = new Mat();
            Cv2.Canny(image, result, threshold1, threshold2, apertureSize);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Canny error: {ex.Message}";
        }
    }
}
