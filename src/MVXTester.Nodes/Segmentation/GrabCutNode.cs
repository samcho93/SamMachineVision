using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Segmentation;

[NodeInfo("GrabCut", NodeCategories.Segmentation, Description = "GrabCut foreground segmentation")]
public class GrabCutNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _maskOutput = null!;
    private NodeProperty _x = null!;
    private NodeProperty _y = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _iterations = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _maskOutput = AddOutput<Mat>("Mask");
        _x = AddIntProperty("X", "Rect X", 10, 0, 10000, "ROI X");
        _y = AddIntProperty("Y", "Rect Y", 10, 0, 10000, "ROI Y");
        _width = AddIntProperty("Width", "Rect Width", 100, 1, 10000, "ROI Width");
        _height = AddIntProperty("Height", "Rect Height", 100, 1, 10000, "ROI Height");
        _iterations = AddIntProperty("Iterations", "Iterations", 5, 1, 50, "Number of iterations");
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

            if (image.Channels() != 3)
            {
                Error = "GrabCut requires a 3-channel (BGR) image";
                return;
            }

            var rect = new Rect(
                _x.GetValue<int>(),
                _y.GetValue<int>(),
                _width.GetValue<int>(),
                _height.GetValue<int>()
            );
            var iterations = _iterations.GetValue<int>();

            // Clamp rect to image bounds
            rect.X = Math.Max(0, Math.Min(rect.X, image.Width - 2));
            rect.Y = Math.Max(0, Math.Min(rect.Y, image.Height - 2));
            rect.Width = Math.Min(rect.Width, image.Width - rect.X);
            rect.Height = Math.Min(rect.Height, image.Height - rect.Y);

            var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.All(0));
            var bgModel = new Mat();
            var fgModel = new Mat();

            Cv2.GrabCut(image, mask, rect, bgModel, fgModel, iterations, GrabCutModes.InitWithRect);

            bgModel.Dispose();
            fgModel.Dispose();

            // Create binary mask (foreground = 255)
            // GC_PR_FGD = 3, GC_FGD = 1
            var resultMask = new Mat();
            Cv2.Compare(mask, new Scalar(3), resultMask, CmpType.EQ);

            var fgMask = new Mat();
            Cv2.Compare(mask, new Scalar(1), fgMask, CmpType.EQ);
            Cv2.BitwiseOr(resultMask, fgMask, resultMask);

            mask.Dispose();
            fgMask.Dispose();

            SetOutputValue(_maskOutput, resultMask);
            SetPreview(resultMask);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"GrabCut error: {ex.Message}";
        }
    }
}
