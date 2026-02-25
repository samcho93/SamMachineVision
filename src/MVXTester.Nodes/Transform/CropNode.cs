using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Transform;

[NodeInfo("Crop", NodeCategories.Transform, Description = "Crop a region from an image")]
public class CropNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _x = null!;
    private NodeProperty _y = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _x = AddIntProperty("X", "X", 0, 0, 10000, "Crop region X");
        _y = AddIntProperty("Y", "Y", 0, 0, 10000, "Crop region Y");
        _width = AddIntProperty("Width", "Width", 100, 1, 10000, "Crop width");
        _height = AddIntProperty("Height", "Height", 100, 1, 10000, "Crop height");
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

            var x = _x.GetValue<int>();
            var y = _y.GetValue<int>();
            var w = _width.GetValue<int>();
            var h = _height.GetValue<int>();

            // Clamp to image bounds
            x = Math.Max(0, Math.Min(x, image.Width - 1));
            y = Math.Max(0, Math.Min(y, image.Height - 1));
            w = Math.Min(w, image.Width - x);
            h = Math.Min(h, image.Height - y);

            if (w <= 0 || h <= 0)
            {
                Error = "Invalid crop region";
                return;
            }

            var roi = new Rect(x, y, w, h);
            var result = new Mat(image, roi).Clone();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Crop error: {ex.Message}";
        }
    }
}
