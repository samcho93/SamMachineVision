using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Transform;

[NodeInfo("Rotate", NodeCategories.Transform, Description = "Rotate image by angle")]
public class RotateNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _angle = null!;
    private NodeProperty _centerX = null!;
    private NodeProperty _centerY = null!;
    private NodeProperty _autoCenter = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _angle = AddDoubleProperty("Angle", "Angle", 0.0, -360.0, 360.0, "Rotation angle in degrees");
        _autoCenter = AddBoolProperty("AutoCenter", "Auto Center", true, "Use image center as rotation center");
        _centerX = AddIntProperty("CenterX", "Center X", 0, 0, 10000, "Custom center X");
        _centerY = AddIntProperty("CenterY", "Center Y", 0, 0, 10000, "Custom center Y");
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

            var angle = _angle.GetValue<double>();
            var autoCenter = _autoCenter.GetValue<bool>();

            Point2f center;
            if (autoCenter)
            {
                center = new Point2f(image.Width / 2f, image.Height / 2f);
            }
            else
            {
                center = new Point2f(_centerX.GetValue<int>(), _centerY.GetValue<int>());
            }

            var rotationMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            var result = new Mat();
            Cv2.WarpAffine(image, result, rotationMatrix, image.Size());
            rotationMatrix.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Rotate error: {ex.Message}";
        }
    }
}
