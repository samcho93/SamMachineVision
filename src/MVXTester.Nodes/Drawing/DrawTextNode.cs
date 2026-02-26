using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Text", NodeCategories.Drawing, Description = "Draw text on an image")]
public class DrawTextNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _posXInput = null!;
    private InputPort<int> _posYInput = null!;
    private InputPort<string> _textInput = null!;
    private NodeProperty _text = null!;
    private NodeProperty _posX = null!;
    private NodeProperty _posY = null!;
    private NodeProperty _font = null!;
    private NodeProperty _scale = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _posXInput = AddInput<int>("PosX");
        _posYInput = AddInput<int>("PosY");
        _textInput = AddInput<string>("Text");
        _resultOutput = AddOutput<Mat>("Result");
        _text = AddStringProperty("Text", "Text", "Hello", "Text to draw");
        _posX = AddIntProperty("PosX", "Position X", 10, 0, 10000);
        _posY = AddIntProperty("PosY", "Position Y", 30, 0, 10000);
        _font = AddEnumProperty("Font", "Font", HersheyFonts.HersheySimplex, "Font face");
        _scale = AddDoubleProperty("Scale", "Scale", 1.0, 0.1, 20.0, "Font scale");
        _colorR = AddIntProperty("ColorR", "Color R", 255, 0, 255);
        _colorG = AddIntProperty("ColorG", "Color G", 255, 0, 255);
        _colorB = AddIntProperty("ColorB", "Color B", 255, 0, 255);
        _thickness = AddIntProperty("Thickness", "Thickness", 2, 1, 50);
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
            var text = GetPortOrPropertyString(_textInput, _text);
            var pos = new Point(
                GetPortOrProperty(_posXInput, _posX),
                GetPortOrProperty(_posYInput, _posY));
            var font = _font.GetValue<HersheyFonts>();
            var scale = _scale.GetValue<double>();
            var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
            var thickness = _thickness.GetValue<int>();

            Cv2.PutText(result, text, pos, font, scale, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Text error: {ex.Message}";
        }
    }
}
