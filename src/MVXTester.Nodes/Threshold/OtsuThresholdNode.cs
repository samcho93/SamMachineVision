using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Threshold;

[NodeInfo("Otsu Threshold", NodeCategories.Threshold, Description = "Apply Otsu's automatic threshold")]
public class OtsuThresholdNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _maxVal = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _maxVal = AddDoubleProperty("MaxVal", "Max Value", 255.0, 0.0, 255.0, "Maximum value");
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

            // Ensure grayscale
            var gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var maxVal = _maxVal.GetValue<double>();
            var result = new Mat();
            Cv2.Threshold(gray, result, 0, maxVal, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            if (needDispose) gray.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Otsu Threshold error: {ex.Message}";
        }
    }
}
