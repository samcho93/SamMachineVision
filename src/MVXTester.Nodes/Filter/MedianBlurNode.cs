using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Median Blur", NodeCategories.Filter, Description = "Apply median blur filter")]
public class MedianBlurNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _ksize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _ksize = AddIntProperty("KSize", "Kernel Size", 5, 1, 99, "Kernel size (odd number)");
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

            var ksize = _ksize.GetValue<int>();
            if (ksize % 2 == 0) ksize++;

            var result = new Mat();
            Cv2.MedianBlur(image, result, ksize);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Median Blur error: {ex.Message}";
        }
    }
}
