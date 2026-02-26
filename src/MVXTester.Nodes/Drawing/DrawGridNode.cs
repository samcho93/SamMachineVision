using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Grid", NodeCategories.Drawing, Description = "Draw grid overlay on an image")]
public class DrawGridNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _cellWidthInput = null!;
    private InputPort<int> _cellHeightInput = null!;
    private NodeProperty _cellWidth = null!;
    private NodeProperty _cellHeight = null!;
    private NodeProperty _thickness = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _cellWidthInput = AddInput<int>("CellWidth");
        _cellHeightInput = AddInput<int>("CellHeight");
        _resultOutput = AddOutput<Mat>("Result");
        _cellWidth = AddIntProperty("CellWidth", "Cell Width", 50, 5, 500, "Grid cell width in pixels");
        _cellHeight = AddIntProperty("CellHeight", "Cell Height", 50, 5, 500, "Grid cell height in pixels");
        _thickness = AddIntProperty("Thickness", "Thickness", 1, 1, 5, "Line thickness");
        _colorR = AddIntProperty("ColorR", "Color R", 128, 0, 255, "Red component");
        _colorG = AddIntProperty("ColorG", "Color G", 128, 0, 255, "Green component");
        _colorB = AddIntProperty("ColorB", "Color B", 128, 0, 255, "Blue component");
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

            var cellWidth = GetPortOrProperty(_cellWidthInput, _cellWidth);
            var cellHeight = GetPortOrProperty(_cellHeightInput, _cellHeight);
            var thickness = _thickness.GetValue<int>();
            var r = _colorR.GetValue<int>();
            var g = _colorG.GetValue<int>();
            var b = _colorB.GetValue<int>();

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var color = new Scalar(b, g, r);

            // Draw vertical lines
            for (int x = cellWidth; x < result.Width; x += cellWidth)
            {
                Cv2.Line(result, new Point(x, 0), new Point(x, result.Height - 1), color, thickness);
            }

            // Draw horizontal lines
            for (int y = cellHeight; y < result.Height; y += cellHeight)
            {
                Cv2.Line(result, new Point(0, y), new Point(result.Width - 1, y), color, thickness);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Grid error: {ex.Message}";
        }
    }
}
