using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Drawing;

[NodeInfo("Draw Ellipse", NodeCategories.Drawing, Description = "Draw an ellipse on an image")]
public class DrawEllipseNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private InputPort<int> _centerXInput = null!;
    private InputPort<int> _centerYInput = null!;
    private InputPort<int> _axisWInput = null!;
    private InputPort<int> _axisHInput = null!;
    private InputPort<double> _angleInput = null!;
    private NodeProperty _centerX = null!;
    private NodeProperty _centerY = null!;
    private NodeProperty _axisW = null!;
    private NodeProperty _axisH = null!;
    private NodeProperty _angle = null!;
    private NodeProperty _colorR = null!;
    private NodeProperty _colorG = null!;
    private NodeProperty _colorB = null!;
    private NodeProperty _thickness = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _centerXInput = AddInput<int>("CenterX");
        _centerYInput = AddInput<int>("CenterY");
        _axisWInput = AddInput<int>("AxisW");
        _axisHInput = AddInput<int>("AxisH");
        _angleInput = AddInput<double>("Angle");
        _resultOutput = AddOutput<Mat>("Result");
        _centerX = AddIntProperty("CenterX", "Center X", 100, 0, 10000);
        _centerY = AddIntProperty("CenterY", "Center Y", 100, 0, 10000);
        _axisW = AddIntProperty("AxisW", "Axis Width", 80, 1, 5000);
        _axisH = AddIntProperty("AxisH", "Axis Height", 50, 1, 5000);
        _angle = AddDoubleProperty("Angle", "Angle", 0.0, 0.0, 360.0, "Rotation angle in degrees");
        _colorR = AddIntProperty("ColorR", "Color R", 255, 0, 255);
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
            var center = new Point(
                GetPortOrProperty(_centerXInput, _centerX),
                GetPortOrProperty(_centerYInput, _centerY));
            var axes = new Size(
                GetPortOrProperty(_axisWInput, _axisW),
                GetPortOrProperty(_axisHInput, _axisH));
            var angle = GetPortOrProperty(_angleInput, _angle);
            var color = new Scalar(_colorB.GetValue<int>(), _colorG.GetValue<int>(), _colorR.GetValue<int>());
            var thickness = _thickness.GetValue<int>();

            Cv2.Ellipse(result, center, axes, angle, 0, 360, color, thickness);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Draw Ellipse error: {ex.Message}";
        }
    }
}
