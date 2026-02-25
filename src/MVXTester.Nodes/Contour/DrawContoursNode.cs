using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Draw Contours", NodeCategories.Contour, Description = "Draw contours on an image")]
public class DrawContoursNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;
    private NodeProperty _index = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _contoursInput = AddInput<Point[][]>("Contours");
        _resultOutput = AddOutput<Mat>("Result");
        _colorR = AddIntProperty("ColorR", "Color R", 0, 0, 255, "Red component");
        _colorG = AddIntProperty("ColorG", "Color G", 255, 0, 255, "Green component");
        _colorB = AddIntProperty("ColorB", "Color B", 0, 0, 255, "Blue component");
        _thickness = AddIntProperty("Thickness", "Thickness", 2, 1, 50, "Line thickness");
        _index = AddIntProperty("Index", "Contour Index", -1, -1, 10000, "Contour index (-1 for all)");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            var contours = GetInputValue(_contoursInput);

            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            if (contours == null || contours.Length == 0)
            {
                SetOutputValue(_resultOutput, image.Clone());
                SetPreview(image);
                Error = null;
                return;
            }

            var r = _colorR.GetValue<int>();
            var g = _colorG.GetValue<int>();
            var b = _colorB.GetValue<int>();
            var thickness = _thickness.GetValue<int>();
            var index = _index.GetValue<int>();

            var result = image.Clone();
            var color = new Scalar(b, g, r); // OpenCV uses BGR
            Cv2.DrawContours(result, contours, index, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Contours error: {ex.Message}";
        }
    }
}
