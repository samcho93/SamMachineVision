using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Threshold;

[NodeInfo("Adaptive Threshold", NodeCategories.Threshold, Description = "Apply adaptive threshold")]
public class AdaptiveThresholdNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _maxVal = null!;
    private NodeProperty _method = null!;
    private NodeProperty _type = null!;
    private NodeProperty _blockSize = null!;
    private NodeProperty _c = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _maxVal = AddDoubleProperty("MaxVal", "Max Value", 255.0, 0.0, 255.0, "Maximum value");
        _method = AddEnumProperty("Method", "Method", AdaptiveThresholdTypes.MeanC, "Adaptive method");
        _type = AddEnumProperty("Type", "Type", ThresholdTypes.Binary, "Threshold type");
        _blockSize = AddIntProperty("BlockSize", "Block Size", 11, 3, 99, "Block size (odd number)");
        _c = AddDoubleProperty("C", "C", 2.0, -100.0, 100.0, "Constant subtracted from mean");
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

            // Ensure grayscale
            var gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var maxVal = _maxVal.GetValue<double>();
            var method = _method.GetValue<AdaptiveThresholdTypes>();
            var type = _type.GetValue<ThresholdTypes>();
            var blockSize = _blockSize.GetValue<int>();
            var c = _c.GetValue<double>();

            if (blockSize % 2 == 0) blockSize++;
            if (blockSize < 3) blockSize = 3;

            var result = new Mat();
            Cv2.AdaptiveThreshold(gray, result, maxVal, method, type, blockSize, c);

            if (needDispose) gray.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Adaptive Threshold error: {ex.Message}";
        }
    }
}
