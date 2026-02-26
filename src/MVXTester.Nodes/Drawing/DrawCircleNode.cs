using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Circle", NodeCategories.Drawing, Description = "Draw a circle on an image")]
public class DrawCircleNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _centerXInput = null!;
    private InputPort<int> _centerYInput = null!;
    private InputPort<int> _radiusInput = null!;
    private NodeProperty _centerX = null!;
    private NodeProperty _centerY = null!;
    private NodeProperty _radius = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _centerXInput = AddInput<int>("CenterX");
        _centerYInput = AddInput<int>("CenterY");
        _radiusInput = AddInput<int>("Radius");
        _resultOutput = AddOutput<Mat>("Result");
        _centerX = AddIntProperty("CenterX", "Center X", 100, 0, 10000);
        _centerY = AddIntProperty("CenterY", "Center Y", 100, 0, 10000);
        _radius = AddIntProperty("Radius", "Radius", 50, 1, 5000);
        _colorR = AddIntProperty("ColorR", "Color R", 0, 0, 255);
        _colorG = AddIntProperty("ColorG", "Color G", 0, 0, 255);
        _colorB = AddIntProperty("ColorB", "Color B", 255, 0, 255);
        _thickness = AddIntProperty("Thickness", "Thickness", 2, -1, 50, "Thickness (-1 for filled)");
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

            var result = image.Clone();
            var center = new Point(
                GetPortOrProperty(_centerXInput, _centerX),
                GetPortOrProperty(_centerYInput, _centerY));
            var radius = GetPortOrProperty(_radiusInput, _radius);
            var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
            var thickness = _thickness.GetValue<int>();

            Cv2.Circle(result, center, radius, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Circle error: {ex.Message}";
        }
    }
}
