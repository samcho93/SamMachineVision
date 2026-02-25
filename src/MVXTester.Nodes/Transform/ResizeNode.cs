using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Transform;

[NodeInfo("Resize", NodeCategories.Transform, Description = "Resize image by dimensions or scale")]
public class ResizeNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _scaleX = null!;
    private NodeProperty _scaleY = null!;
    private NodeProperty _interpolation = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _width = AddIntProperty("Width", "Width", 0, 0, 10000, "Target width (0 = use scale)");
        _height = AddIntProperty("Height", "Height", 0, 0, 10000, "Target height (0 = use scale)");
        _scaleX = AddDoubleProperty("ScaleX", "Scale X", 1.0, 0.01, 100.0, "Horizontal scale factor");
        _scaleY = AddDoubleProperty("ScaleY", "Scale Y", 1.0, 0.01, 100.0, "Vertical scale factor");
        _interpolation = AddEnumProperty("Interpolation", "Interpolation", InterpolationFlags.Linear, "Interpolation method");
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

            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            var scaleX = _scaleX.GetValue<double>();
            var scaleY = _scaleY.GetValue<double>();
            var interpolation = _interpolation.GetValue<InterpolationFlags>();

            var result = new Mat();
            if (width > 0 || height > 0)
            {
                Cv2.Resize(image, result, new Size(width, height), 0, 0, interpolation);
            }
            else
            {
                Cv2.Resize(image, result, new Size(), scaleX, scaleY, interpolation);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Resize error: {ex.Message}";
        }
    }
}
