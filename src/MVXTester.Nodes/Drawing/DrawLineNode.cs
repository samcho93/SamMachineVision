using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Line", NodeCategories.Drawing, Description = "Draw a line on an image")]
public class DrawLineNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _pt1X = null!;
    private NodeProperty _pt1Y = null!;
    private NodeProperty _pt2X = null!;
    private NodeProperty _pt2Y = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _pt1X = AddIntProperty("Pt1X", "Point 1 X", 0, 0, 10000, "Start X");
        _pt1Y = AddIntProperty("Pt1Y", "Point 1 Y", 0, 0, 10000, "Start Y");
        _pt2X = AddIntProperty("Pt2X", "Point 2 X", 100, 0, 10000, "End X");
        _pt2Y = AddIntProperty("Pt2Y", "Point 2 Y", 100, 0, 10000, "End Y");
        _colorR = AddIntProperty("ColorR", "Color R", 255, 0, 255);
        _colorG = AddIntProperty("ColorG", "Color G", 0, 0, 255);
        _colorB = AddIntProperty("ColorB", "Color B", 0, 0, 255);
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
            var pt1 = new Point(_pt1X.GetValue<int>(), _pt1Y.GetValue<int>());
            var pt2 = new Point(_pt2X.GetValue<int>(), _pt2Y.GetValue<int>());
            var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
            var thickness = _thickness.GetValue<int>();

            Cv2.Line(result, pt1, pt2, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Line error: {ex.Message}";
        }
    }
}
