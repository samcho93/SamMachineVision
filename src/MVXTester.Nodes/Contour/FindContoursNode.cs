using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Find Contours", NodeCategories.Contour, Description = "Find contours in a binary image")]
public class FindContoursNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Point[][]> _contoursOutput = null!;
    private NodeProperty _mode = null!;
    private NodeProperty _method = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _contoursOutput = AddOutput<Point[][]>("Contours");
        _mode = AddEnumProperty("Mode", "Retrieval Mode", RetrievalModes.List, "Contour retrieval mode");
        _method = AddEnumProperty("Method", "Approximation", ContourApproximationModes.ApproxSimple, "Contour approximation method");
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

            var mode = _mode.GetValue<RetrievalModes>();
            var method = _method.GetValue<ContourApproximationModes>();

            // Ensure single channel
            Mat binary = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                binary = new Mat();
                Cv2.CvtColor(image, binary, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            Cv2.FindContours(binary, out Point[][] contours, out _, mode, method);

            if (needDispose) binary.Dispose();

            SetOutputValue(_contoursOutput, contours);
            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Find Contours error: {ex.Message}";
        }
    }
}
