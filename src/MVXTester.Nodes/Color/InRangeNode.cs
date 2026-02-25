using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Color;

[NodeInfo("In Range", NodeCategories.Color, Description = "HSV/color range filtering")]
public class InRangeNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _maskOutput = null!;
    private NodeProperty _lowerH = null!;
    private NodeProperty _lowerS = null!;
    private NodeProperty _lowerV = null!;
    private NodeProperty _upperH = null!;
    private NodeProperty _upperS = null!;
    private NodeProperty _upperV = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _maskOutput = AddOutput<Mat>("Mask");
        _lowerH = AddIntProperty("LowerH", "Lower H", 0, 0, 255, "Lower hue bound");
        _lowerS = AddIntProperty("LowerS", "Lower S", 0, 0, 255, "Lower saturation bound");
        _lowerV = AddIntProperty("LowerV", "Lower V", 0, 0, 255, "Lower value bound");
        _upperH = AddIntProperty("UpperH", "Upper H", 180, 0, 255, "Upper hue bound");
        _upperS = AddIntProperty("UpperS", "Upper S", 255, 0, 255, "Upper saturation bound");
        _upperV = AddIntProperty("UpperV", "Upper V", 255, 0, 255, "Upper value bound");
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

            var lowerH = _lowerH.GetValue<int>();
            var lowerS = _lowerS.GetValue<int>();
            var lowerV = _lowerV.GetValue<int>();
            var upperH = _upperH.GetValue<int>();
            var upperS = _upperS.GetValue<int>();
            var upperV = _upperV.GetValue<int>();

            var lower = new Scalar(lowerH, lowerS, lowerV);
            var upper = new Scalar(upperH, upperS, upperV);

            var mask = new Mat();
            Cv2.InRange(image, lower, upper, mask);

            SetOutputValue(_maskOutput, mask);
            SetPreview(mask);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"InRange error: {ex.Message}";
        }
    }
}
