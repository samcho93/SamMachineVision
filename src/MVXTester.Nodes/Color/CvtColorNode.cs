using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Color;

[NodeInfo("Convert Color", NodeCategories.Color, Description = "Color space conversion (BGR2GRAY, BGR2HSV, etc.)")]
public class CvtColorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _conversionCode = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _conversionCode = AddEnumProperty("ConversionCode", "Conversion", ColorConversionCodes.BGR2GRAY, "Color conversion code");
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

            var code = _conversionCode.GetValue<ColorConversionCodes>();
            var result = new Mat();
            Cv2.CvtColor(image, result, code);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"CvtColor error: {ex.Message}";
        }
    }
}
