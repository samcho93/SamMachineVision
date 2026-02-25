using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Box Filter", NodeCategories.Filter, Description = "Apply box (averaging) filter")]
public class BoxFilterNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _kernelW = null!;
    private NodeProperty _kernelH = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _kernelW = AddIntProperty("KernelW", "Kernel Width", 5, 1, 99, "Kernel width");
        _kernelH = AddIntProperty("KernelH", "Kernel Height", 5, 1, 99, "Kernel height");
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

            var kw = _kernelW.GetValue<int>();
            var kh = _kernelH.GetValue<int>();

            var result = new Mat();
            Cv2.BoxFilter(image, result, -1, new Size(kw, kh));

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Box Filter error: {ex.Message}";
        }
    }
}
