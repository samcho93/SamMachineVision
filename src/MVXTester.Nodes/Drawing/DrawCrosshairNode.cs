using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Crosshair", NodeCategories.Drawing, Description = "Draw crosshair/reticle overlay on an image")]
public class DrawCrosshairNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _centerXInput = null!;
    private InputPort<int> _centerYInput = null!;
    private InputPort<int> _sizeInput = null!;
    private NodeProperty _centerX = null!;
    private NodeProperty _centerY = null!;
    private NodeProperty _size = null!;
    private NodeProperty _thickness = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _centerXInput = AddInput<int>("CenterX");
        _centerYInput = AddInput<int>("CenterY");
        _sizeInput = AddInput<int>("Size");
        _resultOutput = AddOutput<Mat>("Result");
        _centerX = AddIntProperty("CenterX", "Center X", -1, -1, 10000, "X position (-1 for auto center)");
        _centerY = AddIntProperty("CenterY", "Center Y", -1, -1, 10000, "Y position (-1 for auto center)");
        _size = AddIntProperty("Size", "Size", 50, 10, 1000, "Crosshair arm length");
        _thickness = AddIntProperty("Thickness", "Thickness", 1, 1, 5, "Line thickness");
        _colorR = AddIntProperty("ColorR", "Color R", 0, 0, 255, "Red component");
        _colorG = AddIntProperty("ColorG", "Color G", 255, 0, 255, "Green component");
        _colorB = AddIntProperty("ColorB", "Color B", 0, 0, 255, "Blue component");
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

            var cx = GetPortOrProperty(_centerXInput, _centerX);
            var cy = GetPortOrProperty(_centerYInput, _centerY);
            var size = GetPortOrProperty(_sizeInput, _size);
            var thickness = _thickness.GetValue<int>();
            var r = _colorR.GetValue<int>();
            var g = _colorG.GetValue<int>();
            var b = _colorB.GetValue<int>();

            // Auto center if -1
            if (cx < 0) cx = image.Width / 2;
            if (cy < 0) cy = image.Height / 2;

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var color = new Scalar(b, g, r);

            // Draw horizontal line through center
            Cv2.Line(result, new Point(cx - size, cy), new Point(cx + size, cy), color, thickness);
            // Draw vertical line through center
            Cv2.Line(result, new Point(cx, cy - size), new Point(cx, cy + size), color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Crosshair error: {ex.Message}";
        }
    }
}
