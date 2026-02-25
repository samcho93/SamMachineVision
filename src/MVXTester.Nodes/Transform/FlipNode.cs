using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Transform;

public enum FlipDirection
{
    Horizontal,
    Vertical,
    Both
}

[NodeInfo("Flip", NodeCategories.Transform, Description = "Flip image horizontally, vertically, or both")]
public class FlipNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _flipCode = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _flipCode = AddEnumProperty("FlipCode", "Direction", FlipDirection.Horizontal, "Flip direction");
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

            var direction = _flipCode.GetValue<FlipDirection>();
            var flipCode = direction switch
            {
                FlipDirection.Horizontal => FlipMode.Y,
                FlipDirection.Vertical => FlipMode.X,
                FlipDirection.Both => FlipMode.XY,
                _ => FlipMode.Y
            };

            var result = new Mat();
            Cv2.Flip(image, result, flipCode);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Flip error: {ex.Message}";
        }
    }
}
