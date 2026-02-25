using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Transform;

[NodeInfo("Warp Perspective", NodeCategories.Transform, Description = "Apply perspective transformation using 4 point pairs")]
public class WarpPerspectiveNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _srcX0 = null!;
    private NodeProperty _srcY0 = null!;
    private NodeProperty _srcX1 = null!;
    private NodeProperty _srcY1 = null!;
    private NodeProperty _srcX2 = null!;
    private NodeProperty _srcY2 = null!;
    private NodeProperty _srcX3 = null!;
    private NodeProperty _srcY3 = null!;
    private NodeProperty _dstX0 = null!;
    private NodeProperty _dstY0 = null!;
    private NodeProperty _dstX1 = null!;
    private NodeProperty _dstY1 = null!;
    private NodeProperty _dstX2 = null!;
    private NodeProperty _dstY2 = null!;
    private NodeProperty _dstX3 = null!;
    private NodeProperty _dstY3 = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _srcX0 = AddIntProperty("SrcX0", "Src X0", 0, 0, 10000);
        _srcY0 = AddIntProperty("SrcY0", "Src Y0", 0, 0, 10000);
        _srcX1 = AddIntProperty("SrcX1", "Src X1", 100, 0, 10000);
        _srcY1 = AddIntProperty("SrcY1", "Src Y1", 0, 0, 10000);
        _srcX2 = AddIntProperty("SrcX2", "Src X2", 100, 0, 10000);
        _srcY2 = AddIntProperty("SrcY2", "Src Y2", 100, 0, 10000);
        _srcX3 = AddIntProperty("SrcX3", "Src X3", 0, 0, 10000);
        _srcY3 = AddIntProperty("SrcY3", "Src Y3", 100, 0, 10000);
        _dstX0 = AddIntProperty("DstX0", "Dst X0", 10, 0, 10000);
        _dstY0 = AddIntProperty("DstY0", "Dst Y0", 10, 0, 10000);
        _dstX1 = AddIntProperty("DstX1", "Dst X1", 90, 0, 10000);
        _dstY1 = AddIntProperty("DstY1", "Dst Y1", 10, 0, 10000);
        _dstX2 = AddIntProperty("DstX2", "Dst X2", 90, 0, 10000);
        _dstY2 = AddIntProperty("DstY2", "Dst Y2", 90, 0, 10000);
        _dstX3 = AddIntProperty("DstX3", "Dst X3", 10, 0, 10000);
        _dstY3 = AddIntProperty("DstY3", "Dst Y3", 90, 0, 10000);
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

            var srcPoints = new[]
            {
                new Point2f(_srcX0.GetValue<int>(), _srcY0.GetValue<int>()),
                new Point2f(_srcX1.GetValue<int>(), _srcY1.GetValue<int>()),
                new Point2f(_srcX2.GetValue<int>(), _srcY2.GetValue<int>()),
                new Point2f(_srcX3.GetValue<int>(), _srcY3.GetValue<int>())
            };

            var dstPoints = new[]
            {
                new Point2f(_dstX0.GetValue<int>(), _dstY0.GetValue<int>()),
                new Point2f(_dstX1.GetValue<int>(), _dstY1.GetValue<int>()),
                new Point2f(_dstX2.GetValue<int>(), _dstY2.GetValue<int>()),
                new Point2f(_dstX3.GetValue<int>(), _dstY3.GetValue<int>())
            };

            var matrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);
            var result = new Mat();
            Cv2.WarpPerspective(image, result, matrix, image.Size());
            matrix.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Warp Perspective error: {ex.Message}";
        }
    }
}
