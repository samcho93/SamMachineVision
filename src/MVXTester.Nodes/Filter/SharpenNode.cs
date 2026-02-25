using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Sharpen", NodeCategories.Filter, Description = "Sharpen image using unsharp mask")]
public class SharpenNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _amount = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _amount = AddDoubleProperty("Amount", "Amount", 1.0, 0.0, 10.0, "Sharpening amount");
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

            var amount = _amount.GetValue<double>();

            // Unsharp mask: result = image + amount * (image - blurred)
            var blurred = new Mat();
            Cv2.GaussianBlur(image, blurred, new Size(0, 0), 3);

            var result = new Mat();
            Cv2.AddWeighted(image, 1.0 + amount, blurred, -amount, 0, result);

            blurred.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Sharpen error: {ex.Message}";
        }
    }
}
