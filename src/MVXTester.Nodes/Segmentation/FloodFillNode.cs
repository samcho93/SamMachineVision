using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Segmentation;

[NodeInfo("Flood Fill", NodeCategories.Segmentation, Description = "Flood fill from a seed point")]
public class FloodFillNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _seedX = null!;
    private NodeProperty _seedY = null!;
    private NodeProperty _newValR = null!;
    private NodeProperty _newValG = null!;
    private NodeProperty _newValB = null!;
    private NodeProperty _loDiff = null!;
    private NodeProperty _upDiff = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _seedX = AddIntProperty("SeedX", "Seed X", 0, 0, 10000, "Seed point X");
        _seedY = AddIntProperty("SeedY", "Seed Y", 0, 0, 10000, "Seed point Y");
        _newValR = AddIntProperty("NewValR", "New Value R", 255, 0, 255);
        _newValG = AddIntProperty("NewValG", "New Value G", 0, 0, 255);
        _newValB = AddIntProperty("NewValB", "New Value B", 0, 0, 255);
        _loDiff = AddIntProperty("LoDiff", "Lo Diff", 20, 0, 255, "Lower brightness/color difference");
        _upDiff = AddIntProperty("UpDiff", "Up Diff", 20, 0, 255, "Upper brightness/color difference");
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

            var seedX = _seedX.GetValue<int>();
            var seedY = _seedY.GetValue<int>();

            if (seedX >= image.Width || seedY >= image.Height)
            {
                Error = "Seed point is outside image bounds";
                return;
            }

            var result = image.Clone();
            var newVal = new Scalar(_newValB.GetValue<int>(), _newValG.GetValue<int>(), _newValR.GetValue<int>());
            var loDiff = new Scalar(_loDiff.GetValue<int>(), _loDiff.GetValue<int>(), _loDiff.GetValue<int>());
            var upDiff = new Scalar(_upDiff.GetValue<int>(), _upDiff.GetValue<int>(), _upDiff.GetValue<int>());

            Cv2.FloodFill(result, new Point(seedX, seedY), newVal, out _, loDiff, upDiff);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Flood Fill error: {ex.Message}";
        }
    }
}
