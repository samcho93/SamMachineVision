using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Rectangle", NodeCategories.Drawing, Description = "Draw a rectangle on an image")]
public class DrawRectangleNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _xInput = null!;
    private InputPort<int> _yInput = null!;
    private InputPort<int> _widthInput = null!;
    private InputPort<int> _heightInput = null!;
    private NodeProperty _x = null!;
    private NodeProperty _y = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _xInput = AddInput<int>("X");
        _yInput = AddInput<int>("Y");
        _widthInput = AddInput<int>("Width");
        _heightInput = AddInput<int>("Height");
        _resultOutput = AddOutput<Mat>("Result");
        _x = AddIntProperty("X", "X", 10, 0, 10000);
        _y = AddIntProperty("Y", "Y", 10, 0, 10000);
        _width = AddIntProperty("Width", "Width", 100, 1, 10000);
        _height = AddIntProperty("Height", "Height", 100, 1, 10000);
        _colorR = AddIntProperty("ColorR", "Color R", 0, 0, 255);
        _colorG = AddIntProperty("ColorG", "Color G", 255, 0, 255);
        _colorB = AddIntProperty("ColorB", "Color B", 0, 0, 255);
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
            var rect = new Rect(
                GetPortOrProperty(_xInput, _x),
                GetPortOrProperty(_yInput, _y),
                GetPortOrProperty(_widthInput, _width),
                GetPortOrProperty(_heightInput, _height));
            var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
            var thickness = _thickness.GetValue<int>();

            Cv2.Rectangle(result, rect, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Rectangle error: {ex.Message}";
        }
    }
}
