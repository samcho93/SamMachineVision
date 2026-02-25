using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Polylines", NodeCategories.Drawing, Description = "Draw polylines on an image")]
public class DrawPolylinesNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Point[][]> _pointsInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _isClosed = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _pointsInput = AddInput<Point[][]>("Points");
        _resultOutput = AddOutput<Mat>("Result");
        _isClosed = AddBoolProperty("IsClosed", "Closed", true, "Whether the polyline is closed");
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
            var points = GetInputValue(_pointsInput);

            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var result = image.Clone();

            if (points != null && points.Length > 0)
            {
                var isClosed = _isClosed.GetValue<bool>();
                var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
                var thickness = _thickness.GetValue<int>();

                Cv2.Polylines(result, points, isClosed, color, thickness);
            }

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Polylines error: {ex.Message}";
        }
    }
}
